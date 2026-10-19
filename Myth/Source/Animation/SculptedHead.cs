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
// Re-renders only when its inputs change. The CPU version below is the reference the shape is verified against
// (Verify) and where hits land (Raycast); keep the two in step.
// extent: how far (radii) the shape can reach past the radius; morphs that grow it out need more.
sealed class SculptedHead(GraphicsDevice device, bool outline = true, float extent = SculptedHead.DefaultExtent) : IDisposable
{
	public const float DefaultExtent = 1.45f; // the shape's reach past the radius (nose, jaw), in radii
	const float SplitGap = 0.35f; // radii each half slides out, fully split
	const float CoreRadius = 0.42f;
	const int MaxSteps = 40;
	const float MaxStretch = 1.35f; // the most a squash or stretch can grow the head along an axis
	const int MaxLights = SculptedBody.MaxLights;
	public const int MaxDents = 4;
	public const int MaxContacts = 2;
	public const int MaxAdornments = 6;

	static readonly BlendMode Overwrite = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly TextureSampler Nearest = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);

	readonly Batcher batcher = new(device, "SculptedHead");
	readonly Material headMaterial = new(null, LoadShader(device, "SculptedHead"));
	readonly Material maskMaterial = new(null, LoadShader(device, "SculptedHeadMask"));
	Target? marched; // color, raw normal (alpha = hit), specular
	Target? dilated; // mask, normal
	int Reach => outline ? 1 : 0; // the mask spreads a pixel past the shape for outlines to hug

	public Texture? ColorTexture => marched?.Attachments[0];
	public Texture? NormalTexture => dilated?.Attachments[1];
	public Texture? MaskTexture => dilated?.Attachments[0];
	public Texture? SpecularTexture => marched?.Attachments[2];
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
	public void Render(HeadShape shape, float radius, float yaw, float pitch, float roll, Expression face,
		Color near, Color core, Color far, Color eye, BodyShadeMode mode = BodyShadeMode.Banded, Vector4 surface = default,
		ReadOnlySpan<BodyLight> lights = default, Vector2 worldCenter = default, ReadOnlySpan<Vector4> dents = default,
		float wobble = 0, float wobblePhase = 0, Color bruise = default, ReadOnlySpan<Vector4> contacts = default,
		Vector2? stretch = null, Vector4 morph = default, Vector2 swing = default)
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
		shading.Add(bruise);
		foreach (var dent in dents)
			shading.Add(dent);
		foreach (var contact in contacts)
			shading.Add(contact);
		shading.Add(squish);
		shading.Add(morph);
		swing = (swing * 32).Round() / 32;
		shading.Add(swing);
		var key = (shape, shape.Version, MathF.Round(radius * 4) / 4, Quantize(yaw), Quantize(pitch), Quantize(roll),
			face, near, core, far, eye, shading.ToHashCode());
		if (marched != null && key == last)
			return;
		last = key;

		if (size != Size || marched == null)
		{
			DisposeTargets();
			Size = size;
			marched = new Target(device, size, size, [TextureFormat.Color, TextureFormat.Color, TextureFormat.Color], name: "SculptedHead");
			dilated = new Target(device, size, size, [TextureFormat.Color, TextureFormat.Color], name: "SculptedHeadMask");
		}

		var (toView, toHead) = Rotations(yaw, pitch, roll);
		Span<Contact> headContacts = stackalloc Contact[contacts.Length];
		for (var i = 0; i < contacts.Length; i++)
			headContacts[i] = new(Vector3.TransformNormal(new(contacts[i].X, -contacts[i].Y, 0), toHead), contacts[i].Z, contacts[i].W);
#if DEBUG
		lastInputs = (radius, yaw, pitch, roll, face, morph, swing, dents.ToArray(), wobble, wobblePhase, headContacts.ToArray(), squish);
