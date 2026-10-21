using System.Numerics;
using Foster.Framework;

namespace Myth;

// A nimble swordsman Bud's size, sculpted, sword in his right fist. He paths with the reach field and burrows like
// Bud, and keeps you at sword's length:
// - Spacing: closes to SpacingRange and holds there; crowd him and he either kicks you off, hard, or hops back out of
//   your reach, still facing you.
// - Cut: in reach, he plants and swings, a quick slash or, now and then, an overhead chop with a longer raise. The
//   raise is the tell. Only the blade hurts, and only while it cuts, so between cuts you can stand toe to toe with him.
// - Ambush: now and then, when there's a wall spot above you he can get to, he goes there, burrowing up the wall if
//   that's the way, and waits buried for AmbushTime, his burrow line glowing where he'll come from. Then he pops off
//   the wall and plunges sword-first: the down smash, which quakes the floor where it lands and leaves him open.
// - Pair: two of them either side of you wind up together and throw their swords at each other, one at your head and
//   one at your feet, spinning; each catches the other's. Until the blades come home both are unarmed, and one that
//   misses his catch has to go pick his sword up off the floor before he can cut again.
// Every hit makes him flinch; five and he falls, and lies there a moment before he's gone.
[GameActor(Sprite = "bud", Tags = ActorTag.Enemy, Anchor = SpriteAnchor.BottomCenter, LivePositionEditable = true)]
public class FootSoldier : Pawn
{
	[ActorSettings(typeof(HumanoidRigSettings))] public readonly HumanoidRigSettings Rig = new(); // body and sword from Assets/Actors/foot-soldier.json
	[I(1, 20)] public int Health = 5;
	[I(0, 4)] public float AmbushTime = 1; // how long he waits buried above you before the smash
	[I(32, 320)] public float SightRange = 180;

	static ReachManner Manner => new(WindupMin: 0.05f, WindupMax: 0.3f, MissHesitation: 0.1f);
	static float SpacingRange => 22f; // px he holds off you
	static float CrowdRange => 10f; // closer than this he hops back
	static float BackHopSpeed => 120f;
	static float BackHopTime => 0.22f;
	static float BackHopFriction => 260f;
	static float BackHopCooldown => 0.8f;
	static float CutReach => 28f;
	static float CutHeight => 14f;
	static float CutCooldown => 1.1f;
	// A swing: the take, when in it (seconds) it hurts, and the lunge as it starts to.
	readonly record struct Swing(string Take, float HitStart, float HitEnd, float Lunge);
	static Swing Slash => new("Sword_Regular_A", 0.25f, 0.5f, 70f);
	static Swing Chop => new("SoldierChop", 0.7f, 0.95f, 30f); // overhead, two-handed, a longer raise
	static float ChopChance => 0.4f;
	static Swing Kick => new("SoldierKick", 0.3f, 0.6f, 20f);
	static float KickChance => 0.5f; // crowded, how often he kicks you off rather than hopping back
	static float KickCooldown => 2f;
	static Point2 KickReach => new(14, 10); // px in front of him, at the waist, the kick lands in
	static Vector2 KickPush => new(220, -110); // how hard it sends you, away from him
	static string RunTake => "SoldierRun";
	static string FlinchTake => "SoldierFlinch";
	static string DeathTake => "SoldierDeath";
	static float DeathLinger => 0.4f; // lying still after the fall before he's gone
	static float AmbushCooldown => 4f;
	static float AmbushMinHeight => 24f; // px above you a wall spot has to be
	static float AmbushMaxHeight => 96f;
	static float AmbushReach => 56f; // px either side of you
	static float AmbushPatience => 5f;
	static float AmbushArrive => 12f; // px from the wall spot that counts as there, sliding
	static float SmashSpeed => 260f;
	static float SmashAlign => 4f; // px off your x he plunges from
	static float SmashMissTime => 1.5f;
	static float SmashRecover => 0.8f;
	static float SmashWaveRange => 56f;
	static Color SmashWaveColor => new(0xc8d4e0);
	static float PairMinRange => 24f; // px either side of you each of the two has to be
	static float PairRange => 120f;
	static float PairCooldown => 6f;
	static string PairTake => "OverhandThrow";
	static float PairWindup => 0.6f; // until the throw
	static float PairCatchTimeout => 2.5f;
	static int HighLane => -12; // px above the feet a blade flies at: one at your head, one at your feet
	static int LowLane => -3;
	static float HurtTime => 0.45f;
	static float HurtKnockback => 90f;

