using System.Numerics;
using Foster.Framework;

namespace Myth;

// A head as a small signed distance field -- cranium, jaw, brow, nose, carved eye sockets and mouth -- ray-marched
// orthographically on the GPU (SculptedHead.hlsl) into tiny targets: color shaded like a SculptedBody (sockets and
// mouth read dark by cavity), screen-space normals for the lighting pass, scene-light highlights for the emissive
// pass, and a 1px-dilated mask for outlines and silhouettes (SculptedHeadMask.hlsl). Dents carve spheres out of it
// and a wobble warps it like jelly. Head-local units are head radii: x is the head's right, y up, z out of the face.
// Re-renders only when its inputs change. The CPU version below is the reference the shape is verified against
// (Verify) and where hits land (Raycast); keep the two in step.
sealed class SculptedHead(GraphicsDevice device) : IDisposable
{
	const float Extent = 1.45f; // the shape's reach past the radius (nose, jaw), in radii
	const int MaxSteps = 40;
	const int MaxLights = SculptedBody.MaxLights;
	public const int MaxDents = 4;
	public const int MaxContacts = 2;

	static readonly BlendMode Overwrite = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly TextureSampler Nearest = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);

	readonly Batcher batcher = new(device, "SculptedHead");
	readonly Material headMaterial = new(null, LoadShader(device, "SculptedHead"));
	readonly Material maskMaterial = new(null, LoadShader(device, "SculptedHeadMask"));
	Target? marched; // color, raw normal (alpha = hit), specular
	Target? dilated; // mask, normal

	public Texture? ColorTexture => marched?.Attachments[0];
	public Texture? NormalTexture => dilated?.Attachments[1];
	public Texture? MaskTexture => dilated?.Attachments[0];
	public Texture? SpecularTexture => marched?.Attachments[2];
	public int Size { get; private set; }

	(HeadShape? Shape, int Version, float Radius, float Yaw, float Pitch, float Roll, float Mouth, Color Near, Color Core,
		Color Far, Color Eye, int Shading) last;

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
	public void Render(HeadShape shape, float radius, float yaw, float pitch, float roll, float mouthOpen,
		Color near, Color core, Color far, Color eye, BodyShadeMode mode = BodyShadeMode.Banded, Vector4 surface = default,
		ReadOnlySpan<BodyLight> lights = default, Vector2 worldCenter = default, ReadOnlySpan<Vector4> dents = default,
		float wobble = 0, float wobblePhase = 0, Color bruise = default, ReadOnlySpan<Vector4> contacts = default)
	{
		dents = dents[..Math.Min(dents.Length, MaxDents)];
		contacts = contacts[..Math.Min(contacts.Length, MaxContacts)];
		var size = TextureSize(radius);
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
		var key = (shape, shape.Version, MathF.Round(radius * 4) / 4, Quantize(yaw), Quantize(pitch), Quantize(roll),
			MathF.Round(mouthOpen * 32) / 32, near, core, far, eye, shading.ToHashCode());
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
		lastInputs = (radius, yaw, pitch, roll, mouthOpen, dents.ToArray(), wobble, wobblePhase, headContacts.ToArray());
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
			Frame = new(size * 0.5f, radius, mouthOpen, 0),
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
		};
		for (var i = 0; i < dents.Length; i++)
			uniforms.Dents[i] = dents[i];
		for (var i = 0; i < headContacts.Length; i++)
		{
			uniforms.Contacts[i * 2] = new(headContacts[i].Normal, headContacts[i].Wall);
			uniforms.Contacts[i * 2 + 1] = new(headContacts[i].Squash, 0, 0, 0);
		}
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

		maskMaterial.Fragment.SetUniformBuffer(new Vector4(size, size, 0, 0));
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
			Mouth, Ears, Near, Core, Far, Eye, Shading, Surface, Deform, Bruise;
		public LightArray Lights;
		public DentArray Dents;
		public ContactArray Contacts;
	}

	[System.Runtime.CompilerServices.InlineArray(MaxLights * 2)]
	struct LightArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxDents)]
	struct DentArray { Vector4 first; }

	[System.Runtime.CompilerServices.InlineArray(MaxContacts * 2)]
	struct ContactArray { Vector4 first; }

	// A wall the head's pressed against, head-local: the normal toward it, its distance (radii), the squash along it.
	readonly record struct Contact(Vector3 Normal, float Wall, float Squash);

	static float Quantize(float angle) => MathF.Round(angle * 180 / MathF.PI * 2) / 2;

	static int TextureSize(float radius) => (int)MathF.Ceiling(radius * Extent * 2) + 3;

	// View space: x right, y up, z toward the viewer. Head-local -> view is roll (screen) * pitch * yaw.
	internal static (Matrix4x4 ToView, Matrix4x4 ToHead) Rotations(float yaw, float pitch, float roll)
	{
		var toView = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationZ(-roll);
		Matrix4x4.Invert(toView, out var toHead);
		return (toView, toHead);
	}

