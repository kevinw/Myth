using System.Numerics;
using Foster.Framework;

namespace Myth;

enum PenPass { Color, Normals, Silhouette, Emissive }

// An actor that draws itself once, through a Pen, instead of once per render pass (see Actor.DrawWithPen).
interface IPenDrawn
{
	void Draw(Pen pen);
	ContentRef<VisualTextureContent> PenTexture => PenMaterial.DefaultTexture;
}

// A tiling height texture every pen shape is printed with, one texel per pixel so its grit stays crisp at any
// size: its darks shade the fill (Shade is premultiplied black, alpha = how dark) and its normals roughen the light.
sealed class PenMaterial(Texture shade, Texture normals) : IDisposable
{
	public readonly Texture Shade = shade, Normals = normals;
	public Vector2 Size => new(Shade.Width, Shade.Height);

	public static ContentRef<VisualTextureContent> DefaultTexture => new("patterns/noise/Noise_014");
	public static ContentRef<VisualTextureContent> MachinedTexture => new("patterns/noise/Noise_053"); // a brushed sweep

	public static PenMaterial? Get(ContentRef<VisualTextureContent> content)
	{
		if (Assets.Textures2D?.Get(content) is not { } source)
			return null;
		if (source.PenMaterial is { } cached)
			return cached;
		using var image = new Image(source.SourceImage);
		using var normal = Assets.GenerateNormalImage(image, wrap: true);
		using var shade = new Image(image.Width, image.Height);
		for (var i = 0; i < shade.Data.Length; i++)
			shade.Data[i] = new Color(0, 0, 0, 255 - image.Data[i].R);
		var device = source.Texture.GraphicsDevice;
		return source.PenMaterial = new(new Texture(device, shade, name: $"PenShade:{source.Key}"),
			new Texture(device, normal, name: $"PenNormals:{source.Key}"));
	}

	public void Dispose()
	{
		Shade.Dispose();
		Normals.Dispose();
	}
}

