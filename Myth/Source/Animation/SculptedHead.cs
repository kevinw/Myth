using System.Numerics;
using Foster.Framework;

namespace Myth;

// A face, each axis -1 .. 1 except MouthOpen (0 closed .. 1 wide). BrowTilt: + inner ends down (anger), - up (worry).
// BrowRaise: the whole brow up (surprise) or down (squint). BrowSkew: + the head's right brow up and left down.
// Smile: + corners up, - a frown. LidClose: -1 wide .. 1 shut, added to the head shape's resting droop. GazeX, GazeY:
// where the pupils lean, head-local, -1 .. 1.
public readonly record struct Expression(float BrowTilt = 0, float BrowRaise = 0, float BrowSkew = 0, float Smile = 0,
	float MouthOpen = 0, float LidClose = 0, float GazeX = 0, float GazeY = 0)
{
	public static Expression Angry => new(1, -0.3f, 0, -1, LidClose: 0.25f);
	public static Expression Surprised => new(0, 1, 0, 0, 0.6f, -1);
	public static Expression Afraid => new(-1, 1, 0, -0.3f, 0.5f, -0.8f);
	public static Expression Sad => new(-1, 0, 0, -0.6f, LidClose: 0.3f);
	public static Expression Smug => new(0, -0.3f, 0.3f, 0.5f, LidClose: 0.35f);
	public static Expression Pained => new(-1, -0.5f, 0, -0.4f, 0.7f, 0.6f);
	public static Expression Skeptical => new(0.2f, 0, 1, -0.2f, LidClose: 0.2f);

	public static Expression Lerp(Expression a, Expression b, float t) => new(float.Lerp(a.BrowTilt, b.BrowTilt, t),
		float.Lerp(a.BrowRaise, b.BrowRaise, t), float.Lerp(a.BrowSkew, b.BrowSkew, t), float.Lerp(a.Smile, b.Smile, t),
		float.Lerp(a.MouthOpen, b.MouthOpen, t), float.Lerp(a.LidClose, b.LidClose, t), float.Lerp(a.GazeX, b.GazeX, t),
		float.Lerp(a.GazeY, b.GazeY, t));

	internal Expression Quantized() => new(Step(BrowTilt), Step(BrowRaise), Step(BrowSkew), Step(Smile), Step(MouthOpen),
		Step(LidClose), Step(GazeX), Step(GazeY));
	static float Step(float value) => MathF.Round(value * 32) / 32;
}

// A head as a small signed distance field -- cranium, jaw, brow, nose, carved eye sockets and mouth -- ray-marched
// orthographically on the GPU (SculptedHead.hlsl) into tiny targets: color shaded like a SculptedBody (sockets and
// mouth read dark by cavity), screen-space normals for the lighting pass, scene-light highlights for the emissive
// pass, and a mask for silhouettes, dilated 1px for outlines unless outline is off (SculptedHeadMask.hlsl). An
// Expression bends the brow and mouth, dents carve spheres out of it and a wobble warps it like jelly. Head-local units are head radii: x is the head's right, y up, z out of the face.
// Re-renders only when its inputs change. A fourth target holds the head-local point each pixel shows, which hits read
// back (PositionTexture) to land dents on exactly what's on screen.
// extent: how far (radii) the shape can reach past the radius; morphs that grow it out need more.
sealed class SculptedHead(GraphicsDevice device, bool outline = true, float extent = SculptedHead.DefaultExtent) : IDisposable
{
	public const float DefaultExtent = 1.45f; // the shape's reach past the radius (nose, jaw), in radii
	const float SplitGap = 0.35f; // radii each half slides out, fully split
	const float MaxStretch = 1.35f; // the most a squash or stretch can grow the head along an axis
	const int MaxLights = SculptedBody.MaxLights;
	public const int MaxDents = 4;
	public const int MaxContacts = 2;
	public const int MaxAdornments = 6;
	public const int MaxJiggles = 6;
	const float WobbleReach = 0.9f; // radii a hit's wobble spreads
	const float MaxJiggle = 0.35f; // of its reach, the furthest a jiggle drags the flesh, so the march stays safe

