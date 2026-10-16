using System.Numerics;
using Foster.Framework;

namespace Myth;

// Rig draw mode: a HumanoidRigVisual stands in for the sprite. The sprite state machine keeps running unseen --
// gameplay reads its names and durations -- and PlayPawnAnimation mirrors each sprite animation onto a rig clip.
public abstract partial class Pawn
{
	// Player and Bud expose rig settings; other pawns (Stalker) bring their own rig, so theirs would collide.
	protected virtual HumanoidRigSettings? DrawRig => null;

	HumanoidRigVisual? rigVisual;
	string rigClip = "PlayerIdle";
	string rigRequested = "";
	// A take bridging two clips -- jump start, landing -- played to its end before the requested clip resumes.
	string? rigLead;
	string rigPlaying = "";
	RigTake? rigTake; // null when rigPlaying is a raw clip
	string? rigPreview; // forced by the rig animation viewer; a one-shot hands back when it finishes
	float rigDiveAngle;
	float rigDiveWeight;
	float rigDiveJumpGrace;
	bool rigDiveClip;
	bool rigRestart;
	// The skid clip's own turn is baked out (StripYaw); its yaw is swept here along the clip instead, so the turn
	// reads with the footwork rather than snapping when Facing flips.
	Signs rigSkidFacing;
	float rigSkidFromYaw;
	float rigSkidToYaw;
	float rigSkidYawWeight;
	// A dive that ends in a burrow keeps its momentum into the surface, cut off where it meets it (PushRigSinkClip).
	bool rigSinking;
	Vector2 rigSinkVelocity;
	bool rigSinkCutMaterial; // what PushRigSinkClip pushed: the terrain-cut material, else a scissor
	// Left alone, the idle breaks into a one-shot fidget now and then; any input hands straight back.
	static readonly string[] RigFidgets =
		["PlayerFidgetHolding", "PlayerFidgetOffensive", "PlayerFidgetWarrior", "PlayerFidgetDwarf", "PlayerFidgetHappy"];
	static float RigFidgetFirstDelay => 5f;
	static float RigFidgetMinGap => 4f;
	static float RigFidgetMaxGap => 9f;
	float rigFidgetTimer = RigFidgetFirstDelay;
	int rigFidgetLast = -1;
	string? rigFidget;
	// Starting a run leans into it, peaking after RigRunLeanPeak seconds, then settles back over the next half second
	// or so to the take's own lean (PlayerRun sits more upright than the clip).
	static float RigRunLeanIn => 39f; // degrees
	static float RigRunLeanPeak => 0.19f;
	float rigRunLeanTime = 10;
	bool rigWasRunning;

	// Locomotion clips play at this rate at full ground speed, slowing with the pawn.
	static float RigGaitRate => 2.2f;
	static float RigGaitMinRate => 0.5f;
	static float RigSprintSpeed => 1.15f; // × MaxGroundSpeed
	static float RigSprintRate => 0.7f;
	static float RigRollMinRate => 0.5f;
	static float RigLandingScale => 0.5f;
	static float RigSkidYawRelease => 0.15f;
	static float RigRopeHandResponse => 20f;
	static float RigRopeTilt => 0.6f; // × the rope's angle off vertical at the grip
	static float RigRopeTiltResponse => 12f;
	static float RigCrawlLift => 2f; // prone, the torso's drawn thickness sinks into the ground
	static float RigCrawlSpeedSlack => 1.15f; // × CrawlSpeed: faster is still the slide out of a run
	// Diving, the outstretched arms point along the arc: ahead at the peak, down falling at this speed.
	// The figure is rotated to aim them, whatever angle the dive take was authored at.
	static float RigDiveFullAngleSpeed => 130f;
	static float RigDiveAngleResponse => 14f;
	static float RigDiveInResponse => 10f; // how fast the figure turns to aim the dive; the angle keeps tracking the arc
	static float RigDiveBlendDuration => 0.3f;
	// Landing out of a dive (no burrow) snaps upright instead of easing out at the dive's gentle in-rate.
	static float RigDiveLandResponse => 40f;
	static float RigDiveLandBlendDuration => 0.08f;
	static float RigDiveReleaseGrace => 0.1f; // a jump release shorter than this doesn't count against the dive
	static float RigDiveClipOut => 0.3f; // the dive clip lets go once the dive's over and its weight is below this
	static float RigRopeGripHeight => 1.55f; // meters above the feet
	static float RigHeadLookMaxTurn => 0.6f;
	static float RigHeadLookRate => 8f;
	static Vector2 RigHatOffset => new(0, 1);
	static float RigSinkMinSpeed => 160f;
	static int RigDuckLookAhead => 8; // pixels, a tile: the duck starts before the ceiling arrives
	static float RigCrouchClearance => 0f; // pixels above the feet; lower ceilings switch to the crouch takes
	static float RigCrouchGaitRate => 1.5f; // × the gait rate
	static float RigAirLegsResponse => 25f;
	static bool RigAirAttackMirrored => true; // swinging in the air, the far hand swings instead
	// Up and down swipes in the air reach that way: the torso bends a little, the swing arm turns the rest, over these
	// legs (take names).
	static float RigAttackTorsoDegrees => 20f;
	static float RigAttackUpArmDegrees => 60f;
	static float RigAttackDownArmDegrees => 90f;
	static float RigAttackArcScale => 1.6f; // × the swing arm's own sweep
	static float RigAttackPitchResponse => 20f;
	static string RigAttackDownLegs => "PlayerAir";
	static string RigAttackUpLegs => "PlayerAir";
	int rigAttackPitch; // 1 down, -1 up
	float rigAttackReach; // eased: 1 down, -1 up