	enum Mode { Idle, Engage, BackHop, Cut, Kick, Ambush, Smash, Recover, Pair, Hurt, Dying }

	readonly ReachSense sense = new(ReachBody.Bud);
	readonly ReachPilot pilot;
	Mode mode;
	float modeTime;
	float cutCooldown;
	float backHopCooldown;
	float ambushCooldown = AmbushCooldown / 2;
	float buried; // how long he's waited buried this ambush
	Point2 ambushSpot;
	bool lunged;
	Swing swing;
	bool kickLive;
	float kickCooldown;
	float lyingStill;
	bool plunging;
	bool swordLive;
	bool armed = true;
	float swordLength; // as authored (and live-edited while he holds it); drawn as none while he's unarmed
	float pairCooldown = PairCooldown / 2;
	FootSoldier? partner;
	int lane;
	bool thrown;
	ThrownBlade? fetching; // a dropped sword he's going to pick up
	int health;

	public FootSoldier()
	{
		pilot = new(this, () => Manner);
		Sprite = Assets.GetSprite(nameof(Bud));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
	}

	protected override PawnAbilities Abilities => PawnAbilities.Burrow;
	protected override HumanoidRigSettings? DrawRig => Rig;
	internal override float MaxBurrowDuration => base.MaxBurrowDuration + AmbushTime;
	internal override bool ContactDamageEnabled => swordLive || kickLive;
	protected override string RigTakeFor(string name) => name is "PlayerRun" or "Jog_Fwd_Loop" or "Sprint_Loop" ? RunTake : name;

	public override void Added()
	{
		base.Added();
		Hitbox = new(StandingHitboxRect);
		health = Health;
		swordLength = Rig.Sculpt.Sword;
	}

	// Waiting, unarmed, on his partner's sword.
	internal bool Receiving => mode == Mode.Pair && thrown && !armed;
	internal Vector2 CatchPoint(int lane) => Position + new Vector2((int)Facing * 4, lane);

	internal bool TryCatch()
	{
		if (!Receiving)
			return false;
		Arm();
		Squish = new(1.15f, 0.9f);
		return true;
	}

	// Only the blade hurts, and only while it's cutting; plunging, all of him does, and kicking, the space in front of
	// him. The body takes the hits.
	internal override bool OverlapsCombatBounds(in RectInt bounds, CombatRole role = CombatRole.Vulnerable)
		=> role != CombatRole.Harmful ? base.OverlapsCombatBounds(bounds, role)
			: swordLive && (plunging && Hitbox.Overlaps(bounds - Position) || RigVisual?.BodyOverlaps(bounds, CombatRole.Harmful) == true) ||
			kickLive && KickRect.Overlaps(bounds);

	RectInt KickRect => new(Position.X + (Facing > 0 ? 0 : -KickReach.X), Position.Y - 12, KickReach.X, KickReach.Y);

	// The kick sends you flying, much further than a cut.
	public override void OnPerformHit(Actor hitting)
	{
		if (kickLive && hitting is Player player)
			player.Velocity = KickPush * new Vector2((int)Facing, 1);
	}

	public override void Update()
	{
		var player = Game.GetFirst<Player>();
		modeTime += Time.Delta;
		cutCooldown -= Time.Delta;
		backHopCooldown -= Time.Delta;
		kickCooldown -= Time.Delta;
		ambushCooldown -= Time.Delta;
		pairCooldown -= Time.Delta;
		if (armed)
			swordLength = Rig.Sculpt.Sword;
		else
			Rig.Sculpt.Sword = 0;
		if (mode == Mode.Idle && player != null && Sees(player))
			Begin(Mode.Engage);
		if (mode != Mode.Idle && player != null && Game.room is { } room)
			sense.Update(Game, room, mode == Mode.Ambush ? ReachBody.Bud.Stand + ambushSpot :
				fetching != null ? ReachBody.Bud.Stand + fetching.Position : player.Hitbox.Rect + player.Position);
		base.Update();
	}

	void Begin(Mode next)
	{
		(mode, modeTime) = (next, 0);
		swordLive = kickLive = plunging = lunged = false;
		buried = 0;
		pilot.Cancel();
	}

