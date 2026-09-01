using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "blob", Tags = ActorTag.Enemy, BoundsX = -22, BoundsY = -28, BoundsW = 44, BoundsH = 28,
	LivePositionEditable = true)]
public class AlienBug : Actor, INeuralTextureAgent
{
	enum States { Idle, Running, Attacking, Stunned }
	public string Rig = "alien-bug";
	[I(1, 60)] public float VisualFPS = 20;
	[I(0.1f, 3)] public float PlaybackSpeed = 1;
	[I(20, 100)] public float PixelsPerMeter = 58;
	[I(-90, 90)] public float ProjectionYaw = 55;
	[I(0.5f, 2)] public float Thickness = 1;
	[I(3, 16)] public float BodySize = 9;
	[I(2, 12)] public float HeadSize = 6;
	[I(1, 12)] public int Health = 4;
	[I(24, 240)] public float NoticeRange = 132;
	[I(24, 160)] public float VerticalNoticeRange = 96;
	[I(8, 64)] public float AttackRange = 24;
	[I(8, 80)] public float RunSpeed = 30;
	public RigSkinMode SkinMode = RigSkinMode.NCA;
	public ContentRef<SurfaceContent> SurfaceId = new("");
	[ActorSettings(typeof(NeuralSurfaceSettings), "Neural Surface", "Neural")]
	public readonly NeuralSurfaceSettings Neural = new()
	{
		NeuralWeightsFile = NeuralWeightsContent.AlienBugDefault,
		NeuralColorAmount = 0.8f,
		NeuralNormalAmount = 0.65f,
		NeuralFireRate = 0.6f,
		NeuralContrast = 1.2f,
		NeuralSaturation = 1.25f,
		NeuralBrightness = 0.12f,
	};
	[ActorSettings(typeof(NeuralReactionSettings), "Neural Reactions", "Neural")]
	public readonly NeuralReactionSettings NeuralReactions = new()
	{
		NeuralHitStrength = 0.7f,
		NeuralJostleInterval = 0.3f,
		NeuralJostleStrength = 0.3f,
		NeuralJostleRadius = 4,
	};
	public Color FarColor = new(0x293d30);
	public Color NearColor = new(0x9bc764);
	public Color CoreColor = new(0x526d38);
	public Color OutlineColor = new(0x111811);
	public Color EyeColor = new(0xff315a);

	static float Gravity => 360;
	static float Acceleration => 180;
	static float Friction => 300;
	static float AttackCooldown => 0.7f;
	static float StunTime => 0.28f;
	static float Knockback => 72;

	AlienBugRigVisual visual = null!;
	States state;
	float stateTime;
	float attackCooldown;
	bool alternateAttack;
	bool attackHit;

	public AlienBug()
	{
		Sprite = null;
		Hitbox = new(new RectInt(-11, -15, 22, 15));
		Mask = Masks.Enemy;
		Depth = -5;
		IFrameTime = 0.2f;
	}

	public override bool CastsSpriteShadow => true;
	public override bool CastsReflection => true;
	public override bool LightingDithered => false;

	public override void Added()
	{
		base.Added();
		visual = new(this);
		visual.Initialize();
	}

	public override void Update()
	{
		Velocity.Y += Gravity * Time.Delta;
		base.Update();
		var grounded = Grounded();
		if (grounded && Velocity.Y > 0)
			Velocity.Y = 0;
		stateTime += Time.Delta;
		attackCooldown = MathF.Max(0, attackCooldown - Time.Delta);

		var player = Game.GetFirst<Player>();
		var toPlayer = player?.Position - Position ?? Point2.Zero;
		if (state == States.Stunned)
		{
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
			if (stateTime >= StunTime && grounded)
				SetState(States.Idle);
		}
		else if (state == States.Attacking)
		{
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
			if (!attackHit && visual.Time >= visual.ActiveClip.Duration * 0.35f)
			{
				attackHit = true;
				var x = Facing == Signs.Positive ? 0 : -30;
				var attackBox = new RectInt(Position.X + x, Position.Y - 18, 30, 18);
				if (Game.OverlapsFirst(attackBox, Masks.Player) is { } hit)
					Hit(hit, new(attackBox));
			}
			if (visual.Time >= visual.ActiveClip.Duration - 0.02f)
				SetState(States.Idle);
		}
		else if (player != null && MathF.Abs(toPlayer.X) <= NoticeRange && MathF.Abs(toPlayer.Y) < VerticalNoticeRange)
		{
			if (MathF.Abs(toPlayer.X) > 1)
				Facing = MathF.Sign(toPlayer.X);
			if (MathF.Abs(toPlayer.X) <= AttackRange && attackCooldown <= 0)
			{
				alternateAttack = !alternateAttack;
				attackHit = false;
				visual.Play(alternateAttack ? "Attack.000" : "Attack.001", restart: true);
				attackCooldown = AttackCooldown;
				SetState(States.Attacking, play: false);
			}
			else
			{
				Velocity.X = Calc.Approach(Velocity.X, (int)Facing * RunSpeed, Acceleration * Time.Delta);
				SetState(States.Running);
			}
		}
		else
		{
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
			SetState(States.Idle);
		}
		visual.Update(state != States.Attacking);
	}

	void SetState(States next, bool play = true)
	{
		if (state == next)
			return;
		state = next;
		stateTime = 0;
		if (play)
			visual.Play(next == States.Running ? "Run" : "Idle");
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		if (state == States.Running)
			SetState(States.Idle);
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		Health--;
		if (Health <= 0)
		{
			for (var i = 0; i < 3; i++)
				Game.Create<Pop>(Position + new Point2((i - 1) * 7, -8 - i * 4));
			Game.DestroyForThisSession(this);
			return;
		}
		var by = hitInfo.By;
		Facing = by.Position.X < Position.X ? Signs.Negative : Signs.Positive;
		Velocity = new Vector2(MathF.Sign(Position.X - by.Position.X) * Knockback, -32);
		visual.Hit();
		SetState(States.Stunned);
	}

	public override void Destroyed() => visual?.Dispose();
	public override void Render(Batcher batcher) => visual?.Draw(batcher, Color.White, silhouette: false);
	public override void RenderShadow(Batcher batcher, Color color) => visual?.Draw(batcher, color, silhouette: true);
	public override void RenderReflection(Batcher batcher, Color color) => visual?.Draw(batcher, color, silhouette: true);
	public override void RenderNormals(Batcher batcher) => visual?.DrawNormals(batcher);
	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context) => visual?.PerturbNeuralTexture(context);
	public override Rect GetVisualBounds() => new(Position.X - 30, Position.Y - 42, 60, 46);
}