	// Where the eyes and head aim; null looks ahead.
	protected virtual Vector2? RigLookTarget => null;
	protected virtual void OnRigFootstep(Vector2 at) { }

	protected bool RigDrawing => rigVisual != null;

	// Where the pawn holds a rope, local to it: the sprite hangs by its chest, the rig by its raised hands.
	protected Point2 RopeGrip => rigVisual == null ? Hitbox.Rect.Center
		: new(Hitbox.Rect.Center.X, -(int)MathF.Round(RigRopeGripHeight * rigVisual.Settings.PixelsPerMeter));

	// Names a saved take (RigTakes, editable in the rig animation viewer) or a raw clip.
	static string? RigClipFor(string animation) => animation switch
	{
		"skid" => "PlayerSkid",
		"idle" or "look_up" => "PlayerIdle",
		"climb" or "climb_idle" or "climb_down" or "climb_fall" => "PlayerClimb",
		"wall_slide" => "PlayerWallSlide",
		"walk" => "Walk_Loop",
		"run" or "run_start" => "PlayerRun",
		"duck" or "burrow" => "Crouch_Idle_Loop",
		"crawl" => "PlayerCrawl",
		"roll" => "PlayerRoll",
		"back_dash" => "Roll",
		"hurt" => "Hit_Knockback",
		"sword" => "Sword_Regular_A",
		_ when animation.StartsWith("jump") || animation is "dive" => "PlayerAir",
		_ when animation.Contains("attack") => "PlayerAttack",
		_ => null,
	};

	static string? RigLeadFor(string from, string to, float velocityY) => (from, to) switch
	{
		(not "PlayerAir", "PlayerAir") when velocityY < 0 => "PlayerJumpStart",
		("PlayerAir" or "PlayerDive", "PlayerIdle") => "PlayerJumpLand",
		("PlayerCrawl", "Crouch_Idle_Loop") => "PlayerCrawlToCrouch",
		("Crouch_Idle_Loop", "PlayerCrawl") => "PlayerCrouchToCrawl",
		("Crouch_Idle_Loop", "PlayerIdle") => "PlayerCrouchToStand",
		(not "PlayerRoll", "PlayerRoll") => "PlayerRollStart",
		_ => null,
	};

	protected void PlayPawnAnimation(string name, bool looping = true, bool restart = false)
	{
		Play(name, looping, restart);
		if (RigClipFor(name) is not { } clip)
			return;
		rigRestart |= restart;
		rigClip = clip;
		rigAttackPitch = name switch { "attack_down" => 1, "attack_up" => -1, _ => 0 };
	}

