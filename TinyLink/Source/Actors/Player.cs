using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Player : Actor
{
	public enum States
	{
		Normal,
		Attack,
		Hurt,
		Start
	}

	public const int MaxHealth = 4;

	static float MaxGroundSpeed => 80;
	static float MaxAirSpeed => 90;
	static float GroundAccel => 500;
	static float AirAccel => 150;
	static float Friction => 500;
	static float AttackFriction => 150;
	static float HurtFriction => 200;
	static float Gravity => 450;
	static float JumpForce => -105;
	static float JumpTime => 0.18f;
	static float HurtDuration => 0.5f;
	static float DeathDuration => 1.5f;
	static float InvincibleDuration => 1.5f;

	public int Health = MaxHealth;
	public States State;
	public Controls Controls => Game.Controls;

	private float stateDuration;
	private float prevStateDuration;
	private float jumpTimer;
	private int footstepFrame = -1;
	private bool grounded;
	private bool ducking;
	private Light? light;

	public Player()
	{
		State = States.Start;
		Sprite = Assets.GetSprite("player");
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Mask = Masks.Player;
		IFrameTime = InvincibleDuration;
		grounded = true;
		Play("sword");
	}

	public override void Added()
	{
		light = Game.Create<Light>(Position);
		light.Radius = 36;
		light.Intensity = 0.45f;
		light.LightColor = 0xffcfa0;
		light.Height = 16;
		light.FlickerFrequency = 1.5f;
		light.FlickerIntensity = 0.02f;
		light.DrawSprite = false;
		light.KeepOnRoomTransition = true;
		UpdateLightPosition();
	}

	float JumpUpSquish = 0;

	public override void OnCollideY()
	{
		base.OnCollideY();
		JumpUpSquish = 0.1f;
	}

	public override void Update()
	{
		base.Update();
		UpdateLightPosition();

		// update grounded state
		var nowGrounded = Velocity.Y >= 0 && Grounded();
		if (nowGrounded && !grounded)
		{
			Squish = new Vector2(1.5f, 0.70f);
			AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this));
		}
		grounded = nowGrounded;

		// increment state timer
		var wasState = State;

		// state control
		switch (State)
		{
			case States.Normal:
				NormalState();
				break;
			case States.Attack:
				AttackState();
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
		if (ducking && State != States.Normal)
			ducking = false;
		if (ducking)
			Hitbox = new(new RectInt(-4, -6, 8, 6));
		else
			Hitbox = new(new RectInt(-4, -12, 8, 12));

		// variable jumping
		if (jumpTimer > 0)
		{
			Velocity.Y = JumpForce;
			jumpTimer -= Time.Delta;
			if (!Controls.Jump.Down)
				jumpTimer = 0;
		}

		// gravity
		if (!grounded)
		{
			float grav = Gravity;
			if (State == States.Normal && MathF.Abs(Velocity.Y) < 20 && Controls.Jump.Down)
				grav *= 0.40f;
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
		if (OverlapsFirst(Masks.Enemy | Masks.Hazard) is { } hit)
			hit.Hit(this);

		prevStateDuration = stateDuration;
		stateDuration += Time.Delta;
		if (State != wasState)
			stateDuration = 0.0f;
	}

	public void NormalState()
	{
		// update ducking state
		ducking = grounded && Controls.Move.IntValue.Y > 0;

		// get input
		var input = Controls.Move.IntValue.X;
		if (ducking)
			input = 0;

		int facing = input;

		// sprite
		if (grounded)
		{
			if (ducking)
				Play("duck");
			else if (input == 0)
				Play("idle");
			else if (Velocity.X > 0 && input < 0 || Velocity.X < 0 && input > 0)
			{
				facing *= -1;
				Play("skid");
			}
			else
				Play("run");
		}
		else
		{
			Play(JumpUpSquish > 0 ? "jump_squish" : "jump");
		}

		// horizontal movement
		{
			// Acceleration
			Velocity.X += input * (grounded ? GroundAccel : AirAccel) * Time.Delta;

			// Max Speed
			var maxSpeed = grounded ? MaxGroundSpeed : MaxAirSpeed;
			if (MathF.Abs(Velocity.X) > maxSpeed)
				Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * maxSpeed, 2000 * Time.Delta);

			// Friction
			if (input == 0 && grounded)
				Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);

			// Facing
			if (grounded && input != 0)
				Facing = facing;
		}

		// Start jumping
		if (grounded && Controls.Jump.ConsumePress())
		{
			JumpUpSquish = 0;
			Squish = new Vector2(0.65f, 1.4f);
			StopX();
			Velocity.X = input * MaxAirSpeed;
			jumpTimer = JumpTime;
			AudioEvent("jump");
		}

		// Begin Attack
		if (Controls.Attack.ConsumePress())
		{
			State = States.Attack;
			if (grounded)
				StopX();
		}

		// Force yourself to die
		if (Controls.Die.ConsumePress())
		{
			ApplyHurt(-100);
		}
	}

	private void UpdateFootsteps()
	{
		if (State != States.Normal || !grounded || !IsPlaying("run"))
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
		Play("attack", false);

		RectInt? hitbox = null;

		if (stateDuration < 0.2f)
		{
			hitbox = new (-16, -12, 17, 8);
		}
		else if (stateDuration < 0.50f)
		{
			if (prevStateDuration < 0.2f)
			{
				AudioEvent("sword");
			}
			hitbox = new (8, -8, 16, 8);
		}

		if (hitbox != null)
		{
			var it = hitbox.Value;
			if (Facing == Signs.Negative)
				it.X = -(it.X + it.Width);
			it += Position;

			if (Game.OverlapsFirst(it, Masks.Enemy | Masks.Hazard) is { } hit)
				Hit(hit);
		}

		if (Grounded())
			Velocity.X = Calc.Approach(Velocity.X, 0, AttackFriction * Time.Delta);

		if (stateDuration >= Animation.Duration)
		{
			Play("idle");
			State = States.Normal;
		}
	}

	public void HurtState()
	{
		if (stateDuration <= 0 && Health <= 0)
		{
			foreach (var actor in Game.Actors)
				if (actor != this)
					Game.Destroy(actor);
			Game.Shake(0.1f);
		}

		Velocity.X = Calc.Approach(Velocity.X, 0, HurtFriction * Time.Delta);

		if (stateDuration >= HurtDuration && Health > 0)
			State = States.Normal;

		if (stateDuration >= DeathDuration && Health <= 0)
		{
			Game.ReloadRoom(restartMusic: true);
		}
	}

	public void StartState()
	{
		if (stateDuration >= 1.0f)
			State = States.Normal;
	}

	public override void OnWasHit(Actor by)
	{
		ApplyHurt();
	}

	private void ApplyHurt(int healthDelta = -1) {
		Game.HitStun(0.1f);
		Game.Shake(0.1f);

		Play("hurt");

		Velocity = new Vector2(-Facing * 100, -80);
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
			AudioEvent("hurt");
		}
	}

	private void UpdateLightPosition()
	{
		if (light != null)
			light.Position = Position + new Point2(0, -6);
	}

	public override void Destroyed()
	{
		if (light != null)
		{
			Game.Destroy(light);
			light = null;
		}
	}
}