	public override PawnInput GetInput()
	{
		if (Game.GetFirst<Player>() is not { } player)
			return default;
		var to = (Vector2)(player.Position - Position);
		var side = Math.Sign(to.X);
		return mode switch
		{
			Mode.Engage => Engage(player, to, side),
			Mode.Cut or Mode.Kick => Swinging(),
			Mode.Dying => Dying(),
			Mode.Ambush => Ambush(player),
			Mode.Smash => Smash(player, to),
			Mode.Pair => Pair(player),
			Mode.Recover or Mode.Hurt => Rest(mode == Mode.Recover ? SmashRecover : HurtTime),
			_ => default,
		};
	}

	PawnInput Engage(Player player, Vector2 to, int side)
	{
		var input = sense.Field is { } field ? pilot.GetInput(field) : default;
		if (pilot.Label.StartsWith("no ") && side != 0)
			input = input with { MoveX = side };
		if (!armed)
			return Fetch(input);
		if (!grounded)
			return input;
		if (ambushCooldown <= 0 && FindAmbush(player, out ambushSpot))
		{
			Begin(Mode.Ambush);
			return input;
		}
		if (MathF.Abs(to.Y) >= CutHeight)
			return input;
		if (side != 0)
			Facing = side;
		if (pairCooldown <= 0 && FindPartner(player) is { } other)
		{
			StartPair(other, HighLane);
			other.StartPair(this, LowLane);
			return default;
		}
		var reach = MathF.Abs(to.X);
		if (reach < CrowdRange)
		{
			if (kickCooldown <= 0)
			{
				kickCooldown = KickCooldown;
				if (Game.rng.Chance(KickChance))
				{
					StartSwing(Mode.Kick, Kick);
					return default;
				}
			}
			if (backHopCooldown <= 0)
			{
				BackHop();
				return default;
			}
		}
		if (reach <= CutReach && cutCooldown <= 0)
		{
			StartSwing(Mode.Cut, Game.rng.Chance(ChopChance) ? Chop : Slash);
			return default;
		}
		return reach <= SpacingRange ? input with { MoveX = 0 } : input;
	}

	// Rounds on you and hops back out of your reach, like Bud ducking a swing.
	void BackHop()
	{
		Begin(Mode.BackHop);
		backHopCooldown = BackHopCooldown;
		Velocity.X = -(int)Facing * BackHopSpeed;
		jumpTimer = 0;
		Squish = new(0.75f, 1.3f);
		State = States.BackDash;
		PlayPawnAnimation("back_dash", looping: false);
	}

	protected override void CustomState()
	{
		if (State != States.BackDash)
			return;
		Velocity.X = Calc.Approach(Velocity.X, 0, BackHopFriction * Time.Delta);
		if (modeTime < BackHopTime)
			return;
		State = States.Normal;
		PlayPawnAnimation("idle");
		Begin(Mode.Engage);
	}

	void StartSwing(Mode next, Swing with)
	{
		Begin(next);
		swing = with;
		PreviewRigTake(with.Take);
	}

	// Planted, the blade (or the kick) hurts through the middle of the swing, with a lunge as it starts to.
	PawnInput Swinging()
	{
		var live = modeTime >= swing.HitStart && modeTime < swing.HitEnd;
		(swordLive, kickLive) = mode == Mode.Cut ? (live, false) : (false, live);
		if (live && !lunged)
		{
			lunged = true;
			Velocity.X += (int)Facing * swing.Lunge;
		}
		if (modeTime >= swing.HitEnd && RigPreview == null)
		{
			if (mode == Mode.Cut)
				cutCooldown = CutCooldown;
			Begin(Mode.Engage);
		}
		return default;
	}

	// A wall spot above you he can get to, the highest there is: a Wall node AmbushMinHeight to AmbushMaxHeight above
	// you and within AmbushReach either side. High up a tall wall is a burrow, and a long drop.
	bool FindAmbush(Player player, out Point2 spot)
	{
		spot = default;
		if (sense.Field is not { } field)
			return false;
		var best = float.MaxValue;
		foreach (var node in field.Nodes)
		{
			var rise = player.Position.Y - node.Position.Y;
			if (node.Kind != ReachField.NodeKind.Wall || rise < AmbushMinHeight || rise > AmbushMaxHeight ||
				Math.Abs(node.Position.X - player.Position.X) > AmbushReach)
				continue;
			if (-rise < best)
				(best, spot) = (-rise, node.Position);
		}
		return best < float.MaxValue;
	}