	void UpdateRig()
	{
		var settings = DrawRig;
		if (settings != rigVisual?.Settings)
		{
			rigVisual?.Dispose();
			rigVisual = null;
			if (settings != null)
			{
				settings.TurnWhileMoving = true;
				settings.GaitFeetFollowTerrain = true;
				rigVisual = new(this, settings) { Footstep = OnRigFootstep };
				rigVisual.Initialize(rigClip);
				rigPlaying = "";
			}
		}
		if (rigVisual == null)
			return;

		var speed = MathF.Abs(Velocity.X);
		// Holding jump past the peak with a burrow available: the dive toward the ground. The dive clip comes in with the
		// intent, before the turn has built up: the roll aims the arms the pose holds, so swapping poses under a
		// half-built turn would swing the body backwards. It lets go only once the weight has fallen, so tapping jump
		// shows a partial dive instead of flickering between clips. Sinking, it holds its pose, arms frozen.
		// A burrow ends the dive: once under, and on a pop that cuts the sink short, none of it follows him back out.
		if (State == States.Burrow ? !rigSinking : rigSinking)
		{
			rigSinking = rigDiveClip = false;
			rigDiveWeight = 0;
			rigVisual.Offset = Vector2.Zero;
		}
		rigDiveJumpGrace = input.JumpDown ? RigDiveReleaseGrace : MathF.Max(0, rigDiveJumpGrace - Time.Delta);
		var airborne = rigClip == "PlayerAir" && State == States.Normal;
		var diving = rigSinking || airborne && Velocity.Y >= 0 && rigDiveJumpGrace > 0 && CanBurrow();
		var diveBlend = 1 - MathF.Exp(-RigDiveAngleResponse * Time.Delta);
		var diveIn = 1 - MathF.Exp(-RigDiveInResponse * Time.Delta);
		if (diving && !rigSinking)
		{
			var diveAngle = Math.Clamp(Velocity.Y / RigDiveFullAngleSpeed, -1, 1) * MathF.PI / 2;
			rigDiveAngle = rigDiveWeight < 0.01f ? diveAngle : float.Lerp(rigDiveAngle, diveAngle, diveBlend);
		}
		rigDiveWeight = float.Lerp(rigDiveWeight, diving ? 1 : 0,
			grounded && !rigSinking ? 1 - MathF.Exp(-RigDiveLandResponse * Time.Delta) : diveIn);
		rigDiveClip = airborne && (diving || rigDiveClip && rigDiveWeight > RigDiveClipOut);
		var clip = rigSinking || rigDiveClip ? "PlayerDive" :
			RigPushing() ? "PlayerPush" :
			// Ducking out of a run slides at run speed, which the sprite plays as "crawl". The rig crouches through
			// the slide and only goes prone once it's really crawling.
			rigClip == "PlayerCrawl" && (input.MoveX == 0 || speed > Player.CrawlSpeed * RigCrawlSpeedSlack) ? "Crouch_Idle_Loop" :
			// Dash-running gets the full run, and the sprint once it's really moving.
			rigClip is "PlayerRun" && IsDashRunning() ? speed > MaxGroundSpeed * RigSprintSpeed ? "Sprint_Loop" : "Jog_Fwd_Loop" :
			rigClip;
		rigVisual.CeilingClearance = RigCeilingClearance();
		if (rigVisual.CeilingClearance < RigCrouchClearance)
			clip = clip switch
			{
				"PlayerIdle" => "Crouch_Idle_Loop",
				"Walk_Loop" or "PlayerRun" or "Jog_Fwd_Loop" or "Sprint_Loop" => "Crouch_Fwd_Loop",
				_ => clip,
			};
		clip = RigFidget(clip);
		rigVisual.ArmAim = new((int)Facing * MathF.Cos(rigDiveAngle), MathF.Sin(rigDiveAngle));
		rigVisual.ArmAimWeight = rigDiveWeight;
		if (clip != rigRequested)
		{
			// Once the sim has turned around, the skid finishes before the run resumes.
			var skidHolds = rigPlaying == "PlayerSkid" && Facing != rigSkidFacing &&
				clip is "PlayerIdle" or "Walk_Loop" or "PlayerRun" or "Jog_Fwd_Loop" or "Sprint_Loop";
			rigLead = skidHolds ? "PlayerSkid" :
				RigLeadFor(rigRequested, clip, Velocity.Y) is { } lead && RigTakes.TryGet(lead, out var leadTake) &&
				rigVisual.TryGetClip(leadTake.Clip, out _) ? lead : null; // a take whose clip isn't baked yet is skipped
			rigRequested = clip;
		}
		if (rigLead != null && rigPlaying == rigLead && rigVisual.AnimationFinished)
			rigLead = null;
		if (rigPreview != null && rigPlaying == rigPreview && rigVisual.AnimationFinished)
			rigPreview = null;
		var shown = rigPreview ?? rigLead ?? clip;
		if (shown != rigPlaying || rigRestart)
			PlayRig(shown, rigRestart);
		// Picks up edits from the rig animation viewer mid-take; a no-op unless the take changed.
		else if (rigTake is { } playingTake && RigTakes.TryGet(rigPlaying, out var liveTake) && liveTake != playingTake)
		{
			rigVisual.Play(liveTake, restart: false);
			rigTake = liveTake;
		}
		rigRestart = false;

		var running = shown is "PlayerRun" or "Jog_Fwd_Loop" or "Sprint_Loop";
		if (running && !rigWasRunning)
			rigRunLeanTime = 0;
		rigWasRunning = running;
		rigRunLeanTime += Time.Delta;
		var leanT = rigRunLeanTime / RigRunLeanPeak;
		rigVisual.ExtraLeanDegrees = RigRunLeanIn * leanT * MathF.Exp(1 - leanT);

		var gait = shown is "Walk_Loop" or "PlayerRun" or "Jog_Fwd_Loop" or "Sprint_Loop" or "PlayerCrawl" or "Crouch_Fwd_Loop";
		rigVisual.GaitActive = gait;
		rigVisual.PlaybackSpeed = (rigTake?.Speed ?? 1) * (
			gait ? MathF.Max(RigGaitMinRate, speed / MaxGroundSpeed * RigGaitRate *
				(shown == "Sprint_Loop" ? RigSprintRate : shown == "Crouch_Fwd_Loop" ? RigCrouchGaitRate : 1)) :
			shown == "PlayerRoll" ? MathF.Max(RigRollMinRate, speed / MaxGroundSpeed) :
			// Pulls only while climbing up; hanging and sliding down hold the grip.
			shown == "PlayerClimb" ? (State == States.Climb && input.MoveY < 0 ? ScriptedClimbSpeed : 0) : 1);
		UpdateRigOffset(shown);
		rigVisual.GripRoll = float.Lerp(rigVisual.GripRoll, RopeSwingAngle() * RigRopeTilt, 1 - MathF.Exp(-RigRopeTiltResponse * Time.Delta));
		UpdateRigSkidYaw();
		UpdateRigAirLegs(shown);
		rigVisual.LookTarget = rigVisual.HeadLookTarget = RigLookTarget;
		rigVisual.HeadLookMaxTurn = RigHeadLookMaxTurn;
		rigVisual.HeadLookRate = RigHeadLookRate;
		rigVisual.Update(grounded && !rigSinking, footIK: grounded && !rigSinking, loop: rigTake?.Loop ?? rigPlaying.EndsWith("_Loop"));
	}

