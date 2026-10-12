using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum LightCookie
{
	None,
	Cone,
	/*
	Tendrils,
	Flame,
	Mushroom,
	Broken
	*/
}

// An anchor that turns in part -- a prop's head: lights attached to it swing about Pivot by Rotation (radians,
// clockwise on screen), and their cone turns with it.
public interface IAttachmentRotation
{
	Vector2 AttachmentPivot { get; }
	float AttachmentRotation { get; }
}

[GameActor(Sprite = "fire", OffsetX = 4, OffsetY = 4, Tags = ActorTag.Light, LivePositionEditable = true)]
public class Light : Actor, IHitByHitbox
{
	internal static float DefaultShadowSourceRadius => 2.3f;

	[I(8, 256)] public float Radius = 48;
	public string SpriteName = "fire";
	public string EnabledByPersistentButtonId = "";
	public Color LightColor = 0xffd37a;
	[I(0, 3)] public float Intensity = 1.0f;
	[I(0, 64)] public float Height = 16;
	[I(0, 20)] public float FlickerFrequency = 4;
	[I(0, 0.5f)] public float FlickerIntensity = 0.08f;
	[InspectorGroup("Cookie")]
	public LightCookie Cookie;
	[I(-180, 180)] public float CookieAngleDegrees;
	[I(-360, 360)] public float CookieSpeedDegrees;
	[I(0.1f, 4)] public float CookieScale = 1.0f;
	[I(0, 2)] public float CookieStrength = 1.0f;
	[I(0, 1)] public float CookieFeather = 0.12f;
	// Cone cookie only: pushes the cone's apex down its axis (fraction of the radius) so the cone
	// begins below the source -- e.g. a horizontal bulb casting a cutoff cone. 0 = apex at the source.
	[I(0, 1)] public float CookieCutoff = 0f;
	// When what the light shines into turns solid -- a lamp on a Crusher slammed flush to the floor -- it eases
	// back to a small dim circle instead of projecting its cone into the ground.
	public bool DimsWhenBuried;
	[InspectorGroup("Shadow")]
	[I(0, 16)] public float ShadowSourceRadius = DefaultShadowSourceRadius;
	[I(0, 3)] public float ShadowSoftness = 1.0f;
	[I(0, 1)] public float ShadowNoise;
	[I(0, 40)] public float ShadowNoiseSpeed = 8.0f;

	
	[InspectorGroup("Sprite")]
	public bool DrawSprite = true;
	// Multiplies the sprite by LightColor, so one neutral sprite (fire_greyscale) can stand in
	// for a flame of any colour. The normals pass stays untinted -- it carries directions, not
	// colour -- but the reflection follows the sprite so water doesn't show the untinted original.
	public bool TintSprite;
	[I(0, 2)] public float TintSaturation = 1;
	[I(0, 1)] public float TintVividness = 0;

	// Multiplying a grey sprite by a colour hands every pixel that colour's saturation exactly
	// -- the grey cancels out of 1 - min/max -- so the flame is only ever as vivid as LightColor
	// is. These two reshape the tint before the multiply; the light it casts still uses LightColor.
	Color SpriteTint => TintSprite ? Saturate(PureHue(LightColor, TintVividness), TintSaturation) : Color.White;
	
	readonly float FlickerRandomOffset = 0;
	internal Actor? SpriteShadowOwner;

	// Pulls a colour toward its own hue at full saturation and value. Pale light colours
	// (DDEBFF is 13% saturated) have almost no hue for TintSaturation to scale, so this is
	// what gives them something to work with.
	static Color PureHue(Color color, float amount)
	{
		if (amount <= 0)
			return color;

		var lo = Math.Min(color.R, Math.Min(color.G, color.B));
		var hi = Math.Max(color.R, Math.Max(color.G, color.B));
		if (hi == lo)
			return color; // no hue to pull toward

		var scale = 255f / (hi - lo);
		return new Color(
			Lerp(color.R, (color.R - lo) * scale, amount),
			Lerp(color.G, (color.G - lo) * scale, amount),
			Lerp(color.B, (color.B - lo) * scale, amount),
			color.A);
	}

