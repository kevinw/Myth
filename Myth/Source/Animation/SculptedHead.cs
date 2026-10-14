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
	(float Radius, float Yaw, float Pitch, float Roll, float Mouth, Color Near, Color Core, Color Far, Color Eye) last;

	public void Render(float radius, float yaw, float pitch, float roll, float mouthOpen,
		Color near, Color core, Color far, Color eye)
	{
		var key = (MathF.Round(radius * 4) / 4, Quantize(yaw), Quantize(pitch), Quantize(roll), MathF.Round(mouthOpen * 32) / 32,
			near, core, far, eye);
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

		Array.Clear(hit);
		for (var y = 0; y < size; y++)
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
					var distance = Distance(position, mouth);
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
				var normal = SurfaceNormal(position, mouth);
				var viewNormal = Vector3.Normalize(Vector3.TransformNormal(normal, toView));
				// Screen normals: x right, y down, z toward the viewer.
				normals[i] = new Color(viewNormal.X * 0.5f + 0.5f, -viewNormal.Y * 0.5f + 0.5f, viewNormal.Z * 0.5f + 0.5f, 1f);
				// Banded form: a fixed key light from above and in front, plus cavity depth, quantized to the palette.
				var occlusion = Math.Clamp(Distance(position + normal * 0.22f, mouth) / 0.22f, 0, 1);
				var light = Math.Clamp(Vector3.Dot(viewNormal, KeyLight), 0, 1) * 0.65f + occlusion * 0.35f;
				colors[i] = InEyeGlow(position) && eye.A > 0 ? eye
					: occlusion < 0.4f ? far : light > 0.62f ? near : light > 0.4f ? core : far;
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

		ColorTexture!.SetData<Color>(colors);
		NormalTexture!.SetData<Color>(normals);
		MaskTexture!.SetData<Color>(mask);
	}

	static float Quantize(float angle) => MathF.Round(angle * 180 / MathF.PI * 2) / 2;

	static readonly Vector3 KeyLight = Vector3.Normalize(new Vector3(0, 0.75f, 0.65f));
	static readonly Vector3 SocketLeft = new(-0.34f, 0.06f, 0.72f);
	static readonly Vector3 SocketRight = new(0.34f, 0.06f, 0.72f);

	static float Distance(Vector3 p, float mouthOpen)
	{
		var cranium = Ellipsoid(p - new Vector3(0, 0.14f, -0.08f), new Vector3(0.84f, 0.92f, 1f));
		var jaw = Ellipsoid(p - new Vector3(0, -0.46f, 0.2f), new Vector3(0.64f, 0.48f, 0.7f));
		var brow = Capsule(p, new Vector3(-0.44f, 0.26f, 0.8f), new Vector3(0.44f, 0.26f, 0.8f), 0.15f);
		var nose = Capsule(p, new Vector3(0, 0.16f, 0.88f), new Vector3(0, -0.18f, 1.18f), 0.15f);
		var d = SmoothUnion(cranium, jaw, 0.22f);
		d = SmoothUnion(d, brow, 0.1f);
		d = SmoothUnion(d, nose, 0.07f);
		var sockets = MathF.Min(Sphere(p - SocketLeft, 0.25f), Sphere(p - SocketRight, 0.25f));
		d = SmoothSubtract(d, sockets, 0.05f);
		var mouth = Capsule(p, new Vector3(-0.3f, -0.52f, 0.86f), new Vector3(0.3f, -0.52f, 0.86f),
			0.05f + mouthOpen * 0.14f);
		return SmoothSubtract(d, mouth, 0.04f);
	}

	// The accent eye glints at the back of each socket.
	static bool InEyeGlow(Vector3 p)
		=> Vector3.DistanceSquared(p, SocketLeft - new Vector3(0, 0.02f, 0.1f)) < 0.012f ||
			Vector3.DistanceSquared(p, SocketRight - new Vector3(0, 0.02f, 0.1f)) < 0.012f;

	static Vector3 SurfaceNormal(Vector3 p, float mouthOpen)
	{
		const float e = 0.01f;
		var a = new Vector3(1, -1, -1);
		var b = new Vector3(-1, -1, 1);
		var c = new Vector3(-1, 1, -1);
		var d = new Vector3(1, 1, 1);
		return Vector3.Normalize(a * Distance(p + a * e, mouthOpen) + b * Distance(p + b * e, mouthOpen) +
			c * Distance(p + c * e, mouthOpen) + d * Distance(p + d * e, mouthOpen));
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