	// Pixels above the feet to the lowest ceiling over the body and a tile toward where it's heading; null when
	// nothing is within reach of a standing rig's head.
	float? RigCeilingClearance()
	{
		if (!grounded || State != States.Normal)
			return null;
		var rect = Hitbox.Rect;
		var ahead = input.MoveX != 0 ? Math.Sign(input.MoveX) : Math.Sign(Velocity.X);
		var reach = (int)MathF.Ceiling(2 * rigVisual!.Settings.PixelsPerMeter);
		var left = Position.X + rect.Left + Math.Min(0, ahead * RigDuckLookAhead);
		var width = rect.Width + Math.Abs(ahead) * RigDuckLookAhead;
		if (!Game.OverlapsSolid(this, new RectInt(left, Position.Y - reach, width, reach + rect.Top)))
			return null;
		int? lowest = null;
		for (var x = left; x < left + width; x++)
		{
			// Solid at body height is a wall or step to stop at, not a ceiling to duck under.
			if (Game.OverlapsSolid(this, new RectInt(x, Position.Y + rect.Top, 1, -rect.Top)))
				continue;
			for (var height = -rect.Top; height < (lowest ?? reach); height++)
				if (Game.OverlapsSolid(this, new RectInt(x, Position.Y - height - 1, 1, 1)))
				{
					lowest = height;
					break;
				}
		}
		return lowest;
	}