	// Scales saturation about the colour's own luma, which the lerp leaves untouched: 0 is grey,
	// 1 is the colour as authored, above 1 extrapolates past it.
	static Color Saturate(Color color, float amount)
	{
		if (amount == 1)
			return color;

		var luma = 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
		return new Color(
			Lerp(luma, color.R, amount),
			Lerp(luma, color.G, amount),
			Lerp(luma, color.B, amount),
			color.A);
	}

	static byte Lerp(float from, float to, float amount)
		=> (byte)Math.Clamp(from + (to - from) * amount, 0, 255);

	static Color Multiply(Color a, Color b) => new(
		(byte)(a.R * b.R / 255),
		(byte)(a.G * b.G / 255),
		(byte)(a.B * b.B / 255),
		(byte)(a.A * b.A / 255));

	public float FlickerScale => FlickerBaseScale * (1 + CurrentFlicker * FlickerIntensity);
	public float FlickerBaseScale => 1 + FlickerIntensity;
	public bool Enabled => string.IsNullOrWhiteSpace(EnabledByPersistentButtonId) || Game.IsButtonPressed(EnabledByPersistentButtonId);
	float CurrentFlicker => FlickerFrequency <= 0 ? 0 :
		0.7f * MathF.Sin(Timer * FlickerFrequency * 0.23f * MathF.Tau + FlickerRandomOffset * 1.7f) +
		0.3f * MathF.Sin(Timer * FlickerFrequency * MathF.Tau + FlickerRandomOffset);

	// Struck by a sword swing: the light flares and shudders, settling back over HitFlickerTime.
	[I(0, 2)] public float HitFlickerStrength = 0.174f;
	// The bright half of the wobble gets this fraction of the dark half's swing. RenderScale
	// drives radius and intensity together, so an unscaled up-swing both brightens and spreads
	// the light and flares much harder than the same number dims it.
	[I(0, 1)] public float HitFlickerBrightRatio = 0.4f;
	// Sags the whole wobble, fading out with it: the hit knocks the light down a little rather
	// than only exciting it.
	[I(0, 0.5f)] public float HitFlickerDip = 0.04f;
	[I(1, 40)] public float HitFlickerRate = 5.337f;
	[I(0.05f, 2)] public float HitFlickerTime = 0.408f;
	float hitFlickerAge = -1; // negative while at rest

	// Keep the old flicker headroom as the average while the light breathes around it.
	public float RenderScale => FlickerScale * HitFlickerScale;
	public float RenderRadius => Radius * RenderScale * Calc.Lerp(1, BuriedRadiusScale, buried);
	public float RenderIntensity => Intensity * RenderScale * Calc.Lerp(1, BuriedIntensityScale, buried);
	public float RenderCookieStrength => CookieStrength * (1 - buried);
	public float RenderCookieCutoff => CookieCutoff * (1 - buried);
	public float RenderCookieAngleDegrees => CookieAngleDegrees + AttachedRotation * Calc.RadToDeg;
	float AttachedRotation => AttachmentTarget is IAttachmentRotation turning ? turning.AttachmentRotation : 0;

	internal override Vector2 LocalVisualOffset
	{
		get
		{
			if (AttachmentTarget is not IAttachmentRotation { AttachmentRotation: not 0 } turning)
				return Vector2.Zero;
			var arm = Position - turning.AttachmentPivot;
			return Vector2.Transform(arm, Matrix3x2.CreateRotation(turning.AttachmentRotation)) - arm;
		}
	}

	static float BuriedRadiusScale => 0.3f;
	static float BuriedIntensityScale => 0.35f;
	static float BuriedSpriteAlpha => 0f;
	float SpriteAlpha => Calc.Lerp(1, BuriedSpriteAlpha, buried);
	static int BuriedClearance => 32; // px of open space ahead the light needs to be fully itself
	float buried; // 0..1: how little room there is ahead of the light

	// Open space straight ahead along the cone, up to BuriedClearance, not counting whatever we're attached to:
	// the crusher carrying the lamp is always solid around it, the floor it lands on is what buries it.
	int Clearance()
	{
		var axis = Cookie == LightCookie.None ? Vector2.UnitY : Calc.AngleToVector(RenderCookieAngleDegrees * Calc.DegToRad);
		for (var distance = 0; distance < BuriedClearance; distance++)
			if (SolidAt((Position + axis * distance).RoundToPoint2()))
				return distance;
		return BuriedClearance;
	}