	static readonly BlendMode Overwrite = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly TextureSampler Nearest = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);
	static readonly TextureSampler Tiled = new(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.Repeat);

	readonly Batcher batcher = new(device, "SculptedHead");
	readonly Material headMaterial = new(null, LoadShader(device, "SculptedHead"));
	readonly Material maskMaterial = new(null, LoadShader(device, "SculptedHeadMask"));
	Target? marched; // color, raw normal (alpha = hit), specular, head-local point on the flesh (alpha = hit)
	Target? dilated; // mask, normal
	int Reach => outline ? 1 : 0; // the mask spreads a pixel past the shape for outlines to hug

	public Texture? ColorTexture => marched?.Attachments[0];
	public Texture? NormalTexture => dilated?.Attachments[1];
	public Texture? MaskTexture => dilated?.Attachments[0];
	public Texture? SpecularTexture => marched?.Attachments[2];
	public Texture? RawNormalTexture => marched?.Attachments[1];
	public Texture? PositionTexture => marched?.Attachments[3];
	public int Size { get; private set; }

	(HeadShape? Shape, int Version, float Radius, float Yaw, float Pitch, float Roll, Expression Face, Color Near,
		Color Core, Color Far, Color Eye, int Shading) last;

	static Shader LoadShader(GraphicsDevice device, string name)
	{
		var file = $"{name}.fragment.{device.Driver.GetShaderExtension()}";
		return new Shader(device, ShaderStage.Fragment, Assets.TryReadCompiledShader(file)
			?? throw new FileNotFoundException($"{name} shader not found at 'Shaders/Compiled/{file}'.", file),
			samplerCount: 1, uniformBufferCount: 1, entryPoint: "fragment_main", name: name);
	}

	// surface: metallic, roughness, specular. worldCenter: where the head's center sits in the world, for the lights
	// and the dither. dents: head-local (center, radius) spheres carved out, their floors shaded toward bruise.
	// wobble: how far (radii) the jelly warp pushes the surface, at wobblePhase. contacts: walls it's pressed against,
	// (screen normal toward the wall, y down; the wall's distance from the center in radii; squash along it, 1 = none).
	// stretch: screen-space squash and stretch, marched rather than scaled, so the pixels stay crisp; clamped to MaxStretch.
	// jiggle: head-local offsets the flesh around each of the FleshAnchors is dragged by. wobbleAt: head-local point the
	// wobble spreads from, zero for all over.
	public void Render(HeadShape shape, float radius, float yaw, float pitch, float roll, Expression face,
		Color near, Color core, Color far, Color eye, BodyShadeMode mode = BodyShadeMode.Banded, Vector4 surface = default,
		ReadOnlySpan<BodyLight> lights = default, Vector2 worldCenter = default, ReadOnlySpan<Vector4> dents = default,
		float wobble = 0, float wobblePhase = 0, Color bruise = default, ReadOnlySpan<Vector4> contacts = default,
		Vector2? stretch = null, Vector4 morph = default, Vector2 swing = default, Vector2 patternBoost = default,
		ReadOnlySpan<Vector3> jiggle = default, Vector3 wobbleAt = default, float anvilHeat = 0)
	{
		dents = dents[..Math.Min(dents.Length, MaxDents)];
		face = face.Quantized();
		morph = (morph * 32).Round() / 32;
		var squish = (Vector2.Clamp(stretch ?? Vector2.One, new(1 / MaxStretch), new(MaxStretch)) * 64).Round() / 64;
		contacts = contacts[..Math.Min(contacts.Length, MaxContacts)];
		var size = TextureSize(radius, extent);
		var textureWorld = (worldCenter - new Vector2(size * 0.5f)).RoundToPoint2();
		var shading = new HashCode();
		shading.Add(mode);
		shading.Add(surface);
		// The dither only moves with the world grid's 4px cycle; lights change highlights whatever they do.
		shading.Add(mode == BodyShadeMode.Dithered ? new Point2(textureWorld.X & 3, textureWorld.Y & 3) : Point2.Zero);
		foreach (var light in lights)
			shading.Add(light);
		if (lights.Length > 0)
			shading.Add(textureWorld);
		shading.Add(wobble);
		shading.Add(wobble > 0 ? wobblePhase : 0);
		wobbleAt = wobble > 0 ? (wobbleAt * 32).Round() / 32 : Vector3.Zero;
		shading.Add(wobbleAt);
		Span<Vector4> jiggles = stackalloc Vector4[MaxJiggles * 2];
		var jiggleCount = PackJiggles(shape, jiggle, jiggles);
		jiggles = jiggles[..(jiggleCount * 2)];
		foreach (var row in jiggles)
			shading.Add(row);
		shading.Add(bruise);
		foreach (var dent in dents)
			shading.Add(dent);
		foreach (var contact in contacts)
			shading.Add(contact);
		shading.Add(squish);
		shading.Add(morph);
		anvilHeat = MathF.Round(anvilHeat * 32) / 32;
		shading.Add(anvilHeat);
		swing = (swing * 32).Round() / 32;
		shading.Add(swing);
		// The pattern spreads and glows past the shape's resting amounts by patternBoost (spread, glow).
		var pattern = shape.Pattern is { Length: > 0 } patternKey ? Assets.Textures2D?.Get(patternKey)?.Texture : null;
		var spread = pattern == null ? 0 : MathF.Round(Math.Clamp(shape.PatternSpread + patternBoost.X, 0, 1) * 32) / 32;
		var glow = MathF.Round(MathF.Max(0, shape.PatternGlow + patternBoost.Y) * 32) / 32;
		shading.Add(pattern);
		shading.Add(spread);
		shading.Add(glow);
		var key = (shape, shape.Version, MathF.Round(radius * 4) / 4, Quantize(yaw), Quantize(pitch), Quantize(roll),
			face, near, core, far, eye, shading.ToHashCode());
		if (marched != null && key == last)
			return;
		last = key;

		if (size != Size || marched == null)
		{
			DisposeTargets();
			Size = size;
			marched = new Target(device, size, size, [TextureFormat.Color, TextureFormat.Color, TextureFormat.Color, TextureFormat.R16G16B16A16Float], name: "SculptedHead");
			dilated = new Target(device, size, size, [TextureFormat.Color, TextureFormat.Color], name: "SculptedHeadMask");
		}

		var (toView, toHead) = Rotations(yaw, pitch, roll);
		Span<Contact> headContacts = stackalloc Contact[contacts.Length];
		for (var i = 0; i < contacts.Length; i++)
			headContacts[i] = new(Vector3.TransformNormal(new(contacts[i].X, -contacts[i].Y, 0), toHead), contacts[i].Z, contacts[i].W);
		var s = shape;
		var lightCount = Math.Min(lights.Length, MaxLights);
		var uniforms = new HeadUniforms
		{
			ToHead0 = new(toHead.M11, toHead.M12, toHead.M13, 0),
			ToHead1 = new(toHead.M21, toHead.M22, toHead.M23, 0),
			ToHead2 = new(toHead.M31, toHead.M32, toHead.M33, 0),
			ToView0 = new(toView.M11, toView.M12, toView.M13, 0),
			ToView1 = new(toView.M21, toView.M22, toView.M23, 0),
			ToView2 = new(toView.M31, toView.M32, toView.M33, 0),
			Frame = new(size * 0.5f, radius, face.MouthOpen, extent),
			Cranium = new(s.CraniumWidth, s.CraniumHeight, s.CraniumDepth, s.CraniumLift),
			Jaw = new(s.JawWidth, s.JawHeight, s.JawDepth, s.JawDrop),
			Jaw2 = new(s.JawForward, s.JawBlend, s.BrowSize, s.BrowWidth),
			Brow = new(s.BrowHeight, s.BrowForward, s.NoseSize, s.NoseLength),
			Nose = new(s.NoseHeight, s.NoseDroop, s.NoseFlat, s.NostrilSize),
			Eyes = new(s.EyeSpacing, s.EyeHeight, s.EyeSize, s.EyeDepth),
			Mouth = new(s.MouthWidth, s.MouthHeight, s.MouthSize, s.EarSize),
			Ears = new(s.EarHeight, anvilHeat, 0, 0),
			Near = (Vector4)near,
			Core = (Vector4)core,
			Far = (Vector4)far,
			Eye = (Vector4)eye,
			Shading = new((int)mode, textureWorld.X, textureWorld.Y, lightCount),
			Surface = surface,
			Deform = new(wobble, wobblePhase, dents.Length, contacts.Length),
			Bruise = (Vector4)bruise,
			Stretch = new(squish, 0, 0),
			Morph = morph,
			Pattern = new(shape.PatternScale, spread, glow, shape.PatternInvert ? 1 : 0),
			PatternColor = (Vector4)shape.PatternColor,
			Flesh = new(wobbleAt, jiggleCount),
			Face = new(face.BrowTilt, face.BrowRaise, face.BrowSkew, face.Smile),
			Look = new(Math.Clamp(s.LidDroop + face.LidClose, 0, 1), face.GazeX * 0.6f, face.GazeY * 0.5f, 0),
			Features = new(MathF.Min(s.EyeballSize, s.EyeSize * 0.9f), s.IrisSize, s.TeethSize, s.LipSize),
			Hair = new(s.HairColor.ToVector3(), s.BrowHair),
			Lips = (Vector4)s.LipColor,
			Teeth = (Vector4)s.TeethColor,
			Sclera = (Vector4)s.ScleraColor,
			Iris = (Vector4)s.IrisColor,
		};
		for (var i = 0; i < dents.Length; i++)
			uniforms.Dents[i] = dents[i];
		for (var i = 0; i < headContacts.Length; i++)
		{
			uniforms.Contacts[i * 2] = new(headContacts[i].Normal, headContacts[i].Wall);
			uniforms.Contacts[i * 2 + 1] = new(headContacts[i].Squash, 0, 0, 0);
		}
		uniforms.Adorn = new(PackAdornments(s, uniforms.Adornments), swing.X, swing.Y, 0);
		for (var i = 0; i < (int)uniforms.Adorn.X; i++)
		{
			var at = Xyz(uniforms.Adornments[i * 4]);
			var ride = Flesh(at, jiggles, wobble, wobblePhase, wobbleAt) - at;
			uniforms.Adornments[i * 4 + 3] = new(uniforms.Adornments[i * 4 + 3].X, ride.X, ride.Y, ride.Z);
		}
		for (var i = 0; i < 4; i++)
		{
			var at = RideAnchor(s, i);
			uniforms.Rides[i] = new(Flesh(at, jiggles, wobble, wobblePhase, wobbleAt) - at, 0);
		}
		for (var i = 0; i < jiggles.Length; i++)
			uniforms.Jiggles[i] = jiggles[i];
		for (var i = 0; i < lightCount; i++)
		{
			var light = lights[i];
			uniforms.Lights[i * 2] = new(light.Position - textureWorld, light.Height, light.Radius);
			uniforms.Lights[i * 2 + 1] = new(light.Color, 0);
		}
		headMaterial.Fragment.SetUniformBuffer(uniforms);
		batcher.PushBlend(Overwrite);
		batcher.PushMaterial(headMaterial);
		// The pattern rides in as the quad's texture, which the shader samples in head space.
		if (pattern != null)
		{
			batcher.PushSampler(Tiled);
			batcher.Image(pattern, Vector2.Zero, Vector2.Zero, new Vector2(size) / new Vector2(pattern.Width, pattern.Height), 0, Color.White);
			batcher.PopSampler();
		}
		else
			batcher.Rect(new Rect(0, 0, size, size), Color.White);
		batcher.PopMaterial();
		batcher.PopBlend();
		batcher.Render(marched);
		batcher.Clear();

		maskMaterial.Fragment.SetUniformBuffer(new Vector4(size, size, 0, Reach));
		batcher.PushBlend(Overwrite);
		batcher.PushMaterial(maskMaterial);
		batcher.PushSampler(Nearest);
		batcher.Image(marched!.Attachments[1], Vector2.Zero, Color.White);
		batcher.PopSampler();
		batcher.PopMaterial();
		batcher.PopBlend();
		batcher.Render(dilated!);
		batcher.Clear();
	}

	// Mirrors SculptedHead.hlsl's FragmentUniformBlock.
	struct HeadUniforms
	{
		public Vector4 ToHead0, ToHead1, ToHead2, ToView0, ToView1, ToView2, Frame, Cranium, Jaw, Jaw2, Brow, Nose, Eyes,
			Mouth, Ears, Near, Core, Far, Eye, Shading, Surface, Deform, Bruise, Stretch, Morph, Face, Look, Features, Hair, Lips, Teeth, Sclera, Iris, Adorn, Pattern, PatternColor, Flesh;
		public LightArray Lights;
		public DentArray Dents;
		public ContactArray Contacts;
		public AdornmentArray Adornments;
		public RideArray Rides;
		public JiggleArray Jiggles;
	}

	[System.Runtime.CompilerServices.InlineArray(MaxLights * 2)]
	struct LightArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxDents)]
	struct DentArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxContacts * 2)]
	struct ContactArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxAdornments * 4)]
	struct AdornmentArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(4)]
	struct RideArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxJiggles * 2)]
	struct JiggleArray { Vector4 first; }

	// The flesh that jiggles, head-local (anchor, reach), reach 0 where the shape has none: left and right jowls, left
	// and right ears, the nose's tip, the brow's middle.
	public static void FleshAnchors(HeadShape s, Span<Vector4> into)
	{
		var jaw = new Vector3(0, -s.JawDrop, s.JawForward) + new Vector3(s.JawWidth * 0.62f, -s.JawHeight * 0.25f, s.JawDepth * 0.45f);
		var ear = new Vector3(s.CraniumWidth, s.EarHeight, -0.05f);
		var flip = new Vector3(-1, 1, 1);
		into[0] = new(jaw * flip, s.JawWidth * 0.55f);
		into[1] = new(jaw, s.JawWidth * 0.55f);
		into[2] = new(ear * flip, s.EarSize > 0 ? s.EarSize * 1.4f + 0.1f : 0);
		into[3] = new(ear, into[2].W);
		into[4] = new(0, s.NoseHeight - s.NoseDroop, 0.88f + s.NoseLength, s.NoseSize > 0 ? s.NoseSize * 2 + 0.12f : 0);
		into[5] = new(0, s.BrowHeight, s.BrowForward, s.BrowSize > 0 ? MathF.Max(s.BrowWidth, 0.3f) : 0);
	}

	// The anchors and offsets of the jiggles that move, as shader rows, each offset easing into MaxJiggle of its reach.
	// Returns how many.
	static int PackJiggles(HeadShape s, ReadOnlySpan<Vector3> jiggle, Span<Vector4> into)
	{
		Span<Vector4> anchors = stackalloc Vector4[MaxJiggles];
		FleshAnchors(s, anchors);
		var count = 0;
		for (var i = 0; i < Math.Min(jiggle.Length, MaxJiggles); i++)
		{
			var reach = anchors[i].W;
			var offset = (jiggle[i] * 64).Round() / 64;
			if (reach <= 0 || offset == Vector3.Zero)
				continue;
			var limit = reach * MaxJiggle;
			offset *= limit * MathF.Tanh(offset.Length() / limit) / offset.Length();
			into[count * 2] = anchors[i];
			into[count++ * 2 + 1] = new(offset, 0);
		}
		return count;
	}

	// Where the rigid parts riding the flesh hang from: left eye, right eye, teeth, anvil.
	static Vector3 RideAnchor(HeadShape s, int i) => i switch
	{
		< 2 => Socket(s, i * 2 - 1) - new Vector3(0, 0, (s.EyeSize - MathF.Min(s.EyeballSize, s.EyeSize * 0.9f)) * 0.5f),
		2 => new(0, s.MouthHeight, 0.8f),
		_ => new(0, -s.JawDrop - s.JawHeight, s.JawForward * 0.5f),
	};

	// The anvil's pieces in the order they're forged, top down, as where each grows from below its ride anchor: neck,
	// block, horn, heel, then the striking face's three columns of studs. Matches SculptedHead.hlsl's anvil().
	internal static readonly Vector3[] AnvilAnchors =
		[new(0, 0.02f, 0), new(0, -0.34f, 0), new(0.6f, -0.46f, 0), new(-0.88f, -0.42f, 0), new(-0.5f, -0.76f, 0), new(0, -0.76f, 0), new(0.5f, -0.76f, 0)];
	const float AnvilStagger = 0.11f, AnvilForm = 0.34f;

	// How many of the anvil's pieces are fully grown at this much assembly.
	internal static int AnvilFormed(float assembly) => Math.Clamp((int)MathF.Floor((assembly - AnvilForm) / AnvilStagger) + 1, 0, AnvilAnchors.Length);

	internal static Vector3 AnvilInView(HeadShape s, int i, float yaw, float pitch, float roll)
		=> Vector3.Transform(RideAnchor(s, 3) + AnvilAnchors[i], Rotations(yaw, pitch, roll).ToView);

	static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

	// Each adornment (and its mirror) as four rows: (point, kind), (ring axis or horn direction, size), (color, metal),
	// (thickness, 0, 0, 0). Returns how many slots it filled.
	static int PackAdornments(HeadShape s, Span<Vector4> into)
	{
		var count = 0;
		foreach (var adornment in s.Adornments)
			for (var side = 1; side >= (adornment.Mirror ? -1 : 1) && count < MaxAdornments; side -= 2, count++)
			{
				var (yaw, pitch) = (adornment.Yaw * MathF.PI / 180, adornment.Pitch * MathF.PI / 180);
				var direction = adornment.Kind == AdornmentKind.Ring ? new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw))
					: new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
				var flip = new Vector3(side, 1, 1);
				into[count * 4] = new(new Vector3(adornment.X, adornment.Y, adornment.Z) * flip, (float)adornment.Kind);
				into[count * 4 + 1] = new(direction * flip, adornment.Size);
				into[count * 4 + 2] = new(adornment.Color.ToVector3(), adornment.Metal ? 1 : 0);
				into[count * 4 + 3] = new(adornment.Thickness, 0, 0, 0);
			}
		return count;
	}

	// A wall the head's pressed against, head-local: the normal toward it, its distance (radii), the squash along it.
	readonly record struct Contact(Vector3 Normal, float Wall, float Squash);

	static float Quantize(float angle) => MathF.Round(angle * 180 / MathF.PI * 2) / 2;

	// Even, so a centered draw's origin lands on a texel corner and nearest sampling maps texels 1:1.
	static int TextureSize(float radius, float extent) => ((int)MathF.Ceiling(radius * extent * MaxStretch) + 2) * 2;

	// View space: x right, y up, z toward the viewer. Head-local -> view is roll (screen) * pitch * yaw.
	internal static (Matrix4x4 ToView, Matrix4x4 ToHead) Rotations(float yaw, float pitch, float roll)
	{
		var toView = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationZ(-roll);
		Matrix4x4.Invert(toView, out var toHead);
		return (toView, toHead);
	}

	static Vector3 Socket(HeadShape s, float side) => new(s.EyeSpacing * side, s.EyeHeight, s.EyeDepth);

	// Where an eye (side -1 or 1) sits in view space, in radii, y up: turned with the head and slid out with its half.
	internal static Vector3 EyeInView(HeadShape s, float side, float yaw, float pitch, float roll, float split)
		=> Vector3.Transform(Socket(s, side) + new Vector3(side * split * SplitGap, 0, 0), Rotations(yaw, pitch, roll).ToView);

	// Where p sits on the resting flesh: dragged back by each jiggle around its anchor, then wobbling, all over or fading
	// out from wobbleAt. Rigid parts ride: carried whole by the flesh at their anchor, Flesh(anchor) - anchor.
	static Vector3 Flesh(Vector3 p, ReadOnlySpan<Vector4> jiggles, float wobble, float phase, Vector3 wobbleAt)
	{
		var q = p;
		for (var i = 0; i < jiggles.Length; i += 2)
		{
			var s = Math.Clamp(1 - Vector3.Distance(p, Xyz(jiggles[i])) / jiggles[i].W, 0, 1);
			q -= Xyz(jiggles[i + 1]) * (s * s * (3 - 2 * s));
		}
		if (wobble <= 0)
			return q;
		var amount = wobbleAt != Vector3.Zero ? wobble * Math.Clamp(1 - Vector3.Distance(q, wobbleAt) / WobbleReach, 0, 1) : wobble;
		return q + amount * new Vector3(MathF.Sin(q.Y * 5 + phase), MathF.Sin(q.Z * 5 + phase + 2.1f), MathF.Sin(q.X * 5 + phase + 4.2f));
	}

	void DisposeTargets()
	{
		marched?.Dispose();
		dilated?.Dispose();
		marched = dilated = null;
	}

	public void Dispose()
	{
		DisposeTargets();
		batcher.Dispose();
		headMaterial.Fragment.Shader?.Dispose();
		maskMaterial.Fragment.Shader?.Dispose();
	}
}