	// How far the leading hand is short of the wall face (negative: through it).
	float RigPushReach(HumanoidRigVisual visual)
	{
		var wall = Position.X + (Facing > 0 ? Hitbox.Rect.Right : Hitbox.Rect.Left);
		var left = visual.LeftHandWorldPosition.X;
		var right = visual.RightHandWorldPosition.X;
		return Facing > 0 ? wall - MathF.Max(left, right) : wall - MathF.Min(left, right);
	}

	// Pressing into a solid flush against us at head height, not just a step at the feet or a low ceiling ahead.
	bool RigPushing()
	{
		if (!grounded || State != States.Normal || input.MoveX == 0 || Math.Sign(input.MoveX) != (int)Facing ||
			rigClip is not ("PlayerIdle" or "Walk_Loop" or "PlayerRun"))
			return false;
		var rect = Hitbox.Rect;
		return Game.OverlapsSolid(this, new RectInt(Position.X + (Facing > 0 ? rect.Right : rect.Left - 1), Position.Y + rect.Top, 1, 3));
	}

	// Climbing, slides the rig sideways so its hands, not its chest, are on the rope; pushing, so its leading hand
	// is on the wall rather than through it; crawling, lifts it.
	void UpdateRigOffset(string shown)
	{
		var visual = rigVisual!;
		if (rigSinking)
		{
			visual.Offset += rigSinkVelocity * Time.Delta;
			// Held through the burrow's entry window even once buried (the clip hides it all): BurrowHidesSprite
			// waits on that window, and an early end would draw the pose, unclipped, on the surface.
			rigSinking = State == States.Burrow && (burrowEnterTimer > 0 || !RigSubmerged(visual.GetVisualBounds()));
			if (rigSinking)
				Game.View?.RequestTerrainCut();
			else
				visual.Offset = Vector2.Zero; // hidden by now, or popped straight back out
			return;
		}
		var target = new Vector2(
			State == States.Climb ? visual.Offset.X + Position.X + Hitbox.Rect.CenterF.X - visual.LeftHandWorldPosition.X :
			shown == "PlayerPush" ? visual.Offset.X + RigPushReach(visual) : 0,
			shown == "PlayerCrawl" ? -RigCrawlLift : 0);
		visual.Offset = Vector2.Lerp(visual.Offset, target, 1 - MathF.Exp(-RigRopeHandResponse * Time.Delta));
	}

	// Once a dive's burrow has attached: the body flies on into the surface instead of squashing onto it.
	// `from` is where it was drawn before the burrow snapped the pawn onto the surface.
	protected void BeginRigSink(Point2 from, Vector2 velocity)
	{
		if (rigVisual == null || rigRequested != "PlayerDive")
			return;
		rigSinking = true;
		rigSinkVelocity = (velocity.Normalized(burrow.Toward) + burrow.Toward).Normalized(burrow.Toward) *
			MathF.Max(velocity.Length(), RigSinkMinSpeed);
		rigVisual.Offset += from - Position;
	}

	// Every drawn pixel is past the burrow surface, so the burrow can take over hiding the body.
	bool RigSubmerged(Rect worldBounds)
	{
		var surface = (Vector2)Position;
		var toward = burrow.Toward;
		return Vector2.Dot(worldBounds.TopLeft - surface, toward) > 0 && Vector2.Dot(worldBounds.TopRight - surface, toward) > 0 &&
			Vector2.Dot(worldBounds.BottomLeft - surface, toward) > 0 && Vector2.Dot(worldBounds.BottomRight - surface, toward) > 0;
	}

	// Sinking, cuts away the body wherever terrain is, so it dives into slopes too. A jumpthru, and passes other than
	// the world draw, scissor away everything past the burrow surface instead -- a real cut, so a thin jumpthru doesn't
	// show the body below it; surfaces are cut along their dominant axis. Call PopRigSinkClip when this returns true.
	protected bool PushRigSinkClip(Batcher batcher)
	{
		if (!rigSinking)
			return false;
		rigSinkCutMaterial = !burrowOnJumpthru && Game.View?.PushTerrainCutMaterial() == true;
		if (rigSinkCutMaterial)
		{
			rigVisual!.SkipSurfaces = true;
			return true;
		}
		const float Far = 100000;
		var toward = burrow.Toward;
		var axis = MathF.Abs(toward.X) > MathF.Abs(toward.Y) ? new Vector2(MathF.Sign(toward.X), 0) : new Vector2(0, MathF.Sign(toward.Y));
		var surface = (Vector2)(Position - RenderPosition); // drawing is local to RenderPosition
		var across = axis.TurnLeft() * Far;
		var open = (RectInt)Rect.Between(Vector2.Transform(surface + across, batcher.Matrix),
			Vector2.Transform(surface - across - axis * Far, batcher.Matrix));
		batcher.PushScissor(batcher.Scissor is { } outer ? outer.GetIntersection(open) : open);
		return true;
	}