	bool SolidAt(Point2 point)
	{
		var probe = new RectInt(point, 1, 1);
		if (Game.OverlapsRoomCollision(this, probe))
			return true;
		foreach (var other in Game.Actors)
			if (other != AttachmentTarget && other is not Grid && other.Mask.Has(Masks.Solid) && other.Hitbox.Overlaps(probe - other.Position))
				return true;
		return false;
	}
	public float RenderShadowSourceRadius => ShadowSourceRadius * (1 + (RenderScale / FlickerBaseScale - 1) * 0.45f);

	float HitFlickerScale
	{
		get
		{
			var fade = HitFade;
			if (fade <= 0)
				return 1;

			var wobble = HitWobble;
			var strength = wobble > 0 ? HitFlickerStrength * HitFlickerBrightRatio : HitFlickerStrength;
			return 1 + wobble * strength - HitFlickerDip * fade;
		}
	}

	// 1 the moment the hit lands, easing to 0 as the light settles.
	float HitFade => hitFlickerAge < 0 || HitFlickerTime <= 0 ? 0 : 1 - hitFlickerAge / HitFlickerTime;

	// The shared decaying wave a hit leaves behind, -1..1 and settling to 0. sin starts at 0,
	// so both the brightness and the lean open from rest rather than snapping. The lean reads
	// it raw -- only brightness treats the two halves differently.
	float HitWobble
	{
		get
		{
			var fade = HitFade;
			return fade <= 0 ? 0 : fade * MathF.Sin(hitFlickerAge * HitFlickerRate * MathF.Tau);
		}
	}

	// How far the flame leans away from the swing at the peak of the wobble.
	[I(0, 1)] public float HitSkew = 0.35f;
	int hitSkewDirection;

	public bool HitByHitbox(RectInt hitbox)
	{
		if (!hitbox.Overlaps(Hitbox.Rect + Position) || !DrawSprite || !Enabled)
			return false;

		AudioEvent("torch_hit");
		hitFlickerAge = 0;
		// Lean away from where the swing came from; a light dead-centre in the rect takes the
		// attacker's facing instead.
		hitSkewDirection = Math.Sign(Position.X - hitbox.Center.X);
		if (hitSkewDirection == 0)
			hitSkewDirection = Game.GetFirst<Player>() is { } player ? (int)player.Facing : 1;
		return true;
	}

	internal void FlickerFromBurrow(RectInt path)
	{
		if (hitFlickerAge < 0)
			HitByHitbox(path);
	}

	public override void Update()
	{
		base.Update();
		if (DimsWhenBuried)
			buried = 1 - Clearance() / (float)BuriedClearance;
		if (hitFlickerAge < 0)
			return;

		hitFlickerAge += Time.Delta;
		if (hitFlickerAge >= HitFlickerTime)
			hitFlickerAge = -1;
		else
			Skew = HitWobble * HitSkew * hitSkewDirection; // waves with the flicker, then Actor eases it home
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => DrawSprite && Enabled;

	static Rng rng = new();

	public bool IsStatic = true; // doesn't move

	public Light()
	{
		FlickerRandomOffset = rng.Float(0, 3.0f);
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Sprite = Assets.GetSprite(SpriteName);
		Play("idle");
		CollidesWithSolids = false;
		Depth = Depths.Ambient;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteName);

	}

	public override void Render(Batcher batcher)
	{
		if (DrawSprite && Enabled)
			RenderSpriteSubtexture(batcher, SpriteTint * SpriteAlpha);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (DrawSprite && Enabled)
			RenderSpriteSubtexture(batcher, SubtextureType.Normals, Color.White * SpriteAlpha);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (DrawSprite && Enabled)
			RenderSpriteSubtexture(batcher, SubtextureType.Emissive,
				Color.White * Math.Clamp(1 + (RenderScale / FlickerBaseScale - 1) * 1.5f, 0, 1) * SpriteAlpha);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (DrawSprite && Enabled)
			base.RenderReflection(batcher, Multiply(color, SpriteTint) * SpriteAlpha);
	}

}