#if DEBUG
	(float Radius, float Yaw, float Pitch, float Roll, float Mouth, Vector4[] Dents, float Wobble, float Phase,
		Contact[] Contacts) lastInputs;

	// Compares the last GPU render against the CPU reference: per texture, how many pixels differ by more than a
	// rounding step, and the worst channel difference. With a directory, writes both versions as PNGs side by side.
	public string Verify(string? directory = null)
	{
		if (marched == null || last.Shape is not { } shape)
			return "nothing rendered yet";
		var (radius, yaw, pitch, roll, mouth, dents, wobble, phase, contacts) = lastInputs;
		var (normals, mask) = RenderReference(new(shape, mouth, dents, wobble, phase, contacts), radius, yaw, pitch, roll);
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
	static (Color[] Normals, Color[] Mask) RenderReference(Field s, float radius, float yaw, float pitch, float roll)
	{
		var size = TextureSize(radius);
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
				var across = new Vector2((x + 0.5f - half) / radius, -(y + 0.5f - half) / radius);
				var depth = Extent * Extent - across.LengthSquared();
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
				var viewNormal = Vector3.Normalize(Vector3.TransformNormal(normal, toView));
				// Screen normals: x right, y down, z toward the viewer.
				normals[i] = new Color(viewNormal.X * 0.5f + 0.5f, -viewNormal.Y * 0.5f + 0.5f, viewNormal.Z * 0.5f + 0.5f, 1f);
			}

		// The mask: the shape dilated one pixel, so outlines and silhouettes hug it.
		for (var y = 0; y < size; y++)
			for (var x = 0; x < size; x++)
			{
				var covered = false;
				for (var dy = -1; dy <= 1 && !covered; dy++)
					for (var dx = -1; dx <= 1 && !covered; dx++)
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

	readonly record struct Field(HeadShape Shape, float Mouth, Vector4[] Dents, float Wobble, float Phase, Contact[] Contacts);

	// Where a view ray through `across` (radii from the center, y up) first meets the head, in head-local units, and the
	// surface normal there. Falls back toward the center when it misses the silhouette.
	public static bool Raycast(HeadShape shape, Vector2 across, float yaw, float pitch, float roll, out Vector3 point,
		out Vector3 normal)
	{
		var (_, toHead) = Rotations(yaw, pitch, roll);
		var rayDirection = Vector3.TransformNormal(-Vector3.UnitZ, toHead);
		for (var attempt = 0; attempt <= 8; attempt++, across *= 0.8f)
		{
			var depth = Extent * Extent - across.LengthSquared();
			if (depth <= 0)
				continue;
			depth = MathF.Sqrt(depth);
			point = Vector3.Transform(new Vector3(across, depth), toHead);
			for (var (step, travelled) = (0, 0f); step < MaxSteps && travelled < depth * 2; step++)
			{
				var distance = Distance(point, shape, 0);
				if (distance < 0.004f)
				{
					normal = SurfaceNormal(point, new(shape, 0, [], 0, 0, []));
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
		var w = Wobble(q, f);
		var d = Distance(w, f.Shape, f.Mouth);
		foreach (var dent in f.Dents)
			d = SmoothSubtract(d, Sphere(w - new Vector3(dent.X, dent.Y, dent.Z), dent.W), 0.06f);
		d *= scale;
		foreach (var contact in f.Contacts)
			d = SmoothSubtract(d, contact.Wall - Vector3.Dot(p, contact.Normal), 0.08f);
		return d;
	}

	static float Distance(Vector3 p, HeadShape s, float mouthOpen)
	{
		var cranium = Ellipsoid(p - new Vector3(0, s.CraniumLift, -0.08f), new Vector3(s.CraniumWidth, s.CraniumHeight, s.CraniumDepth));
		var jaw = Ellipsoid(p - new Vector3(0, -s.JawDrop, s.JawForward), new Vector3(s.JawWidth, s.JawHeight, s.JawDepth));
		var d = SmoothUnion(cranium, jaw, MathF.Max(0.01f, s.JawBlend));
		if (s.BrowSize > 0)
			d = SmoothUnion(d, Capsule(p, new Vector3(-s.BrowWidth, s.BrowHeight, s.BrowForward),
				new Vector3(s.BrowWidth, s.BrowHeight, s.BrowForward), s.BrowSize), 0.1f);
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
				var face = tip + (tip - root).Normalized(Vector3.UnitZ) * (s.NoseFlat > 0 ? 0 : s.NoseSize);
				var apart = new Vector3(s.NoseSize * 0.45f, 0, 0);
				d = SmoothSubtract(d, MathF.Min(Sphere(p - face - apart, s.NostrilSize), Sphere(p - face + apart, s.NostrilSize)), 0.03f);
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
			d = SmoothSubtract(d, MathF.Min(Sphere(p - Socket(s, -1), s.EyeSize), Sphere(p - Socket(s, 1), s.EyeSize)), 0.05f);
		if (s.MouthWidth > 0)
			d = SmoothSubtract(d, Capsule(p, new Vector3(-s.MouthWidth, s.MouthHeight, 0.86f),
				new Vector3(s.MouthWidth, s.MouthHeight, 0.86f), s.MouthSize + mouthOpen * 0.14f), 0.04f);
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

	static float Ellipsoid(Vector3 p, Vector3 radii)
	{
		var k0 = (p / radii).Length();
		var k1 = (p / (radii * radii)).Length();
		return k1 > 0 ? k0 * (k0 - 1) / k1 : -MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z));
	}

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