	protected void PopRigSinkClip(Batcher batcher)
	{
		if (rigSinkCutMaterial)
		{
			batcher.PopMaterial();
			rigVisual!.SkipSurfaces = false;
		}
		else
			batcher.PopScissor();
	}

	void DrawRigClipped(Batcher batcher, Color color, bool silhouette)
	{
		if (BurrowHidesSprite)
			return;
		var clipped = PushRigSinkClip(batcher);
		rigVisual!.Draw(batcher, color, silhouette);
		if (clipped)
			PopRigSinkClip(batcher);
	}

	// Swinging in the air, the legs keep the air pose and up and down swipes reach; landing mid-swing hands both back to
	// the swing's stance.
	void UpdateRigAirLegs(string shown)
	{
		var visual = rigVisual!;
		var airSwing = shown == "PlayerAttack" && !grounded;
		visual.LegWeight = float.Lerp(visual.LegWeight, airSwing ? 1 : 0, 1 - MathF.Exp(-RigAirLegsResponse * Time.Delta));
		if (visual.LegWeight < 0.01f)
			visual.LegWeight = 0;
		var legs = rigAttackPitch > 0 ? RigAttackDownLegs : rigAttackPitch < 0 ? RigAttackUpLegs : "PlayerAir";
		visual.LegTake = RigTakes.TryGet(legs, out var legTake) ? legTake : null;
		rigAttackReach = float.Lerp(rigAttackReach, airSwing ? rigAttackPitch : 0, 1 - MathF.Exp(-RigAttackPitchResponse * Time.Delta));
		visual.TorsoRoll = rigAttackReach * (int)Facing * RigAttackTorsoDegrees * MathF.PI / 180;
		// The down swipe is the up swipe flipped top to bottom: up and ahead, then down and back.
		visual.ArmReachWeight = airSwing ? MathF.Abs(rigAttackReach) : 0;
		visual.ArmReachRoll = -(rigAttackReach > 0 ? RigAttackDownArmDegrees : RigAttackUpArmDegrees) * MathF.PI / 180;
		visual.ArmArcScale = RigAttackArcScale;
		visual.ArmReachFlip = rigAttackReach > 0;
	}

	void UpdateRigSkidYaw()
	{
		var visual = rigVisual!;
		if (rigPlaying == "PlayerSkid")
		{
			var progress = Math.Clamp((visual.Time - visual.RangeStart) / MathF.Max(0.0001f, visual.RangeEnd - visual.RangeStart), 0, 1);
			visual.YawOverride = float.Lerp(rigSkidFromYaw, rigSkidToYaw, progress * progress * (3 - 2 * progress));
			rigSkidYawWeight = 1;
		}
		else
			rigSkidYawWeight = Calc.Approach(rigSkidYawWeight, 0, Time.Delta / RigSkidYawRelease);
		visual.YawOverrideWeight = rigSkidYawWeight;
		if (rigSkidYawWeight <= 0)
			visual.YawOverride = null;
	}

	string RigFidget(string clip)
	{
		if (clip != "PlayerIdle" || input.MoveX != 0 || input.MoveY != 0 || input.JumpDown || input.DashDown || input.AttackPressed)
		{
			rigFidget = null;
			rigFidgetTimer = RigFidgetFirstDelay;
			return clip;
		}
		if (rigFidget != null && rigPlaying == rigFidget && rigVisual!.AnimationFinished)
		{
			rigFidget = null;
			rigFidgetTimer = rng.Float(RigFidgetMinGap, RigFidgetMaxGap);
		}
		if (rigFidget == null && (rigFidgetTimer -= Time.Delta) <= 0)
		{
			// Never the same one twice running.
			rigFidgetLast = rigFidgetLast < 0 ? rng.Int(RigFidgets.Length)
				: (rigFidgetLast + 1 + rng.Int(RigFidgets.Length - 1)) % RigFidgets.Length;
			rigFidget = RigFidgets[rigFidgetLast];
		}
		return rigFidget ?? clip;
	}