#endif
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
			Ears = new(s.EarHeight, 0, 0, 0),
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
		for (var i = 0; i < lightCount; i++)
		{
			var light = lights[i];
			uniforms.Lights[i * 2] = new(light.Position - textureWorld, light.Height, light.Radius);
			uniforms.Lights[i * 2 + 1] = new(light.Color, 0);
		}
		headMaterial.Fragment.SetUniformBuffer(uniforms);
		batcher.PushBlend(Overwrite);
		batcher.PushMaterial(headMaterial);
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
			Mouth, Ears, Near, Core, Far, Eye, Shading, Surface, Deform, Bruise, Stretch, Morph, Face, Look, Features, Hair, Lips, Teeth, Sclera, Iris, Adorn;
		public LightArray Lights;
		public DentArray Dents;
		public ContactArray Contacts;
		public AdornmentArray Adornments;
	}

	[System.Runtime.CompilerServices.InlineArray(MaxLights * 2)]
	struct LightArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxDents)]
	struct DentArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxContacts * 2)]
	struct ContactArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxAdornments * 4)]
	struct AdornmentArray { Vector4 first; }

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

#if DEBUG
	(float Radius, float Yaw, float Pitch, float Roll, Expression Face, Vector4 Morph, Vector2 Swing, Vector4[] Dents, float Wobble, float Phase,
		Contact[] Contacts, Vector2 Stretch) lastInputs;

	// Compares the last GPU render against the CPU reference: per texture, how many pixels differ by more than a
	// rounding step, and the worst channel difference. With a directory, writes both versions as PNGs side by side.
	public string Verify(string? directory = null)
	{
		if (marched == null || last.Shape is not { } shape)
			return "nothing rendered yet";
		var (radius, yaw, pitch, roll, face, morph, swing, dents, wobble, phase, contacts, stretch) = lastInputs;
		var (normals, mask) = RenderReference(new(shape, face, morph, swing, dents, wobble, phase, contacts), radius, yaw, pitch, roll,
			stretch, Reach, extent);
		var report = new System.Text.StringBuilder($"{Size}x{Size}");
		Compare("normal", NormalTexture!, normals);
		Compare("mask", MaskTexture!, mask);
		return report.ToString();

		void Compare(string name, Texture texture, Color[] reference)
		{
			var gpu = new Color[reference.Length];
			texture.GetData<Color>(gpu);
			int differing = 0, worst = 0;
			for (var i = 0; i < gpu.Length; i++)
			{
				var diff = Math.Max(Math.Max(Math.Abs(gpu[i].R - reference[i].R), Math.Abs(gpu[i].G - reference[i].G)),
					Math.Max(Math.Abs(gpu[i].B - reference[i].B), Math.Abs(gpu[i].A - reference[i].A)));
				worst = Math.Max(worst, diff);
				if (diff > 2)
					differing++;
			}
			report.Append($" | {name}: {differing}/{gpu.Length} px differ, worst {worst}");
			if (directory == null)
				return;
			var both = new Color[Size * 2 * Size];
			for (var y = 0; y < Size; y++)
			{
				Array.Copy(gpu, y * Size, both, y * Size * 2, Size);
				Array.Copy(reference, y * Size, both, y * Size * 2 + Size, Size);
			}
			Directory.CreateDirectory(directory);
			new Image(Size * 2, Size, both).WritePng(Path.Join(directory, $"head-{name}.png"));
		}
	}

	// The CPU ray-march the shaders mirror, pixel for pixel.
	static (Color[] Normals, Color[] Mask) RenderReference(Field s, float radius, float yaw, float pitch, float roll,
		Vector2 stretch, int reach, float extent)
	{
		var size = TextureSize(radius, extent);
		var normals = new Color[size * size];
		var mask = new Color[size * size];
		var hit = new bool[size * size];
		var (toView, toHead) = Rotations(yaw, pitch, roll);
		var rayDirection = Vector3.TransformNormal(-Vector3.UnitZ, toHead);
		var half = size * 0.5f;
		for (var y = 0; y < size; y++)
			for (var x = 0; x < size; x++)
			{
				var i = y * size + x;
				// Rays that miss the bounding sphere can't hit; the rest start where they enter it.
				var across = new Vector2((x + 0.5f - half) / radius, -(y + 0.5f - half) / radius) / stretch;
				var depth = extent * extent - across.LengthSquared();
				if (depth <= 0)
					continue;
				depth = MathF.Sqrt(depth);
				var position = Vector3.Transform(new Vector3(across, depth), toHead);
				var travelled = 0f;
				var found = false;
				for (var step = 0; step < MaxSteps && travelled < depth * 2; step++)
				{
					var distance = Distance(position, s);
					if (distance < 0.004f)
					{
						found = true;
						break;
					}
					distance *= s.Wobble > 0 ? 0.7f : 1;
					position += rayDirection * distance;
					travelled += distance;
				}
				if (!found)
					continue;

				hit[i] = true;
				var normal = SurfaceNormal(position, s);
				var viewNormal = Vector3.TransformNormal(normal, toView);
				viewNormal = Vector3.Normalize(new(new Vector2(viewNormal.X, viewNormal.Y) / stretch, viewNormal.Z));
				// Screen normals: x right, y down, z toward the viewer.
				normals[i] = new Color(viewNormal.X * 0.5f + 0.5f, -viewNormal.Y * 0.5f + 0.5f, viewNormal.Z * 0.5f + 0.5f, 1f);
			}

		// The mask: the shape dilated by reach, so outlines and silhouettes hug it.
		for (var y = 0; y < size; y++)
			for (var x = 0; x < size; x++)
			{
				var covered = false;
				for (var dy = -reach; dy <= reach && !covered; dy++)
					for (var dx = -reach; dx <= reach && !covered; dx++)
					{
						var nx = x + dx;
						var ny = y + dy;
						covered = nx >= 0 && ny >= 0 && nx < size && ny < size && hit[ny * size + nx];
					}
				if (!covered)
					continue;
				var i = y * size + x;
				mask[i] = Color.White;
				if (!hit[i])
				{
					var outward = new Vector2(x + 0.5f - half, y + 0.5f - half).Normalized(Vector2.UnitY);
					normals[i] = RigDraw.ScreenNormalColor(outward);
				}
			}
		return (normals, mask);
	}