	// The pilot gets him to the wall spot. Buried in the wall when it would let go and pop, he holds there instead
	// for AmbushTime; once out on the wall, the smash.
	PawnInput Ambush(Player player)
	{
		if (modeTime > AmbushPatience || pilot.Phase == ReachPilot.Phases.Stuck)
		{
			ambushCooldown = AmbushCooldown;
			Begin(Mode.Engage);
			return default;
		}
		var input = sense.Field is { } field ? pilot.GetInput(field) : default;
		if (State == States.Burrow && !input.JumpDown && buried < AmbushTime)
		{
			buried += Time.Delta;
			Facing = Math.Sign(player.Position.X - Position.X) is var side and not 0 ? side : (int)Facing;
			return new(0, 0, false, JumpDown: true, false);
		}
		if (State != States.Burrow && !grounded &&
			(buried > 0 || WallSliding && Vector2.DistanceSquared(Position, ambushSpot) <= AmbushArrive * AmbushArrive))
			Begin(Mode.Smash);
		return input;
	}

	// Off the wall toward you, then, once over you, straight down sword-first.
	PawnInput Smash(Player player, Vector2 to)
	{
		if (plunging)
		{
			Velocity = new(0, MathF.Max(Velocity.Y, SmashSpeed));
			return default;
		}
		if (grounded || modeTime > SmashMissTime)
		{
			ambushCooldown = AmbushCooldown;
			Begin(Mode.Engage);
			return default;
		}
		var side = Math.Sign(to.X);
		if (WallSliding)
			return new(side, 0, JumpPressed: true, JumpDown: false, false);
		if (MathF.Abs(to.X) <= SmashAlign && to.Y > 8)
		{
			plunging = swordLive = true;
			Velocity.X = 0;
			return default;
		}
		return new(Math.Clamp(to.X / 8, -1, 1), 0, false, false, false);
	}

	// Another soldier ready to throw with him from your other side, both level with you and in range.
	FootSoldier? FindPartner(Player player)
	{
		var mine = Position.X - player.Position.X;
		if (MathF.Abs(mine) < PairMinRange || MathF.Abs(mine) > PairRange)
			return null;
		foreach (var actor in Game.Actors)
		{
			if (actor is not FootSoldier other || other == this || other.mode != Mode.Engage || !other.armed ||
				!other.grounded || other.pairCooldown > 0)
				continue;
			var theirs = other.Position.X - player.Position.X;
			if (MathF.Sign(theirs) == -MathF.Sign(mine) && MathF.Abs(theirs) >= PairMinRange && MathF.Abs(theirs) <= PairRange &&
				MathF.Abs(other.Position.Y - player.Position.Y) < CutHeight)
				return other;
		}
		return null;
	}

	void StartPair(FootSoldier with, int lane)
	{
		Begin(Mode.Pair);
		(partner, this.lane, thrown, pairCooldown) = (with, lane, false, PairCooldown);
		Facing = Math.Sign(with.Position.X - Position.X) is var side and not 0 ? side : (int)Facing;
		PreviewRigTake(PairTake);
	}

	// Both wind up together; if his partner's knocked out of it first, he doesn't throw. Thrown, he waits on the
	// partner's sword.
	PawnInput Pair(Player player)
	{
		if (!thrown)
		{
			if (partner is not { mode: Mode.Pair })
				Begin(Mode.Engage);
			else if (modeTime >= PairWindup)
				Throw();
			return default;
		}
		if (armed || modeTime > PairWindup + PairCatchTimeout)
			Begin(Mode.Engage);
		return default;
	}

	void Arm() => (armed, Rig.Sculpt.Sword) = (true, swordLength);

	void Throw()
	{
		(thrown, armed) = (true, false);
		Game.Create<ThrownBlade>(Position + new Point2((int)Facing * 4, lane), blade =>
		{
			blade.Catcher = partner;
			blade.Lane = lane;
			blade.Length = swordLength * Rig.PixelsPerMeter;
			blade.BladeColor = Rig.Sculpt.BladeColor;
			blade.HiltColor = Rig.Sculpt.GripColor;
		});
	}

