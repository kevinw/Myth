using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public partial class Player : Actor
{
	public enum States
	{
		Normal,
		Roll,
		Dash,
		Attack,
		BlinkStrike,
		Hurt,
		Start
	}
	private readonly StateMachine<States> state = new(States.Start);

	public const int MaxHealth = 4;

	static float MaxGroundSpeed => 93;
	static float MaxAirSpeed => 99;
	static float CrawlSpeed => 28;
	static float GroundAccel => 400;
	static float AirAccel => 260;
	static float Friction => 480;
	static float AttackFriction => 150;
	static float HurtFriction => 200;
	static float Gravity => 450;
	static float WallHugGravityMultiplier => 0.25f;
	static float JumpForce => -105;
	static float JumpTime => 0.18f;
	static float CoyoteJumpTime => 0.08f;
	static float WallJumpSpeed => 125;
	static float WallJumpInputLockTime => 0.22f;
	static float RollMinHorizontalSpeed => 45;
	static float RollMinFallSpeed => 20;
	static float RollSpeed => 145;
	static float DownhillRollSpeed => 185;
	static float RollDuration => 0.36f;
	static float RollFriction => 220;
	static float DownhillRollFriction => 90;
	static float RollJumpForce => -82;
	static float RollJumpTime => 0.11f;
	static float RollJumpSpeed => 150;
	static float RollJumpCarryTime => 0.28f;
	static float DashSpeed => 220;
	static float DashDuration => 0.15f;
	static float DashCooldown => 0.20f;
	static float DuckAttackCooldown => 0.4f;
	static float AttackBlockOverlapThreshold => 0.5f;
	static float HurtDuration => 0.5f;
	static float DeathDuration => 1.5f;
	static float InvincibleDuration => 1.5f;
	static RectInt StandingHitboxRect => new(-4, -12, 8, 12);
	static RectInt DuckingHitboxRect => new(-4, -6, 8, 6);

	public int Health = MaxHealth;
	public States State
	{
		get => state.Current;
		set => state.Change(value);
	}
	public bool IsGrounded => grounded;
	public Controls Controls => Game.Controls;

	private float jumpTimer;
	private float coyoteJumpTimer;
	private int coyoteJumpY;
	private int footstepFrame = -1;
	private bool grounded;
	private bool ducking;
	private float wallJumpInputLockTimer;
	private int wallJumpDirection;
	private int dashDirection;
	private float dashCooldownTimer;
	private float dashRemainder;
	private float jumpForce = JumpForce;
	private float rollJumpCarryTimer;
	private float duckAttackCooldownTimer;
	private bool duckAttack;
	private bool attackBlocked;
	private Light? light;
	private SoundInstance? rollSound;
	float RunStartTimer; // Used to play a "running start" anim at the beginning of a run.
	bool RunStartSkid; // Used to know if we should "start running" with a skid (we're running the opposite direction we were facing.)

	public Player()
	{
		Sprite = Assets.GetSprite("player");
		Hitbox = new(StandingHitboxRect);
		Mask = Masks.Player;
		IFrameTime = InvincibleDuration;
		grounded = true;
		Play("sword");
	}

	public override void Added()
	{
		light = Game.Create<Light>(Position, it =>
		{
			it.Radius = 36;
			it.Intensity = 0.45f;
			it.LightColor = 0xffcfa0;
			it.Height = 16;
			it.FlickerFrequency = 1.5f;
			it.FlickerIntensity = 0.02f;
			it.DrawSprite = false;
			it.KeepOnRoomTransition = true;
		});
		UpdateLightPosition();
	}

	float JumpUpSquish = 0;
	private bool CanPlaySquishSound = false;

	public override void OnCollideY()
	{
		bool wasGoingUp = Velocity.Y < 0;
		base.OnCollideY();
		if (wasGoingUp && CanPlaySquishSound && State == States.Normal)
		{
			AudioEvent("player/squish");
			CanPlaySquishSound = false;
			JumpUpSquish = 0.15f;
		}
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		if (State == States.Roll)
		{
			StopRollSound(false);
			State = States.Normal;
		}
	}

	public override void Update()
	{
		var previousVelocity = Velocity;
		base.Update();
		UpdateLightPosition();

		// update grounded state
		var nowGrounded = Velocity.Y >= 0 && Grounded();
		if (nowGrounded && !grounded)
		{
			Squish = new Vector2(1.5f, 0.70f);
			AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this, searchOutward: true));
			CanPlaySquishSound = true;
			TryStartRoll(previousVelocity);
		}
		grounded = nowGrounded;
		if (grounded)
		{
			coyoteJumpTimer = CoyoteJumpTime;
			coyoteJumpY = Position.Y;
			wallJumpInputLockTimer = 0;
			rollJumpCarryTimer = 0;
		}
		else
		{
			if (coyoteJumpTimer > 0)
				coyoteJumpTimer = Calc.Approach(coyoteJumpTimer, 0, Time.Delta);
			if (wallJumpInputLockTimer > 0)
				wallJumpInputLockTimer = Calc.Approach(wallJumpInputLockTimer, 0, Time.Delta);
		}
		if (!grounded && rollJumpCarryTimer > 0)
			rollJumpCarryTimer = Calc.Approach(rollJumpCarryTimer, 0, Time.Delta);
		if (duckAttackCooldownTimer > 0)
			duckAttackCooldownTimer = Calc.Approach(duckAttackCooldownTimer, 0, Time.Delta);
		if (dashCooldownTimer > 0)
			dashCooldownTimer = Calc.Approach(dashCooldownTimer, 0, Time.Delta);

		// state control
		switch (State)
		{
			case States.Normal:
				NormalState();
				break;
			case States.Roll:
				RollState();
				break;
			case States.Dash:
				DashState();
				break;
			case States.Attack:
				AttackState();
				break;
			case States.BlinkStrike:
				BlinkStrikeState();
				break;
			case States.Hurt:
				HurtState();
				break;
			case States.Start:
				StartState();
				break;
		}

		UpdateFootsteps();

		// ducking collider(s)
		if (ducking && State != States.Normal && State != States.Roll && !(State == States.Attack && duckAttack))
			ducking = false;
		if (ducking || State == States.Roll)
			Hitbox = new(DuckingHitboxRect);
		else
			Hitbox = new(StandingHitboxRect);

		// variable jumping
		if (jumpTimer > 0)
		{
			Velocity.Y = jumpForce;
			jumpTimer -= Time.Delta;
			if (!Controls.Jump.Down)
				jumpTimer = 0;
		}

		// gravity
		if (!grounded && State != States.Dash)
		{
			float grav = Gravity;
			if (State == States.Normal && MathF.Abs(Velocity.Y) < 20 && Controls.Jump.Down)
				grav *= 0.40f;
			if (State == States.Normal && Velocity.Y > 0 && IsWallHugging())
				grav *= WallHugGravityMultiplier;
			Velocity.Y += grav * Time.Delta;

			JumpUpSquish -= Time.Delta;
			if (JumpUpSquish <= 0) JumpUpSquish = 0;
		}

		// goto next room
		if (Health > 0)
		{
			if (Position.X > Game.Bounds.Right && !Game.Transition(Point2.Right))
			{
				Position.X = Game.Bounds.Right;
			}
			else if (Position.X < Game.Bounds.Left && !Game.Transition(Point2.Left))
			{
				Position.X = Game.Bounds.Left;
			}
			else if (Position.Y > Game.Bounds.Bottom + 12 && !Game.Transition(Point2.Down))
			{
				ApplyHurt(-1000);
			}
			else if (Position.Y < Game.Bounds.Top)
			{
				if (Game.Transition(Point2.Up))
					Velocity.Y = -150;
				else
					Position.Y = Game.Bounds.Top;
			}
		}

		// detect getting hit
		if (State == States.Dash)
		{
			if (OverlapsFirst(Masks.Hazard) is { } hit)
				hit.Hit(this);
		}
		else if (OverlapsFirst(Masks.Enemy | Masks.Hazard) is { } hit)
			hit.Hit(this);

		state.Update(State == States.Roll && RollingDownSlope() ? 0 : Time.Delta);
	}

	public void NormalState()
	{
		// update ducking state
		if (grounded && Controls.Move.IntValue.Y > 0)
			ducking = true;
		else if (ducking)
			ducking = grounded && !CanStandUp();
		else
			ducking = false;

		// get input
		var input = Controls.Move.IntValue.X;

		int facing = input;
		var movementInput = input;
		if (!grounded && wallJumpInputLockTimer > 0)
			movementInput = wallJumpDirection;

		// sprite
		bool didRun = false;
		bool didStartRunning = false;
		if (grounded)
		{
			if (ducking)
				Play(input == 0 ? "duck" : "crawl");
			else if (input == 0)
				Play("idle");
			else if (Velocity.X > 0 && input < 0 || Velocity.X < 0 && input > 0)
			{
				facing *= -1;
				Play("skid");
				RunStartTimer = 0;
				RunStartSkid = false;
			}
			else
			{
				RunStartTimer += Time.Delta;
				didRun = true;
				if (Velocity.X == 0) didStartRunning = true;
				if (RunStartTimer < 0.35f)
				{
					if (RunStartTimer < 0.15f && RunStartSkid)
						Play("skid");
					else
						Play("run_start");
				}
				else
					Play("run");
			}
		}
		else
		{
			var wallJumpDir = GetWallJumpDirection();
			if (IsWallHugging(wallJumpDir))
			{
				Facing = -(Signs)wallJumpDir;
				Play("wall_slide");
			}
			else if (JumpUpSquish > 0 && !grounded)
			{
				Play("jump_squish");
			}
			else
			{
				if (Velocity.X != 0 && input != 0 && Math.Sign(Velocity.X) != Math.Sign(input))
				{
					Play("jump_turn");
				}
				else
				{
					Play("jump");
				}
			}
		}

		if (!didRun)
		{
			RunStartTimer = 0;
			RunStartSkid = false;
		}

		// horizontal movement
		var maxSpeed = grounded ? MaxGroundSpeed : MaxAirSpeed;
		var lastFacing = Facing;
		if (ducking)
			maxSpeed = CrawlSpeed;
		{
			// Acceleration
			Velocity.X += movementInput * (grounded ? GroundAccel : AirAccel) * Time.Delta;

			// Max Speed
			if (!grounded && wallJumpInputLockTimer > 0)
				maxSpeed = MathF.Max(maxSpeed, WallJumpSpeed);
			if (!grounded && rollJumpCarryTimer > 0)
				maxSpeed = MathF.Max(maxSpeed, RollJumpSpeed);
			if (MathF.Abs(Velocity.X) > maxSpeed)
				Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * maxSpeed, 2000 * Time.Delta);

			// Friction
			if (input == 0 && grounded)
				Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);

			// Facing
			if (grounded && input != 0)
			{
				Facing = facing;
				if (lastFacing != Facing && didStartRunning)
				{
					RunStartSkid = true;
				}
			}
		}

		// Start jumping
		var wallJump = GetWallJumpDirection();
		var canCoyoteJump = CanCoyoteJump();
		var canGroundJump = grounded && CanStandUp() || canCoyoteJump;
		var canWallJump = wallJump != 0 && input != 0;
		if (Controls.Jump.Pressed && (canGroundJump || canWallJump))
		{
			Controls.Jump.ConsumePress();
			JumpUpSquish = 0;
			Squish = new Vector2(0.65f, 1.4f);
			if (canGroundJump)
			{
				if (canCoyoteJump)
					Position.Y = coyoteJumpY;
				coyoteJumpTimer = 0;
				// we used to instantly snap to MaxAirAccel here but I'm not sure that's what I want...
				// maybe a slight speed boost up to a point would be nice. want to encourage tech.
			}
			else
			{
				wallJumpDirection = wallJump;
				wallJumpInputLockTimer = WallJumpInputLockTime;
				facing = wallJumpDirection;
				Facing = wallJumpDirection;
				Velocity.X = wallJumpDirection * WallJumpSpeed;
			}
			StartJump(JumpForce, JumpTime);
			AudioEvent("jump");
		}

		// Begin Attack
		if (Controls.Dash.ConsumePress() && TryStartDash(input))
		{
			return;
		}
		else if (Controls.BlinkStrike.ConsumePress())
		{
			blinkStrikeTarget = FindBlinkStrikeTarget();
			if (blinkStrikeTarget != null)
			{
				duckAttack = false;
				attackBlocked = false;
				blinkStrikeResolved = false;
				State = States.BlinkStrike;
				Play(GetAttackAnimation(), false);
				return;
			}
		}
		else if (Controls.Attack.ConsumePress())
		{
			if (!grounded && input != 0)
				Facing = input;

			if (ducking && duckAttackCooldownTimer <= 0)
			{
				duckAttack = true;
				attackBlocked = IsAttackBlocked(duckAttack);
				duckAttackCooldownTimer = DuckAttackCooldown;
				State = States.Attack;
				if (grounded)
					StopX();
			}
			else if (!ducking)
			{
				duckAttack = false;
				attackBlocked = false;
				State = States.Attack;
				if (grounded)
					StopX();
			}
		}
		else if (Controls.Saw.ConsumePress())
		{
			Game.Create<Saw>(Position - new Point2(-5 * Facing, 10), (saw) =>
			{
				saw.Facing = Facing;
			});
		}

		// Force yourself to die
		if (Controls.Die.ConsumePress())
		{
			ApplyHurt(-100);
		}
	}

	private void TryStartRoll(Vector2 impactVelocity)
	{
		if (State != States.Normal) return;
		if (Controls.Move.IntValue.Y <= 0) return;
		if (impactVelocity.Y < RollMinFallSpeed) return;
		if (MathF.Abs(impactVelocity.X) < RollMinHorizontalSpeed) return;

		Facing = Math.Sign(impactVelocity.X);
		Velocity = new(Facing * RollSpeed, 0);
		ducking = true;
		Squish = new(1.65f, 0.62f);
		Play("roll");
		State = States.Roll;
		StartRollSound();
	}

	private void RollState()
	{
		ducking = true;
		Play("roll");

		if (!grounded)
		{
			StopRollSound(false);
			State = States.Normal;
			return;
		}

		Game.SetAudioEventPosition(rollSound, Position, Game.GetFirst(Masks.Player)?.Position);
		var rollingDownSlope = RollingDownSlope();
		var targetSpeed = rollingDownSlope ? DownhillRollSpeed : MaxGroundSpeed;
		var friction = rollingDownSlope ? DownhillRollFriction : RollFriction;
		Velocity.X = Calc.Approach(Velocity.X, Facing * targetSpeed, friction * Time.Delta);

		if (Controls.Jump.ConsumePress())
		{
			StopRollSound(false);
			JumpUpSquish = 0;
			Squish = new Vector2(0.75f, 1.25f);
			Velocity.X = Facing * MathF.Max(MathF.Abs(Velocity.X), RollJumpSpeed);
			rollJumpCarryTimer = RollJumpCarryTime;
			StartJump(RollJumpForce, RollJumpTime);
			State = States.Normal;
			AudioEvent("jump");
			return;
		}

		if (state.Time >= RollDuration || Controls.Move.IntValue.Y <= 0)
		{
			StopRollSound(false);
			State = States.Normal;
			ducking = grounded && Controls.Move.IntValue.Y > 0;
		}
	}

	private bool TryStartDash(int input)
	{
		if (dashCooldownTimer > 0)
			return false;

		dashDirection = input != 0 ? Math.Sign(input) : Facing;
		if (dashDirection == 0)
			dashDirection = Signs.Positive;

		Facing = dashDirection;
		dashRemainder = 0;
		dashCooldownTimer = DashCooldown;
		jumpTimer = 0;
		Velocity = Vector2.Zero;
		ducking = false;
		Squish = new Vector2(1.35f, 0.82f);
		Play("jump");
		State = States.Dash;
		return true;
	}

	private void DashState()
	{
		ducking = false;
		Play("jump");
		Velocity = Vector2.Zero;
		MoveDash(DashSpeed * Time.Delta * dashDirection);

		if (state.Time >= DashDuration)
			State = States.Normal;
	}

	private void MoveDash(float value)
	{
		dashRemainder += value;
		var move = (int)dashRemainder;
		dashRemainder -= move;

		while (move != 0)
		{
			var sign = Math.Sign(move);
			var bounds = Hitbox.Rect + Position + new Point2(sign, 0);
			if (Game.OverlapsRoomCollision(bounds))
			{
				dashRemainder = 0;
				State = States.Normal;
				return;
			}

			Position.X += sign;
			move -= sign;
		}
	}

	private bool RollingDownSlope()
	{
		if (!grounded || Velocity.LengthSquared() <= 0.001f)
			return false;

		var bounds = Hitbox.Rect + Position;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out var hit) || !hit.Slope.HasValue)
			return false;

		var normal = hit.Normal;
		if (normal.Y >= -0.001f)
			return false;

		var downhill = new Vector2(normal.Y, -normal.X);
		if (downhill.Y < 0)
			downhill = -downhill;

		return Vector2.Dot(Velocity, downhill) > 1;
	}

	private void StartRollSound()
	{
		StopRollSound(true);

		try
		{
			rollSound = Game.CreateAudioEvent("player/roll", Position);
			rollSound?.Start();
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			rollSound?.Release();
			rollSound = null;
		}
	}

	private void StopRollSound(bool immediate)
	{
		if (rollSound == null)
			return;

		rollSound.Stop(immediate);
		rollSound.Release();
		rollSound = null;
	}
	private void StartJump(float force, float duration)
	{
		jumpForce = force;
		jumpTimer = duration;
	}

	private bool CanCoyoteJump()
	{
		if (grounded || coyoteJumpTimer <= 0)
			return false;

		var originalY = Position.Y;
		Position.Y = coyoteJumpY;
		var canJump = CanStandUp();
		Position.Y = originalY;
		return canJump;
	}

	private bool CanStandUp()
	{
		var standingBounds = StandingHitboxRect + Position;
		if (Game.OverlapsRoomCollision(standingBounds))
			return false;

		foreach (var other in Game.Actors)
		{
			if (other == this || other is Grid || !other.Mask.Has(Masks.Solid))
				continue;

			if (other.Hitbox.Overlaps(standingBounds - other.Position))
				return false;
		}

		return true;
	}

	private int GetWallJumpDirection()
	{
		if (grounded)
			return 0;

		var touchingLeft = OverlapsAny(Point2.Left, Masks.Solid);
		var touchingRight = OverlapsAny(Point2.Right, Masks.Solid);
		if (touchingLeft == touchingRight)
			return 0;

		return touchingLeft ? 1 : -1;
	}

	private bool IsWallHugging()
		=> IsWallHugging(GetWallJumpDirection());

	private bool IsWallHugging(int wallJumpDirection)
		=> wallJumpDirection != 0 && Controls.Move.IntValue.X == -wallJumpDirection;

	private void UpdateFootsteps()
	{
		if (State != States.Normal || !grounded || !IsPlaying("run") || !IsPlaying("run_start"))
		{
			footstepFrame = -1;
			return;
		}

		var frame = AnimationFrameIndex;
		if (frame < 0 || frame == footstepFrame)
			return;

		footstepFrame = frame;

		var animation = Animation;
		var localFrame = frame - animation.FrameStart;
		if (localFrame >= 0 && localFrame % 2 == 0)
		{
			AudioEvent("footstep", "Type", Game.GetSurfaceTypeBelow(this));
		}
	}

	public void AttackState()
	{
		if (duckAttack)
			ducking = true;

		Play(GetAttackAnimation(), false);

		RectInt? hitbox = null;

		if (attackBlocked)
		{
			if (state.PreviousTime <= 0)
			{
				AudioEvent("player/sword_block");
			}
		}
		else if (duckAttack)
		{
			if (state.PreviousTime <= 0)
				AudioEvent("sword_crouched");
			if (state.Time < Animation.Duration)
				hitbox = GetAttackHitbox(duckAttack);
		}
		else if (state.Time < 0.2f)
		{
			hitbox = new(-16, -12, 17, 8);
		}
		else if (state.Time < 0.50f)
		{
			if (state.PreviousTime < 0.2f)
			{
				attackBlocked = IsAttackBlocked(duckAttack);
				if (attackBlocked)
				{
					Play(GetAttackAnimation(), false);
					AudioEvent("player/sword_block");
				}
				else
				{
					AudioEvent("sword");
				}
			}
			if (!attackBlocked)
				hitbox = GetAttackHitbox(duckAttack);
		}

		if (hitbox is { } it)
		{
			if (Facing == Signs.Negative)
				it.X = -(it.X + it.Width);
			it += Position;

			Game.CollisionRectsThisFrame.Add(it);
			if (Game.OverlapsFirst(it, Masks.Enemy | Masks.Hazard) is { } hit)
				Hit(hit);

			Game.BreakTiles(it);
		}

		if (Grounded())
			Velocity.X = Calc.Approach(Velocity.X, 0, AttackFriction * Time.Delta);

		var attackFinished = attackBlocked
			? IsFinishedPlaying(GetAttackAnimation())
			: state.Time >= Animation.Duration;
		if (attackFinished)
		{
			attackBlocked = false;
			if (duckAttack)
			{
				duckAttack = false;
				Play("duck");
			}
			else
			{
				Play("idle");
			}
			State = States.Normal;
		}
	}

	private bool IsAttackBlocked(bool duckingAttack)
	{
		var hitbox = GetAttackHitbox(duckingAttack);
		if (Facing == Signs.Negative)
			hitbox.X = -(hitbox.X + hitbox.Width);
		hitbox += Position;

		return Game.GetRoomCollisionOverlapRatio(hitbox, includeBreakableTiles: false) >= AttackBlockOverlapThreshold;
	}

	private RectInt GetAttackHitbox(bool duckingAttack)
		=> duckingAttack
			? new RectInt(3, -6, 13, 6)
			: new RectInt(8, -9, 16, 9);

	private string GetAttackAnimation()
	{
		if (duckAttack)
		{
			return attackBlocked ? "duck_attack_block" : "duck_attack";
		}

		return attackBlocked ? "attack_block" : "attack";
	}

	public void HurtState()
	{
		if (state.Time <= 0 && Health <= 0)
		{
			foreach (var actor in Game.Actors)
				if (actor != this)
					Game.Destroy(actor);
			Game.Shake(0.1f);
		}

		Velocity.X = Calc.Approach(Velocity.X, 0, HurtFriction * Time.Delta);

		if (state.Time >= HurtDuration && Health > 0)
			State = States.Normal;

		if (state.Time >= DeathDuration && Health <= 0)
		{
			Game.ReloadRoom(restartMusic: true);
		}
	}

	public void StartState()
	{
		if (state.Time >= 1.0f)
			State = States.Normal;
	}

	public override void OnWasHit(Actor by)
	{
		ApplyHurt(fromActor: by);
	}

	private void ApplyHurt(int healthDelta = -1, Actor? fromActor = null)
	{
		Game.HitStun(0.1f);
		Game.Shake(0.1f);
		Game.PulseDitherGlitch(HurtGlitchAxis(fromActor));

		Play("hurt");

		// Apply a hurt velocity
		int signDirection = -Facing;
		if (fromActor != null)
		{
			// Get pushed away from the actor that hurt us.
			var actorDelta = Math.Sign(Position.X - fromActor.Position.X);
			if (actorDelta != 0)
				signDirection = actorDelta;
		}
		Velocity = new Vector2(signDirection * 100, -80);
		State = States.Hurt;

		var wasHealth = Health;
		Health += healthDelta;

		if (Health <= 0 && wasHealth > 0)
		{
			Game.StopMusic();
			AudioEvent("death");
		}
		else
		{
			if (healthDelta < 0 && wasHealth > 0)
				Game.TriggerPlayerHitSnapshot();
			AudioEvent("hurt");
		}
	}

	private Vector2 HurtGlitchAxis(Actor? fromActor)
	{
		if (fromActor != null)
		{
			var axis = Position - fromActor.Position;
			if (axis.LengthSquared() > 0.001f)
				return Vector2.Normalize(axis);
		}

		if (Velocity.LengthSquared() > 0.001f)
			return Vector2.Normalize(Velocity);

		return new Vector2(-Facing, 0);
	}

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		RenderBlink(batcher);
	}

	private void UpdateLightPosition()
	{
		if (light != null)
			light.Position = Position + new Point2(0, -6);
	}

	public override void Destroyed()
	{
		StopRollSound(true);

		if (light != null)
		{
			Game.Destroy(light);
			light = null;
		}
	}
}