#endif

	readonly record struct Field(HeadShape Shape, Expression Face, Vector4 Morph, Vector2 Swing, Vector4[] Dents, float Wobble, float Phase, Contact[] Contacts);

	// Where a view ray through `across` (radii from the center, y up) first meets the head, in head-local units, and the
	// surface normal there. Falls back toward the center when it misses the silhouette.
	public static bool Raycast(HeadShape shape, Vector2 across, float yaw, float pitch, float roll, out Vector3 point,
		out Vector3 normal)
	{
		var (_, toHead) = Rotations(yaw, pitch, roll);
		var rayDirection = Vector3.TransformNormal(-Vector3.UnitZ, toHead);
		for (var attempt = 0; attempt <= 8; attempt++, across *= 0.8f)
		{
			var depth = DefaultExtent * DefaultExtent - across.LengthSquared();
			if (depth <= 0)
				continue;
			depth = MathF.Sqrt(depth);
			point = Vector3.Transform(new Vector3(across, depth), toHead);
			for (var (step, travelled) = (0, 0f); step < MaxSteps && travelled < depth * 2; step++)
			{
				var distance = Distance(point, shape, default, default, default);
				if (distance < 0.004f)
				{
					normal = SurfaceNormal(point, new(shape, default, default, default, [], 0, 0, []));
					return true;
				}
				point += rayDirection * distance;
				travelled += distance;
			}
		}
		(point, normal) = (Vector3.Zero, Vector3.UnitZ);
		return false;
	}

	static Vector3 Socket(HeadShape s, float side) => new(s.EyeSpacing * side, s.EyeHeight, s.EyeDepth);

	static Vector3 Wobble(Vector3 p, Field f) => p + f.Wobble * new Vector3(MathF.Sin(p.Y * 5 + f.Phase),
		MathF.Sin(p.Z * 5 + f.Phase + 2.1f), MathF.Sin(p.X * 5 + f.Phase + 4.2f));

	// Squashed toward each contact's wall, bulging the rest to keep the volume, then flattened where it meets the wall.
	static float Distance(Vector3 p, Field f)
	{
		var (q, scale) = (p, 1f);
		foreach (var contact in f.Contacts)
		{
			var along = Vector3.Dot(q, contact.Normal);
			var anchor = MathF.Min(contact.Wall, 1);
			q = (q - contact.Normal * along) * MathF.Sqrt(contact.Squash) + contact.Normal * (anchor + (along - anchor) / contact.Squash);
			scale *= MathF.Min(contact.Squash, 1 / MathF.Sqrt(contact.Squash));
		}
		var d = MorphedDistance(q, f) * scale;
		foreach (var contact in f.Contacts)
			d = SmoothSubtract(d, contact.Wall - Vector3.Dot(p, contact.Normal), 0.08f);
		return d;
	}

	static float Distance(Vector3 p, HeadShape s, Expression face, Vector4 morph, Vector2 swing)
	{
		var cranium = Ellipsoid(p - new Vector3(0, s.CraniumLift, -0.08f), new Vector3(s.CraniumWidth, s.CraniumHeight, s.CraniumDepth));
		var jaw = Ellipsoid(p - new Vector3(0, -s.JawDrop, s.JawForward), new Vector3(s.JawWidth, s.JawHeight, s.JawDepth));
		var d = SmoothUnion(cranium, jaw, MathF.Max(0.01f, s.JawBlend));
		if (morph.Y > 0)
			d = SmoothUnion(d, MathF.Min(Sphere(p - new Vector3(-0.5f, -0.22f, 0.5f), 0.3f * morph.Y),
				Sphere(p - new Vector3(0.5f, -0.22f, 0.5f), 0.3f * morph.Y)), 0.15f);
		if (morph.X > 0)
		{
			var a = morph.X;
			var top = new Vector3(0, -s.JawDrop - s.JawHeight, s.JawForward * 0.5f);
			var neck = RoundBox(p - top + new Vector3(0, 0.14f * a, 0), new Vector3(0.35f, 0.16f, 0.32f) * a, 0.04f * a);
			var block = RoundBox(p - top + new Vector3(0, 0.48f * a, 0), new Vector3(0.85f, 0.2f, 0.6f) * a, 0.05f * a);
			d = SmoothUnion(d, SmoothUnion(neck, block, 0.06f * a), 0.1f);
		}
		if (s.BrowSize > 0)
		{
			var ends = s.BrowHeight + face.BrowRaise * 0.08f + face.BrowTilt * 0.03f;
			var middle = new Vector3(0, s.BrowHeight + face.BrowRaise * 0.08f - face.BrowTilt * 0.14f,
				s.BrowForward + MathF.Max(face.BrowTilt, 0) * 0.06f);
			d = SmoothUnion(d, BentCapsule(p, new(-s.BrowWidth, ends - face.BrowSkew * 0.08f, s.BrowForward), middle,
				new(s.BrowWidth, ends + face.BrowSkew * 0.08f, s.BrowForward), s.BrowSize, face.BrowTilt != 0), 0.1f);
		}
		if (s.NoseSize > 0)
		{
			var root = new Vector3(0, s.NoseHeight, 0.88f);
			var tip = root + new Vector3(0, -s.NoseDroop, s.NoseLength);
			var nose = Capsule(p, root, tip, s.NoseSize);
			if (s.NoseFlat > 0 && tip != root)
			{
				var edge = s.NoseSize * 0.25f;
				nose = float.Lerp(nose, Cylinder(p, root, tip, s.NoseSize - edge) - edge, s.NoseFlat);
			}
			d = SmoothUnion(d, nose, 0.07f);
			if (s.NostrilSize > 0)
			{
				// The nostrils sit in the nose's end face, wherever it points.
				var end = tip + (tip - root).Normalized(Vector3.UnitZ) * (s.NoseFlat > 0 ? 0 : s.NoseSize);
				var apart = new Vector3(s.NoseSize * 0.45f, 0, 0);
				d = SmoothSubtract(d, MathF.Min(Sphere(p - end - apart, s.NostrilSize), Sphere(p - end + apart, s.NostrilSize)), 0.03f);
			}
		}
		if (s.EarSize > 0)
		{
			var ear = new Vector3(s.CraniumWidth, s.EarHeight, -0.05f);
			var ears = MathF.Min(Ellipsoid(p - ear, new Vector3(0.35f, 1, 0.7f) * s.EarSize),
				Ellipsoid(p - ear * new Vector3(-1, 1, 1), new Vector3(0.35f, 1, 0.7f) * s.EarSize));
			d = SmoothUnion(d, ears, 0.05f);
		}
		if (s.EyeSize > 0)
		{
			d = SmoothSubtract(d, MathF.Min(Sphere(p - Socket(s, -1), s.EyeSize), Sphere(p - Socket(s, 1), s.EyeSize)), 0.05f);
			var eyeball = MathF.Min(s.EyeballSize, s.EyeSize * 0.9f);
			if (eyeball > 0)
			{
				// Eyeballs sit back in the sockets, the lids slide down over them from above as they close.
				var close = Math.Clamp(s.LidDroop + face.LidClose, 0, 1);
				var lid = eyeball * 1.12f;
				foreach (var side in (ReadOnlySpan<float>)[-1, 1])
				{
					var q = p - Socket(s, side) + new Vector3(0, 0, (s.EyeSize - eyeball) * 0.5f);
					d = MathF.Min(d, MathF.Min(Sphere(q, eyeball), MathF.Max(Sphere(q, lid), lid * (1 - 2 * close) - q.Y)));
				}
			}
		}
		if (s.MouthWidth > 0)
		{
			var radius = s.MouthSize + face.MouthOpen * 0.14f;
			var corner = new Vector3(s.MouthWidth, s.MouthHeight + face.Smile * 0.07f, 0.86f);
			d = SmoothSubtract(d, BentCapsule(p, corner * new Vector3(-1, 1, 1), new(0, s.MouthHeight - face.Smile * 0.03f, 0.86f),
				corner, radius, face.Smile != 0), 0.04f);
			if (s.TeethSize > 0)
			{
				// Upper teeth hang from the top of the mouth's cut, lower stand on its floor; clenched they fill the slit.
				var half = new Vector3(s.MouthWidth * 0.8f, s.TeethSize * 0.5f, 0.05f);
				var upper = RoundBox(p - new Vector3(0, s.MouthHeight + radius - s.TeethSize * 0.5f, 0.8f), half, 0.015f);
				var lower = RoundBox(p - new Vector3(0, s.MouthHeight - radius + s.TeethSize * 0.5f, 0.8f), half, 0.015f);
				d = MathF.Min(d, MathF.Min(upper, lower));
			}
		}
		Span<Vector4> adornments = stackalloc Vector4[MaxAdornments * 4];
		for (var i = PackAdornments(s, adornments) - 1; i >= 0; i--)
			d = MathF.Min(d, Adornment(p, adornments.Slice(i * 4, 4), swing));
		return d;
	}

	// Split open, each half keeps only its own side, and a glowing core hangs in the gap.
	static float MorphedDistance(Vector3 p, Field f)
	{
		if (f.Morph.Z <= 0)
			return FaceDistance(p, f);
		var q = Unsplit(p, f.Morph.Z);
		return MathF.Min(MathF.Max(FaceDistance(q, f), -q.X * MathF.Sign(p.X)), Sphere(p, CoreRadius * f.Morph.Z));
	}

	static Vector3 Unsplit(Vector3 p, float split) => p - new Vector3(MathF.Sign(p.X) * split * SplitGap, 0, 0);

	static float FaceDistance(Vector3 p, Field f)
	{
		var w = Wobble(p, f);
		var d = Distance(w, f.Shape, f.Face, f.Morph, f.Swing);
		foreach (var dent in f.Dents)
			d = SmoothSubtract(d, Sphere(w - new Vector3(dent.X, dent.Y, dent.Z), dent.W), 0.06f);
		return d;
	}

	static Vector3 SurfaceNormal(Vector3 p, Field s)
	{
		const float e = 0.01f;
		var a = new Vector3(1, -1, -1);
		var b = new Vector3(-1, -1, 1);
		var c = new Vector3(-1, 1, -1);
		var d = new Vector3(1, 1, 1);
		return Vector3.Normalize(a * Distance(p + a * e, s) + b * Distance(p + b * e, s) +
			c * Distance(p + c * e, s) + d * Distance(p + d * e, s));
	}

	static float Sphere(Vector3 p, float radius) => p.Length() - radius;

	static float Adornment(Vector3 p, ReadOnlySpan<Vector4> rows, Vector2 swing)
	{
		var at = new Vector3(rows[0].X, rows[0].Y, rows[0].Z);
		var direction = new Vector3(rows[1].X, rows[1].Y, rows[1].Z);
		var (size, thickness) = (rows[1].W, rows[3].X);
		switch (rows[0].W)
		{
			case < 0.5f:
				var q = p - (at + Vector3.Normalize(new Vector3(swing.X, -1, swing.Y)) * size);
				var along = Vector3.Dot(q, direction);
				return new Vector2((q - direction * along).Length() - size, along).Length() - thickness;
			case < 1.5f:
				return Sphere(p - at, size);
			default:
				return RoundCone(p, at, at + direction * size, thickness, thickness * 0.15f);
		}
	}

	static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
	{
		var ba = b - a;
		var l2 = Vector3.Dot(ba, ba);
		var rr = r1 - r2;
		var a2 = l2 - rr * rr;
		var il2 = 1 / l2;
		var pa = p - a;
		var y = Vector3.Dot(pa, ba);
		var z = y - l2;
		var xv = pa * l2 - ba * y;
		var x2 = Vector3.Dot(xv, xv);
		var y2 = y * y * l2;
		var z2 = z * z * l2;
		var k = MathF.Sign(rr) * rr * rr * x2;
		if (MathF.Sign(z) * a2 * z2 > k)
			return MathF.Sqrt(x2 + z2) * il2 - r2;
		if (MathF.Sign(y) * a2 * y2 < k)
			return MathF.Sqrt(x2 + y2) * il2 - r1;
		return (MathF.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
	}

	static float RoundBox(Vector3 p, Vector3 half, float rounding)
	{
		var q = Vector3.Abs(p) - half + new Vector3(rounding);
		return Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0) - rounding;
	}

	static float Ellipsoid(Vector3 p, Vector3 radii)
	{
		var k0 = (p / radii).Length();
		var k1 = (p / (radii * radii)).Length();
		return k1 > 0 ? k0 * (k0 - 1) / k1 : -MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z));
	}

	static float BentCapsule(Vector3 p, Vector3 start, Vector3 middle, Vector3 end, float radius, bool bent)
		=> bent ? MathF.Min(Capsule(p, start, middle, radius), Capsule(p, middle, end, radius)) : Capsule(p, start, end, radius);

	static float Capsule(Vector3 p, Vector3 a, Vector3 b, float radius)
	{
		var pa = p - a;
		var ba = b - a;
		var h = Math.Clamp(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba), 0, 1);
		return (pa - ba * h).Length() - radius;
	}

	static float Cylinder(Vector3 p, Vector3 a, Vector3 b, float radius)
	{
		var ba = b - a;
		var pa = p - a;
		var baba = Vector3.Dot(ba, ba);
		var paba = Vector3.Dot(pa, ba);
		var x = (pa * baba - ba * paba).Length() - radius * baba;
		var y = MathF.Abs(paba - baba * 0.5f) - baba * 0.5f;
		var d = MathF.Max(x, y) < 0 ? -MathF.Min(x * x, y * y * baba) : (x > 0 ? x * x : 0) + (y > 0 ? y * y * baba : 0);
		return MathF.Sign(d) * MathF.Sqrt(MathF.Abs(d)) / baba;
	}

	static float SmoothUnion(float a, float b, float k)
	{
		var h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
		return float.Lerp(b, a, h) - k * h * (1 - h);
	}

	static float SmoothSubtract(float a, float b, float k)
	{
		var h = Math.Clamp(0.5f - 0.5f * (a + b) / k, 0, 1);
		return float.Lerp(a, -b, h) + k * h * (1 - h);
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
