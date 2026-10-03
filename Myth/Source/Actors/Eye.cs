using System.Numerics;
using Foster.Framework;

namespace Myth;

// A lidded eye painted a pixel column at a time, so it sits on the pixel grid and the lids clip the pupil for
// free. Its owner steers it: LookAt (world) pulls the pupil and widens the eye as it nears, Anger squints the
// top lid down toward the inner corner and heats the pupil, Open is how far the lids may part. Attached
// (AttachedToActorId), it rides its owner, tremble included.
public sealed class Eye : Actor
{
	public Vector2? LookAt;
	public float Anger;
	public float Open = 1;
	public int Slant; // where the inner corner is, -1 left or +1 right; 0 is a lone eye, which scowls in the middle

	public const int HalfWidth = 5;
	static float HalfHeight => 3.5f;
	static float RestOpen => 0.45f;
	static float SightRange => Game.TileSize * 14;
	static float PupilRadius => 2f;
	static float SquintDepth => 0.8f;
	static float CloseRate => 14;
	static float OpenRate => 3;
	static Color Sclera => new(0xe8e0d0);
	static Color Ink => new(0x1a1418);
	static Color HeatColor => new(0xff5a30);

	float openness = 1, blink, nextBlink = 3;
	Vector2 pupil;

	public Eye()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = Depths.ForegroundProp - 1;
	}

	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		var toTarget = LookAt is { } at ? at - (Vector2)Position : Vector2.Zero;
		var near = LookAt == null ? 0 : 1 - Math.Clamp(toTarget.Length() / SightRange, 0, 1);

		var target = Open * float.Lerp(RestOpen, 1, near);
		openness = Calc.Approach(openness, target, (target < openness ? CloseRate : OpenRate) * Time.Delta);

		var reach = new Vector2(HalfWidth - PupilRadius - 1, HalfHeight - PupilRadius + 0.5f);
		var aim = toTarget.LengthSquared() > 1 ? Vector2.Normalize(toTarget) * reach : Vector2.Zero;
		pupil = Vector2.Lerp(pupil, aim, 1 - MathF.Exp(-10 * Time.Delta));

		// Idle blinks only: a glaring or shutting eye keeps its lids where its owner put them.
		blink = Calc.Approach(blink, 0, 8 * Time.Delta);
		if ((nextBlink -= Time.Delta) <= 0)
			(blink, nextBlink) = (Anger <= 0 && Open >= 1 ? 1 : 0, Game.rng.Float(2, 6));
	}

	public override void Render(Batcher batcher) => Paint(batcher, emissive: false);
	public override void RenderEmissive(Batcher batcher) => Paint(batcher, emissive: true);

	void Paint(Batcher batcher, bool emissive)
	{
		if (emissive && Anger <= 0)
			return;

		var h = HalfHeight * openness * (1 - MathF.Sin(blink * MathF.PI));
		var r = PupilRadius * (1 - 0.4f * Anger);
		var sclera = Color.Lerp(Sclera, HeatColor, Anger * 0.35f);
		for (var x = -HalfWidth; x < HalfWidth; x++)
		{
			var u = (x + 0.5f) / HalfWidth;
			var curve = 1 - u * u;
			var squint = Slant == 0 ? 1 - MathF.Abs(u) : (1 + Slant * u) / 2;
			var top = (int)MathF.Round(-h * curve + Anger * h * SquintDepth * squint);
			var bottom = (int)MathF.Round(h * 0.8f * curve);

			var dx = x + 0.5f - pupil.X;
			var half = dx * dx < r * r ? MathF.Sqrt(r * r - dx * dx) : -1;
			var pupilTop = Math.Max(top, (int)MathF.Round(pupil.Y - half));
			var pupilBottom = Math.Min(bottom, (int)MathF.Round(pupil.Y + half));

			if (emissive)
			{
				if (pupilBottom > pupilTop)
					batcher.Rect(new RectInt(x, pupilTop, 1, pupilBottom - pupilTop), HeatColor * Anger);
				continue;
			}
			if (bottom <= top)
			{
				batcher.Rect(new RectInt(x, 0, 1, 1), Ink); // shut: just the seam
				continue;
			}
			batcher.Rect(new RectInt(x, top - 1, 1, 1), Ink);
			batcher.Rect(new RectInt(x, bottom, 1, 1), Ink);
			batcher.Rect(new RectInt(x, top, 1, bottom - top), sclera);
			if (pupilBottom > pupilTop)
				batcher.Rect(new RectInt(x, pupilTop, 1, pupilBottom - pupilTop), Ink);
		}
	}
}
