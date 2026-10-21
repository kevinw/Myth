using System.Numerics;
using Foster.Framework;

namespace Myth;

// A glass cylinder standing floor to ceiling, holding something in. Each strike shudders it and cracks it from where it
// landed, further every time, and sloshes the gas inside away from the blow; once it's one strike from breaking, gas
// hisses out of every crack and drains around them. The last strike shatters it for good, so what waits on it carries
// on: a NeuralTextureActor whose ShutdownSourceActorId names it releases its gas, a HeadBoss whose ContainerId names it
// wakes.
[GameActor(Sprite = "Editor/particles", Icon = "smog", Anchor = SpriteAnchor.TopLeft, BoundsW = Game.TileSize * 12,
	BoundsH = Game.TileSize * 20, LivePositionEditable = true, LiveResizable = true, ShowFacing = false)]
public sealed class GlassTube : Actor, IResizable, INeuralTextureAgent
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 12, Game.TileSize * 20);

	public Point2 Size { get; set; } = DefaultSize;
	[I(1, 8)] public int Health = 3;
	public Color GlassColor = new(0x9fd8e8);
	public Color CapColor = new(0x4a4e5a);

	static int CapHeight => 6;
	static float ShudderKick => 90f; // px/sec
	static float ShudderStiffness => 700f;
	static float ShudderDamping => 12f;
	static float CrackReach => 16f; // px each strike's cracks run, times the strike count
	static float SloshRadius => 28f;
	static float SloshPush => 12f;
	static float LeakInterval => 0.1f;
	static float LeakRadius => 7f;
	static float LeakErase => 0.04f;
	static Color CrackColor => new(0xf0fbff);

	public int Strikes { get; private set; }
	readonly List<(Vector2 From, Vector2 To)> cracks = [];
	readonly List<Vector2> struck = []; // local, where each strike landed
	float shudder, shudderVelocity;
	Vector2? slosh; // world point the next gas step is shoved away from
	float sloshSide;
	float leakTimer;
	int shardEffect = -1;
	int steamEffect = -1;

	public GlassTube()
	{
		Sprite = null;
		Mask = Masks.Solid | Masks.Destructible;
		CollidesWithSolids = false;
		Depth = -6;
	}

	public override bool CastsSpriteShadow => false;
	public override string HitSoundType => "metal";
	Rect Glass => new(0, CapHeight, Size.X, Size.Y - CapHeight * 2);
	bool Leaking => Strikes > 0 && Strikes >= Health - 1;

	public override void Added()
	{
		base.Added();
		Hitbox = new(new RectInt(Point2.Zero, Size));
		shardEffect = AcquireParticleEffect("glass_shards");
		steamEffect = AcquireParticleEffect("steam");
	}

	public override void Destroyed()
	{
		ReleaseParticleEffect(ref shardEffect);
		ReleaseParticleEffect(ref steamEffect);
	}

	public override void Update()
	{
		base.Update();
		Spring.Step(ref shudder, ref shudderVelocity, 0, ShudderStiffness, ShudderDamping, Time.Delta);
		if (!Leaking)
			return;
		for (leakTimer -= Time.Delta; leakTimer <= 0; leakTimer += LeakInterval)
			foreach (var at in struck)
				Game.Particles?.Emit(steamEffect, Position + at, 1, -MathF.PI / 2, 0.6f, 0.8f);
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		var at = Vector2.Clamp(hitInfo.AttackBox.Rect.Center - Position, Glass.TopLeft + Vector2.One, Glass.BottomRight - Vector2.One);
		var side = MathF.Sign(Position.X + Size.X / 2f - hitInfo.By.Position.X);
		Strikes++;
		struck.Add(at);
		for (var i = 0; i < 2 + Strikes; i++)
			Crack(at, Game.rng.Float(MathF.Tau), CrackReach * Strikes * Game.rng.Float(0.6f, 1.2f), 2);
		shudderVelocity += side * ShudderKick;
		(slosh, sloshSide) = (Position + at, side);
		Game.Particles?.Emit(shardEffect, Position + at, 2 + Strikes * 2, side > 0 ? 0 : MathF.PI, 0.6f, 1.4f);
		AudioEvent("glass");
		if (Strikes >= Health)
		{
			Shatter();
			return;
		}
		Game.HitStun(0.08f);
		Game.Shake(0.15f, 1 + Strikes * 0.5f);
	}

	// A crack running from a point, wandering and now and then forking, until it runs out or off the glass.
	void Crack(Vector2 at, float angle, float reach, int forks)
	{
		var glass = Glass;
		while (reach > 0)
		{
			angle += Game.rng.Float(-0.5f, 0.5f);
			var step = MathF.Min(reach, Game.rng.Float(3, 7));
			var next = at + Calc.AngleToVector(angle, step);
			if (!glass.Contains(next))
				return;
			cracks.Add((at, next));
			(at, reach) = (next, reach - step);
			if (forks > 0 && Game.rng.Chance(0.25f))
				Crack(at, angle + Game.rng.Choose(-0.8f, 0.8f), reach * 0.5f, forks - 1);
		}
	}

	void Shatter()
	{
		var glass = Glass;
		for (var y = glass.Top; y < glass.Bottom; y += 10)
			Game.Particles?.Emit(shardEffect, Position + new Vector2(Game.rng.Float(glass.Left, glass.Right), y), 5, -MathF.PI / 2, 1, MathF.PI);
		Game.Particles?.Emit(steamEffect, Position + glass.Center, 40, -MathF.PI / 2, 2.5f, MathF.Tau);
		Game.Shake(0.4f, 3);
		Game.HitStun(0.25f);
		Game.DestroyPersistently(this);
	}

	// Only the gas it holds: the texture that releases when it breaks.
	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (!string.Equals(context.Texture.ShutdownSourceActorId, ActorId, StringComparison.OrdinalIgnoreCase))
			return;
		if (slosh is { } at)
			context.AddStamp(at, at, SloshRadius, 0, new(sloshSide * SloshPush, 0));
		slosh = null;
		if (Leaking)
			foreach (var hit in struck)
				context.AddStamp(Position + hit, Position + hit, LeakRadius, LeakErase);
	}

	public override void Render(Batcher batcher)
	{
		var glass = Glass;
		var size = (Vector2)Size;
		batcher.PushMatrix(Matrix3x2.CreateTranslation(MathF.Round(shudder), 0));
		batcher.Rect(glass, GlassColor * 0.12f);
		// The cylinder's sides catch the light edge-on; highlights run down its near face.
		batcher.Rect(new Rect(glass.Left, glass.Top, 2, glass.Height), GlassColor * 0.6f);
		batcher.Rect(new Rect(glass.Right - 2, glass.Top, 2, glass.Height), GlassColor * 0.4f);
		batcher.Rect(new Rect(MathF.Round(size.X * 0.2f), glass.Top, 3, glass.Height), Color.White * 0.3f);
		batcher.Rect(new Rect(MathF.Round(size.X * 0.2f) + 5, glass.Top, 1, glass.Height), Color.White * 0.18f);
		batcher.Rect(new Rect(MathF.Round(size.X * 0.82f), glass.Top, 1, glass.Height), Color.White * 0.15f);
		foreach (var (from, to) in cracks)
			batcher.Line(from, to, 1, CrackColor);
		batcher.PopMatrix();
		// Iron caps hold it to the floor and ceiling and don't shudder.
		var shine = Color.Lerp(CapColor, Color.White, 0.3f);
		batcher.Rect(new Rect(-2, 0, size.X + 4, CapHeight), CapColor);
		batcher.Rect(new Rect(-2, CapHeight - 2, size.X + 4, 1), shine);
		batcher.Rect(new Rect(-2, size.Y - CapHeight, size.X + 4, CapHeight), CapColor);
		batcher.Rect(new Rect(-2, size.Y - CapHeight + 1, size.X + 4, 1), shine);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		batcher.PushMatrix(Matrix3x2.CreateTranslation(MathF.Round(shudder), 0));
		foreach (var (from, to) in cracks)
			batcher.Line(from, to, 1, CrackColor * 0.35f);
		batcher.PopMatrix();
	}
}