	void PlayRig(string name, bool restart)
	{
		if (name == "PlayerSkid" && rigPlaying != name)
		{
			// Turning toward the stick: Facing has already flipped for a skid out of a standing turn, not for a brake.
			var toward = input.MoveX != 0 ? (Signs)input.MoveX : -Facing;
			rigSkidFacing = -toward;
			rigSkidFromYaw = rigVisual!.FacingYaw(-toward);
			rigSkidToYaw = rigVisual.FacingYaw(toward);
		}
		var blend = name == "PlayerDive" ? RigDiveBlendDuration
			: rigPlaying == "PlayerDive" && grounded ? RigDiveLandBlendDuration : -1;
		rigTake = RigTakes.TryGet(name, out var take) ? take : null;
		if (rigTake != null)
			rigVisual!.Play(name == "PlayerAttack" && !grounded && RigAirAttackMirrored
				? take with { NearHand = take.NearHand switch { RigHand.Left => RigHand.Right, RigHand.Right => RigHand.Left, _ => RigHand.None } }
				: take, blend);
		else
			rigVisual!.Play(name, restart: restart, blendDuration: blend);
		rigPlaying = name;
		// A swing out of the air starts on the air legs it already had; a down swipe starts reaching up for the hat.
		if (name == "PlayerAttack")
		{
			rigVisual.LegWeight = grounded ? 0 : 1;
			if (!grounded && rigAttackPitch > 0)
				rigAttackReach = 1;
		}
	}

	internal HumanoidRigVisual? RigVisual => rigVisual;
	internal string RigPlaying => rigPlaying;
	internal string? RigPreview => rigPreview;

	internal void PreviewRigTake(string? take)
	{
		rigPreview = take;
		rigRestart |= take != null;
	}

	// Attacking, the hat swipes in the sword hand: brim in the palm, crown out along torso -> hand. Local to the pawn,
	// with the hat's rotation; null when drawing the sprite or not attacking.
	protected (Vector2 Position, float Rotation)? RigHatInHand
	{
		get
		{
			if (rigVisual == null || rigPlaying != "PlayerAttack")
				return null;
			var palm = rigVisual.RightPalm;
			var outward = (palm - rigVisual.TorsoCenter).Normalized(-Vector2.UnitY);
			return (palm, MathF.Atan2(outward.X, -outward.Y));
		}
	}

	// A new action restarts its take even when the same one is already playing (back-to-back attacks).
	protected void RestartRigTake() => rigRestart = true;

	// Where a hat sits on the rig's head, local to the pawn; null when drawing the sprite.
	protected Vector2? RigHatPosition => rigVisual == null ? null
		: rigVisual.HeadPosition - Vector2.UnitY * rigVisual.HeadRadius + RigHatOffset;
	protected float RigHeadRoll => rigVisual == null ? 0 : rigVisual.HeadRoll + rigVisual.PoseRoll;

	protected void RenderBody(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			RenderSpriteSubtexture(batcher, color);
		else if (!SpriteBlinkHidden)
			DrawRigClipped(batcher, color, silhouette: false);
	}

	public override void Render(Batcher batcher) => RenderBody(batcher, Color.White);

	public override void RenderNormals(Batcher batcher)
	{
		if (rigVisual == null)
			base.RenderNormals(batcher);
		else if (!BurrowHidesSprite)
		{
			var clipped = PushRigSinkClip(batcher);
			rigVisual.DrawNormals(batcher);
			if (clipped)
				PopRigSinkClip(batcher);
		}
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			base.RenderShadow(batcher, color);
		else
			DrawRigClipped(batcher, color, silhouette: true);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			base.RenderReflection(batcher, color);
		else
			DrawRigClipped(batcher, color, silhouette: true);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (rigVisual == null)
			base.RenderEmissive(batcher);
		else
			rigVisual.DrawEmissive(batcher);
	}

	public override Rect GetVisualBounds() => rigVisual?.GetVisualBounds() ?? base.GetVisualBounds();
}