// Draws each shape once for whichever pass is running: its colors (tinted, for reflections), its 3D profile into
// the normal buffer, its silhouette (shadows), or its glows. The emissive pass runs under the occluder blend, so
// each solid shape erases the glows drawn before it -- a seam behind the body stays dark -- and glows draw over
// what came before them.
//
// Shapes are chamfered: a flat face inside a rim RimWidth pixels wide whose normals turn hard outward, so light
// breaks across edges in crisp bands. The rim sits inside the shape and only darkens it, so the material and any
// skin print straight across it. Widths here are pixels, not scale: a bigger actor gets more detail, not fatter.
//
// Shapes given material positions (where that point sits on the actor's rest pose, in pixels) are printed with
// the material's grit and, when set, the skin -- so the texture rides each part instead of swimming across it.
readonly struct Pen(Batcher batcher, PenPass pass, Color tint)
{
	public Batcher Batcher => batcher;
	public PenPass Pass => pass;
	public RigSkin Skin { get; init; }
	public PenMaterial? Material { get; init; }
	// 0 = front, 1 = deepest: deep shapes fade toward DepthColor (the room's backdrop, darkened), flatten in the
	// normal buffer and glow dimmer, so one number pushes a part back in color, light and emission at once.
	public float Depth { get; init; }
	public Color DepthColor { get; init; }

	static float DepthFade => 0.8f;
	static float DepthFlatten => 0.5f;
	static float DepthDim => 0.7f;
	public static Color DepthColorFor(Color backdrop) => Color.Lerp(backdrop, Color.Black, 0.5f);

	public static float RimWidth => 1.5f;
	static float RimShade => 0.55f;          // how much a rim darkens toward the outline color
	static float RimTilt => 0.75f;
	static float RimNormalAmount => 0.85f;   // rims over the material's normals, so they keep a little grit
	static float FacetTilt => 0.4f;          // a ball's inner facets
	static float Roundness => 0.9f;          // how far a round ball's or a tube's edges lean
	static float GrooveTilt => 0.6f;
	static float Grit => 0.45f;              // how dark the material's darks print
	static float GritNormals => 0.35f;
	static readonly Color FlatNormal = new(0x8080ff);

	// Solid shapes draw their silhouette in both: a shadow, or (under the occluder blend) a hole in the glow.
	bool Silhouetting => pass is PenPass.Silhouette or PenPass.Emissive;
	bool Skinned => Skin.Surface != null || Skin.Texture != null;
	Color Tinted(Color color)
	{
		if (Depth > 0)
			color = Color.Lerp(color, DepthColor * (color.A / 255f), Depth * DepthFade);
		return tint == Color.White ? color : RigDraw.Multiply(color, tint);
	}
	float Flatten => 1 - Depth * DepthFlatten;
	Color Normal(Vector2 lateral) => RigDraw.NormalColor(lateral * Flatten);
	Color Glowing(Color color) => color * (1 - Depth * DepthDim);

	// A star-shaped plate (convex, or chipped a little) fanned from its center.
	public void Plate(ReadOnlySpan<Vector2> vertices, ReadOnlySpan<Vector2> material, Color fill, Color outline)
	{
		var count = vertices.Length;
		var center = Center(vertices);
		if (Silhouetting)
		{
			Fan(vertices, center, tint);
			return;
		}

		var materialCenter = Center(material);
		Span<Vector2> inner = stackalloc Vector2[count];
		Span<Vector2> innerMaterial = stackalloc Vector2[count];
		for (var i = 0; i < count; i++)
		{
			var inset = MathF.Min(0.45f, RimWidth / MathF.Max(Vector2.Distance(center, vertices[i]), 0.01f));
			inner[i] = Vector2.Lerp(vertices[i], center, inset);
			innerMaterial[i] = Vector2.Lerp(material[i], materialCenter, inset);
		}

		if (pass == PenPass.Color)
		{
			Fan(vertices, center, Tinted(fill));
			Print(vertices, material, center, materialCenter, fill);
			var rim = Tinted(outline) * RimShade;
			for (var i = 0; i < count; i++)
			{
				var next = (i + 1) % count;
				batcher.Quad(vertices[i], vertices[next], inner[next], inner[i], rim);
			}
			return;
		}

		Fan(inner, Center(inner), FlatNormal);
		for (var i = 0; i < count; i++)
		{
			var next = (i + 1) % count;
			batcher.Quad(vertices[i], vertices[next], inner[next], inner[i], Normal(Outward(vertices[i], vertices[next], center) * RimTilt));
		}
		Print(vertices, material, center, materialCenter, fill);
		for (var i = 0; i < count; i++)
		{
			var next = (i + 1) % count;
			batcher.Quad(vertices[i], vertices[next], inner[next], inner[i],
				Normal(Outward(vertices[i], vertices[next], center) * RimTilt) * RimNormalAmount);
		}
	}

	// A faceted dome -- a body, a knuckle -- printed at `material` (its center on the rest pose).
	// `round` swaps the facets for a smooth sphere -- a head, an eyeball -- keeping the grit and the rim's shading.
	public void Ball(Vector2 center, float radius, Vector2 material, Color fill, Color outline, bool round = false)
	{
		var steps = round ? Math.Clamp((int)(radius * 3), 12, 32) : Math.Clamp((int)(radius * 1.2f), 6, 18);
		Span<Vector2> ring = stackalloc Vector2[steps];
		Span<Vector2> ringMaterial = stackalloc Vector2[steps];
		for (var i = 0; i < steps; i++)
		{
			var direction = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			ring[i] = center + direction * radius;
			ringMaterial[i] = material + direction * radius;
		}
		if (pass != PenPass.Normals)
		{
			Plate(ring, ringMaterial, fill, outline);
			return;
		}
		if (round)
		{
			RigDraw.DrawSphereNormals(batcher, center, radius, steps, Roundness * Flatten, 0, 0);
			Print(ring, ringMaterial, center, material, fill);
			return;
		}

		Span<Vector2> inner = stackalloc Vector2[steps];
		var inset = MathF.Max(radius - RimWidth, radius * 0.5f);
		for (var i = 0; i < steps; i++)
			inner[i] = center + (ring[i] - center) / radius * inset;
		for (var i = 0; i < steps; i++)
		{
			var next = (i + 1) % steps;
			var facing = Outward(ring[i], ring[next], center);
			batcher.Triangle(center, inner[i], inner[next], Normal(facing * FacetTilt));
			batcher.Quad(ring[i], ring[next], inner[next], inner[i], Normal(facing * RimTilt));
		}
		Print(ring, ringMaterial, center, material, fill);
		for (var i = 0; i < steps; i++)
		{
			var next = (i + 1) % steps;
			batcher.Quad(ring[i], ring[next], inner[next], inner[i], Normal(Outward(ring[i], ring[next], center) * RimTilt) * RimNormalAmount);
		}
	}

	// A limb or rod: a cylinder tapering from `fromWidth` to `toWidth`, its outline shading the sides. Its material
	// runs along it from `material`, so a leg's grit stays put on the leg. `segment` > 0 bands it every that many
	// pixels.
	public void Tube(Vector2 from, Vector2 to, float fromWidth, float toWidth, Vector2 material, Color fill, Color outline, float segment = 0)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft() * 0.5f;
		var length = Vector2.Distance(from, to);
		ReadOnlySpan<Vector2> quad = [from + across * fromWidth, to + across * toWidth, to - across * toWidth, from - across * fromWidth];
		ReadOnlySpan<Vector2> quadMaterial = [material, material + new Vector2(length, 0), material + new Vector2(length, toWidth), material + new Vector2(0, fromWidth)];
		if (pass == PenPass.Normals)
		{
			RigDraw.DrawCylinderNormals(batcher, from, to, fromWidth, toWidth, Roundness * Flatten, 0, 0);
			Print(quad, quadMaterial, Center(quad), Center(quadMaterial), fill);
		}
		else
			Plate(quad, quadMaterial, fill, outline);
		if (segment > 0)
			for (var at = segment; at < length - 1; at += segment)
			{
				var half = across * float.Lerp(fromWidth, toWidth, at / length);
				Groove(from + along * at + half, from + along * at - half, 1, outline);
			}
	}

	// A sagging line -- a cable, a tendon -- hung from `from` to `to`, bowing `sag` at its middle.
	public void Cable(Vector2 from, Vector2 to, Vector2 sag, float width, Color color)
	{
		const int Segments = 6;
		var previous = from;
		for (var i = 1; i <= Segments; i++)
		{
			var t = i / (float)Segments;
			var next = Vector2.Lerp(from, to, t) + sag * 4 * t * (1 - t);
			if (pass == PenPass.Color)
				batcher.Line(previous, next, width, Tinted(color));
			else if (Silhouetting)
				batcher.Line(previous, next, width, tint);
			else if (pass == PenPass.Normals)
				RigDraw.DrawCylinderNormals(batcher, previous, next, width, 0.8f * Flatten, 0, 0);
			previous = next;
		}
	}

	// A flat triangular facet -- a tooth, an iris blade -- whose normal leans `lean`.
	public void Facet(Vector2 a, Vector2 b, Vector2 c, Color color, Vector2 lean)
	{
		if (pass == PenPass.Color)
			batcher.Triangle(a, b, c, Tinted(color));
		else if (Silhouetting)
			batcher.Triangle(a, b, c, tint);
		else if (pass == PenPass.Normals)
			batcher.Triangle(a, b, c, Normal(lean));
	}

	// Teeth along the edge a->b, pointing `outward`: two facets each, so every tooth catches light on one side.
	public void Teeth(Vector2 a, Vector2 b, Vector2 outward, float size, float spacing, Color color)
	{
		var length = Vector2.Distance(a, b);
		var along = (b - a) / MathF.Max(length, 0.01f);
		for (var at = spacing * 0.5f; at + spacing * 0.5f <= length + 0.01f; at += spacing)
		{
			var root = a + along * at;
			var tip = root + outward * size;
			Facet(root - along * spacing * 0.45f, tip, root, color, (outward - along) * 0.45f);
			Facet(root, tip, root + along * spacing * 0.45f, color, (outward + along) * 0.45f);
		}
	}

	// A V-shaped seam cut into whatever is beneath it.
	public void Groove(Vector2 from, Vector2 to, float width, Color color)
	{
		if (pass == PenPass.Color)
			batcher.Line(from, to, width, Tinted(color));
		else if (pass == PenPass.Normals)
		{
			var across = (to - from).TurnLeft().Normalized();
			var half = across * width * 0.5f;
			var (left, right) = (Normal(-across * GrooveTilt), Normal(across * GrooveTilt));
			batcher.Quad(from + half, to + half, to, from, left);
			batcher.Quad(from, to, to - half, from - half, right);
		}
	}

	// A bolt head: a pixel-sized dome.
	public void Rivet(Vector2 center, Color color)
	{
		if (pass == PenPass.Color)
			batcher.Circle(center, 0.8f, 6, Tinted(color));
		else if (pass == PenPass.Normals)
			RigDraw.DrawSphereNormals(batcher, center, 1.2f, 6, 0.8f * Flatten, 0, 0);
	}

	// Color only: a detail painted onto whatever shape is beneath it (a pupil, a glint), keeping that shape's normals.
	public void Dot(Vector2 center, float radius, Color color)
	{
		if (pass == PenPass.Color)
			batcher.Circle(center, radius, Math.Clamp((int)(radius * 3), 6, 24), Tinted(color));
	}

	// Color only, like Dot: energy rather than a surface, so it has no normals and casts no shadow.
	public void Stroke(Vector2 from, Vector2 to, float width, Color color)
	{
		if (pass == PenPass.Color)
			batcher.Line(from, to, width, Tinted(color));
	}

	public void Ring(Vector2 center, float radius, float width, Color color)
	{
		if (pass == PenPass.Color)
			batcher.CircleLine(center, radius, width, Math.Clamp((int)(radius * 1.5f), 12, 32), Tinted(color));
	}

	// Emissive only, added over whatever has been drawn so far.
	public void Glow(Vector2 center, float radius, Color color)
	{
		if (pass != PenPass.Emissive)
			return;
		batcher.PushBlend(GameView.EmissiveBlend);
		batcher.Circle(center, radius, Math.Clamp((int)(radius * 3), 6, 24), Glowing(color));
		batcher.PopBlend();
	}

	public void GlowLine(Vector2 from, Vector2 to, float width, Color color)
	{
		if (pass != PenPass.Emissive)
			return;
		batcher.PushBlend(GameView.EmissiveBlend);
		batcher.Line(from, to, width, Glowing(color));
		batcher.PopBlend();
	}

	public void GlowRing(Vector2 center, float radius, float width, Color color)
	{
		if (pass != PenPass.Emissive)
			return;
		batcher.PushBlend(GameView.EmissiveBlend);
		batcher.CircleLine(center, radius, width, Math.Clamp((int)(radius * 1.5f), 12, 32), Glowing(color));
		batcher.PopBlend();
	}

	// The material's grit, then the skin, over a polygon whose rest-pose positions are `material`.
	void Print(ReadOnlySpan<Vector2> vertices, ReadOnlySpan<Vector2> material, Vector2 center, Vector2 materialCenter, Color fill)
	{
		if (Material is { } grit)
		{
			var texture = pass == PenPass.Normals ? grit.Normals : grit.Shade;
			var color = Color.White * (pass == PenPass.Normals ? GritNormals : Grit);
			var size = grit.Size;
			for (var i = 0; i < vertices.Length; i++)
			{
				var next = (i + 1) % vertices.Length;
				batcher.Triangle(texture, center, vertices[i], vertices[next], materialCenter / size, material[i] / size, material[next] / size, color);
			}
		}
		if (!Skinned)
			return;
		Span<Vector2> uvs = stackalloc Vector2[vertices.Length];
		for (var i = 0; i < uvs.Length; i++)
			uvs[i] = (material[i] - (Vector2)Skin.Origin) / (Vector2)Skin.Size;
		Skin.Push(batcher);
		Skin.Polygon(batcher, vertices, uvs, pass == PenPass.Normals ? Color.White * Flatten : Tinted(fill));
		Skin.Pop(batcher);
	}

	void Fan(ReadOnlySpan<Vector2> vertices, Vector2 center, Color color)
	{
		for (var i = 0; i < vertices.Length; i++)
			batcher.Triangle(center, vertices[i], vertices[(i + 1) % vertices.Length], color);
	}

	// The unit normal of edge a->b pointing away from `center`.
	public static Vector2 Outward(Vector2 a, Vector2 b, Vector2 center)
	{
		var normal = (b - a).TurnLeft().Normalized();
		return Vector2.Dot(normal, (a + b) * 0.5f - center) < 0 ? -normal : normal;
	}

	public static Vector2 Center(ReadOnlySpan<Vector2> vertices)
	{
		var sum = Vector2.Zero;
		foreach (var vertex in vertices)
			sum += vertex;
		return sum / vertices.Length;
	}
}
