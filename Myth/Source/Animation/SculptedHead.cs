using System.Numerics;
using Foster.Framework;

namespace Myth;

// A head as a small signed distance field -- cranium, jaw, brow, nose, carved eye sockets and mouth -- ray-cast
// orthographically into tiny textures: palette color (banded by cavity depth, so sockets and mouth read dark),
// screen-space normals for the lighting pass, and a 1px-dilated mask for outlines and silhouettes. Head-local units
// are head radii: x is the head's right, y up, z out of the face. Re-renders only when its inputs change.
sealed class SculptedHead(GraphicsDevice device) : IDisposable
{
	const float Extent = 1.45f; // the shape's reach past the radius (nose, jaw), in radii
	const int MaxSteps = 40;

	public Texture? ColorTexture { get; private set; }
	public Texture? NormalTexture { get; private set; }
	public Texture? MaskTexture { get; private set; }
	public int Size { get; private set; }

	Color[] colors = [];
	Color[] normals = [];
	Color[] mask = [];
	bool[] hit = [];
	(HeadShape? Shape, int Version, float Radius, float Yaw, float Pitch, float Roll, float Mouth, Color Near, Color Core,
		Color Far, Color Eye) last;

	public void Render(HeadShape shape, float radius, float yaw, float pitch, float roll, float mouthOpen,
		Color near, Color core, Color far, Color eye)
	{
		var key = (shape, shape.Version, MathF.Round(radius * 4) / 4, Quantize(yaw), Quantize(pitch), Quantize(roll),
			MathF.Round(mouthOpen * 32) / 32, near, core, far, eye);
		if (ColorTexture != null && key == last)
			return;
		last = key;

		var size = (int)MathF.Ceiling(radius * Extent * 2) + 3;
		if (size != Size || ColorTexture == null)
		{
			Dispose();
			Size = size;
			colors = new Color[size * size];
			normals = new Color[size * size];
			mask = new Color[size * size];
			hit = new bool[size * size];
			ColorTexture = new Texture(device, size, size, name: "SculptedHeadColor");
			NormalTexture = new Texture(device, size, size, name: "SculptedHeadNormal");
			MaskTexture = new Texture(device, size, size, name: "SculptedHeadMask");
		}

		// View space: x right, y up, z toward the viewer. Head-local -> view is roll (screen) * pitch * yaw.
		var toView = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationZ(-roll);
		Matrix4x4.Invert(toView, out var toHead);
		var rayDirection = Vector3.TransformNormal(-Vector3.UnitZ, toHead);
		var half = size * 0.5f;
		var mouth = mouthOpen;
		var s = shape;

		Array.Clear(hit);
		Parallel.For(0, size, y =>
		{
			for (var x = 0; x < size; x++)
			{
				var i = y * size + x;
				colors[i] = default;
				normals[i] = default;
				mask[i] = default;
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
					var distance = Distance(position, s, mouth);
					if (distance < 0.004f)
					{
						found = true;
						break;
					}
					position += rayDirection * distance;
					travelled += distance;
				}
				if (!found)
					continue;

				hit[i] = true;
				var normal = SurfaceNormal(position, s, mouth);
				var viewNormal = Vector3.Normalize(Vector3.TransformNormal(normal, toView));
				// Screen normals: x right, y down, z toward the viewer.
				normals[i] = new Color(viewNormal.X * 0.5f + 0.5f, -viewNormal.Y * 0.5f + 0.5f, viewNormal.Z * 0.5f + 0.5f, 1f);
				// Banded form: a fixed key light from above and in front, plus cavity depth, quantized to the palette.
				var occlusion = Math.Clamp(Distance(position + normal * 0.22f, s, mouth) / 0.22f, 0, 1);
				var light = Math.Clamp(Vector3.Dot(viewNormal, KeyLight), 0, 1) * 0.65f + occlusion * 0.35f;
				colors[i] = InEyeGlow(position, s) && eye.A > 0 ? eye
					: occlusion < 0.4f ? far : light > 0.62f && !OnSnoutFace(position, s) ? near : light > 0.4f ? core : far;
			}
		});

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

		ColorTexture!.SetData<Color>(colors);
		NormalTexture!.SetData<Color>(normals);
		MaskTexture!.SetData<Color>(mask);
	}

	static float Quantize(float angle) => MathF.Round(angle * 180 / MathF.PI * 2) / 2;

	static readonly Vector3 KeyLight = Vector3.Normalize(new Vector3(0, 0.75f, 0.65f));

	static Vector3 Socket(HeadShape s, float side) => new(s.EyeSpacing * side, s.EyeHeight, s.EyeDepth);

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

	// A flat snout's end face sits a shade down from the cheeks around it, so it reads as a disc head-on.
	static bool OnSnoutFace(Vector3 p, HeadShape s)
	{
		if (s.NoseFlat < 0.5f || s.NoseSize <= 0)
			return false;
		var root = new Vector3(0, s.NoseHeight, 0.88f);
		var axis = new Vector3(0, -s.NoseDroop, s.NoseLength);
		var along = Vector3.Dot(p - root, axis) / axis.LengthSquared();
		return along > 0.97f && (p - root - axis * along).LengthSquared() < s.NoseSize * s.NoseSize * 0.8f;
	}

	// The accent eye glints at the back of each socket.
	static bool InEyeGlow(Vector3 p, HeadShape s)
	{
		var glow = s.EyeSize * 0.45f;
		var back = new Vector3(0, -0.02f, s.EyeSize * 0.4f);
		return s.EyeSize > 0 && (Vector3.DistanceSquared(p, Socket(s, -1) - back) < glow * glow ||
			Vector3.DistanceSquared(p, Socket(s, 1) - back) < glow * glow);
	}

	static Vector3 SurfaceNormal(Vector3 p, HeadShape s, float mouthOpen)
	{
		const float e = 0.01f;
		var a = new Vector3(1, -1, -1);
		var b = new Vector3(-1, -1, 1);
		var c = new Vector3(-1, 1, -1);
		var d = new Vector3(1, 1, 1);
		return Vector3.Normalize(a * Distance(p + a * e, s, mouthOpen) + b * Distance(p + b * e, s, mouthOpen) +
			c * Distance(p + c * e, s, mouthOpen) + d * Distance(p + d * e, s, mouthOpen));
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

	public void Dispose()
	{
		ColorTexture?.Dispose();
		NormalTexture?.Dispose();
		MaskTexture?.Dispose();
		ColorTexture = NormalTexture = MaskTexture = null;
	}
}
