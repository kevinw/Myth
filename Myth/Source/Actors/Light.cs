using Foster.Framework;

namespace Myth;

public enum LightCookie
{
	None,
	Mushroom,
	Tendrils,
	Flame,
	Cone,
	Broken
}

[GameActor(Sprite = "fire", OffsetX = 4, OffsetY = 4, Tags = ActorTag.Light, LivePositionEditable = true)]
public class Light : Actor, IHitByHitbox
{
	internal static float DefaultShadowSourceRadius => 2.3f;

	[I(8, 256)] public float Radius = 48;
	public string SpriteName = "fire";
	public Color LightColor = 0xffd37a;
	[I(0, 3)] public float Intensity = 1.0f;
	[I(0, 64)] public float Height = 16;
	[I(0, 20)] public float FlickerFrequency = 4;
	[I(0, 0.5f)] public float FlickerIntensity = 0.08f;
	public LightCookie Cookie;
	[I(-180, 180)] public float CookieAngleDegrees;
	[I(-360, 360)] public float CookieSpeedDegrees;
	[I(0.1f, 4)] public float CookieScale = 1.0f;
	[I(0, 2)] public float CookieStrength = 1.0f;
	[I(0, 1)] public float CookieFeather = 0.12f;
	[I(0, 16)] public float ShadowSourceRadius = DefaultShadowSourceRadius;
	[I(0, 3)] public float ShadowSoftness = 1.0f;
	[I(0, 1)] public float ShadowNoise;
	[I(0, 40)] public float ShadowNoiseSpeed = 8.0f;
	readonly float FlickerRandomOffset = 0;
	public bool DrawSprite = true;
	internal Actor? SpriteShadowOwner;

	public float FlickerScale => 1 + CurrentFlicker * FlickerIntensity;
	public float FlickerScaleMax => 1 + FlickerIntensity;
	float CurrentFlicker => MathF.Sin(Timer * FlickerFrequency * MathF.Tau + FlickerRandomOffset);

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

	// What the renderer scales radius and intensity by: the authored flicker headroom, times
	// the decaying wobble of a recent hit.
	public float RenderScale => FlickerScaleMax * HitFlickerScale;

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

	public void HitByHitbox(RectInt hitbox)
	{
		if (!hitbox.Overlaps(Hitbox.Rect + Position))
			return;



		AudioEvent("torch_hit");
		hitFlickerAge = 0;
		// Lean away from where the swing came from; a light dead-centre in the rect takes the
		// attacker's facing instead.
		hitSkewDirection = Math.Sign(Position.X - hitbox.Center.X);
		if (hitSkewDirection == 0)
			hitSkewDirection = Game.GetFirst<Player>() is { } player ? (int)player.Facing : 1;
	}

	public override void Update()
	{
		base.Update();
		if (hitFlickerAge < 0)
			return;

		hitFlickerAge += Time.Delta;
		if (hitFlickerAge >= HitFlickerTime)
			hitFlickerAge = -1;
		else
			Skew = HitWobble * HitSkew * hitSkewDirection; // waves with the flicker, then Actor eases it home
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => DrawSprite;

	static Rng rng = new();

	public bool IsStatic = true; // doesn't move

	public Light()
	{
		FlickerRandomOffset = rng.Float(0, 3.0f);
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Sprite = Assets.GetSprite(SpriteName);
		Play("idle");
		CollidesWithSolids = false;
		Depth = 1;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteName);

	}

	public override void Render(Batcher batcher)
	{
		if (DrawSprite)
			base.Render(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (DrawSprite)
			base.RenderNormals(batcher);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (DrawSprite)
			base.RenderEmissive(batcher);
	}

}