	// Unarmed, he goes for the nearest sword lying on the floor and picks it up; with none to be had, he draws another.
	PawnInput Fetch(PawnInput input)
	{
		if (fetching == null || Game.destroying.Contains(fetching))
		{
			fetching = null;
			var best = float.MaxValue;
			foreach (var actor in Game.Actors)
				if (actor is ThrownBlade { Flying: false } blade && !Game.destroying.Contains(blade) &&
					Vector2.DistanceSquared(blade.Position, Position) is var distance && distance < best)
					(fetching, best) = (blade, distance);
			if (fetching == null)
			{
				Arm();
				return input;
			}
		}
		if (fetching.Hitbox.Overlaps(Hitbox.Rect + Position - fetching.Position))
		{
			Game.Destroy(fetching);
			fetching = null;
			Arm();
			Squish = new(0.85f, 1.15f);
		}
		return input;
	}

	PawnInput Rest(float time)
	{
		if (modeTime >= time)
		{
			ambushCooldown = MathF.Max(ambushCooldown, mode == Mode.Recover ? AmbushCooldown : 0);
			Begin(Mode.Engage);
		}
		return default;
	}

	protected override void AfterMovementUpdate(Vector2 previousVelocity)
	{
		if (plunging)
			PlayPawnAnimation("attack_down", looping: false);
	}

	protected override void OnLanded(Vector2 previousVelocity)
	{
		base.OnLanded(previousVelocity);
		if (plunging)
			SmashLand();
	}

	// The blade drives into the floor, which quakes out from it; he's left wrenching it free.
	void SmashLand()
	{
		Begin(Mode.Recover);
		Game.Shake(0.2f, 2);
		Game.HitStun(0.05f);
		Squish = new(1.35f, 0.7f);
		var floor = Position + new Point2(0, 1);
		var wave = Game.Create<RadialWave>(floor);
		wave.Source = this;
		wave.Target = Game.GetFirst<Player>();
		wave.CanHit = static (wave, target) => target is Player p && p.Grounded();
		wave.Medium = RadialWaveMedium.Solid;
		wave.Speed = 140;
		wave.MaxRadius = SmashWaveRange;
		wave.Thickness = 4;
		wave.Color = SmashWaveColor;
		wave.CoreColor = Color.Lerp(SmashWaveColor, Color.White, 0.55f);
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (health <= 0)
			return;
		health--;
		var away = Math.Sign(Position.X - hitInfo.By.Position.X);
		RigVisual?.Hit(away, animateUpperBody: true);
		Game.Create<Pop>(Position + new Point2(0, -8));
		if (health <= 0)
		{
			Die();
			return;
		}
		Game.HitStun(0.08f);
		if (plunging)
			return;
		Begin(Mode.Hurt);
		PreviewRigTake(FlinchTake);
		Velocity = new(away * HurtKnockback, -40);
	}

	void Die()
	{
		Begin(Mode.Dying);
		Mask = Masks.None;
		PreviewRigTake(DeathTake);
		Game.HitStun(0.2f);
		Game.Shake(0.15f);
		Game.KillForThisSession(this);
	}

	// Falls, lies still a moment, then he's gone.
	PawnInput Dying()
	{
		if (RigPreview != null || (lyingStill += Time.Delta) < DeathLinger)
			return default;
		for (var x = -1; x <= 1; x++)
			Game.Create<Pop>(Position + new Point2(x * 8, -4 + Math.Abs(x) * 2));
		Game.Destroy(this);
		return default;
	}

	bool Sees(Player player)
	{
		var eye = Position + new Point2(0, -10);
		var target = player.Position + new Point2(0, -6);
		if (Vector2.DistanceSquared(eye, target) > SightRange * SightRange)
			return false;
		var steps = (int)MathF.Ceiling(Vector2.Distance(eye, target) / 2);
		for (var i = 1; i < steps; i++)
			if (Game.OverlapsRoomCollision(new RectInt(Vector2.Lerp(eye, target, i / (float)steps).FloorToPoint2(), Point2.One)))
				return false;
		return true;
	}

	public override void Render(Batcher batcher)
	{
		if (!BurrowHidesSprite)
			RenderBody(batcher, Color.White);
		RenderBurrowLine(batcher);
	}
}
