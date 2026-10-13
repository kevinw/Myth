using System.Numerics;
using Foster.Framework;

namespace Myth;

// Rig draw mode: a HumanoidRigVisual stands in for the sprite. The sprite state machine keeps running unseen --
// gameplay reads its names and durations -- and PlayPawnAnimation mirrors each sprite animation onto a rig clip.
public abstract partial class Pawn
{
	// Player and Bud expose rig settings; other pawns (Stalker) bring their own rig, so theirs would collide.
	protected virtual HumanoidRigSettings? DrawRig => null;
	protected static HumanoidRigSettings DefaultRig() => new()
	{
		PixelsPerMeter = 12f,
		VisualFPS = 240,
		HeadSize = 1.6f,
		EyeSize = 0.5f,
		TorsoSize = 2,
		BicepSize = 1,
		ForearmSize = 1,
		ThighSize = 1.5f,
		TurnInertia = 0.3f,
		RagdollCarry = 1,
		TurnDuration = 0.12f,
		VelocityLean = 0.6f,
		SquishResponse = 0.6f,
		SkinMode = RigSkinMode.Solid,
		SecondaryMotion = 0.19f,
		NormalRoundness = 0.5f,
		CostumeId = "brass-wanderer",
		OutlineColor = Color.Black * 0.4f,
	};

	HumanoidRigVisual? rigVisual;
	string rigClip = "Idle_Loop";
	string rigRequested = "";
	// A take bridging two clips -- jump start, landing -- played to its end before the requested clip resumes.
	string? rigLead;
	string rigPlaying = "";
	RigTake? rigTake; // null when rigPlaying is a raw clip
	string? rigPreview; // forced by the rig animation viewer; a one-shot hands back when it finishes
	float rigDiveAngle;
	float rigDiveWeight;
	bool rigRestart;
	// The skid clip's own turn is baked out (StripYaw); its yaw is swept here along the clip instead, so the turn
	// reads with the footwork rather than snapping when Facing flips.
	Signs rigSkidFacing;
	float rigSkidFromYaw;
	float rigSkidToYaw;
	float rigSkidYawWeight;

	// Locomotion clips play at this rate at full ground speed, slowing with the pawn.
	static float RigGaitRate => 2.2f;
	static float RigGaitMinRate => 0.5f;
	static float RigSprintSpeed => 1.15f; // × MaxGroundSpeed
	static float RigSprintRate => 0.7f;
	static float RigRollMinRate => 0.5f;
	static float RigLandingScale => 0.5f;
	static float RigSkidYawRelease => 0.15f;
	static float RigRopeHandResponse => 20f;
	static float RigCrawlLift => 2f; // prone, the torso's drawn thickness sinks into the ground
	// Diving, the outstretched arms point along the arc: ahead at the peak, down falling at this speed.
	// The figure is rotated to aim them, whatever angle the dive take was authored at.
	static float RigDiveFullAngleSpeed => 130f;
	static float RigDiveAngleResponse => 20f;
	static float RigRopeGripHeight => 1.55f; // meters above the feet
	static float RigHeadLookMaxTurn => 0.6f;
	static float RigHeadLookRate => 8f;
	static Vector2 RigHatOffset => new(0, 1);

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
		"idle" or "look_up" => "Idle_Loop",
		"climb" or "climb_idle" or "climb_down" or "climb_fall" => "PlayerClimb",
		"wall_slide" => "Idle_Rail_Loop",
		"walk" => "Walk_Loop",
		"run" or "run_start" => "Jog_Fwd_Loop",
		"duck" or "burrow" => "Crouch_Idle_Loop",
		"crawl" => "PlayerCrawl",
		"roll" => "PlayerRoll",
		"back_dash" => "Roll",
		"hurt" => "Hit_Knockback",
		"sword" => "Sword_Regular_A",
		_ when animation.StartsWith("jump") || animation is "dive" => "PlayerAir",
		_ when animation.Contains("attack") => "Sword_Regular_A",
		_ => null,
	};

	static string? RigLeadFor(string from, string to, float velocityY) => (from, to) switch
	{
		(not "PlayerAir", "PlayerAir") when velocityY < 0 => "PlayerJumpStart",
		("PlayerAir", "Idle_Loop") => "PlayerJumpLand",
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
		// Holding jump past the peak with a burrow available: the dive toward the ground.
		var diving = rigClip == "PlayerAir" && State == States.Normal && Velocity.Y >= 0 && input.JumpDown && CanBurrow();
		var clip = diving ? "PlayerDive" :
			rigClip is "Jog_Fwd_Loop" && speed > MaxGroundSpeed * RigSprintSpeed ? "Sprint_Loop" : rigClip;
		var diveBlend = 1 - MathF.Exp(-RigDiveAngleResponse * Time.Delta);
		if (diving)
		{
			var diveAngle = Math.Clamp(Velocity.Y / RigDiveFullAngleSpeed, -1, 1) * MathF.PI / 2;
			rigDiveAngle = rigDiveWeight < 0.01f ? diveAngle : float.Lerp(rigDiveAngle, diveAngle, diveBlend);
		}
		rigDiveWeight = float.Lerp(rigDiveWeight, diving ? 1 : 0, diveBlend);
		rigVisual.ArmAim = new((int)Facing * MathF.Cos(rigDiveAngle), MathF.Sin(rigDiveAngle));
		rigVisual.ArmAimWeight = rigDiveWeight;
		if (clip != rigRequested)
		{
			// Once the sim has turned around, the skid finishes before the run resumes.
			var skidHolds = rigPlaying == "PlayerSkid" && Facing != rigSkidFacing &&
				clip is "Idle_Loop" or "Walk_Loop" or "Jog_Fwd_Loop" or "Sprint_Loop";
			rigLead = skidHolds ? "PlayerSkid" :
				RigLeadFor(rigRequested, clip, Velocity.Y) is { } lead && RigTakes.TryGet(lead, out _) ? lead : null;
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

		var gait = shown is "Walk_Loop" or "Jog_Fwd_Loop" or "Sprint_Loop" or "PlayerCrawl";
		rigVisual.GaitActive = gait;
		rigVisual.PlaybackSpeed = (rigTake?.Speed ?? 1) * (
			gait ? MathF.Max(RigGaitMinRate, speed / MaxGroundSpeed * RigGaitRate * (shown == "Sprint_Loop" ? RigSprintRate : 1)) :
			shown == "PlayerRoll" ? MathF.Max(RigRollMinRate, speed / MaxGroundSpeed) :
			// Pulls only while climbing up; hanging and sliding down hold the grip.
			shown == "PlayerClimb" ? (State == States.Climb && input.MoveY < 0 ? ScriptedClimbSpeed : 0) : 1);
		UpdateRigOffset(shown);
		UpdateRigSkidYaw();
		rigVisual.LookTarget = rigVisual.HeadLookTarget = RigLookTarget;
		rigVisual.HeadLookMaxTurn = RigHeadLookMaxTurn;
		rigVisual.HeadLookRate = RigHeadLookRate;
		rigVisual.Update(grounded, footIK: grounded, loop: rigTake?.Loop ?? rigPlaying.EndsWith("_Loop"));
	}

	// Climbing, slides the rig sideways so its hands, not its chest, are on the rope; crawling, lifts it.
	void UpdateRigOffset(string shown)
	{
		var visual = rigVisual!;
		var target = new Vector2(State == States.Climb
			? visual.Offset.X + Position.X + Hitbox.Rect.CenterF.X - visual.LeftHandWorldPosition.X : 0,
			shown == "PlayerCrawl" ? -RigCrawlLift : 0);
		visual.Offset = Vector2.Lerp(visual.Offset, target, 1 - MathF.Exp(-RigRopeHandResponse * Time.Delta));
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
		rigTake = RigTakes.TryGet(name, out var take) ? take : null;
		if (rigTake != null)
			rigVisual!.Play(take);
		else
			rigVisual!.Play(name, restart: restart);
		rigPlaying = name;
	}

	internal HumanoidRigVisual? RigVisual => rigVisual;
	internal string RigPlaying => rigPlaying;
	internal string? RigPreview => rigPreview;

	internal void PreviewRigTake(string? take)
	{
		rigPreview = take;
		rigRestart |= take != null;
	}

	// Where a hat sits on the rig's head, local to the pawn; null when drawing the sprite.
	protected Vector2? RigHatPosition => rigVisual == null ? null
		: rigVisual.HeadPosition - Vector2.UnitY * rigVisual.Settings.HeadSize + RigHatOffset;
	protected float RigHeadRoll => rigVisual == null ? 0 : rigVisual.HeadRoll + rigVisual.PoseRoll;

	protected void RenderBody(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			RenderSpriteSubtexture(batcher, color);
		else if (!SpriteBlinkHidden)
			rigVisual.Draw(batcher, color, silhouette: false);
	}

	public override void Render(Batcher batcher) => RenderBody(batcher, Color.White);

	public override void RenderNormals(Batcher batcher)
	{
		if (rigVisual == null)
			base.RenderNormals(batcher);
		else
			rigVisual.DrawNormals(batcher);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			base.RenderShadow(batcher, color);
		else
			rigVisual.Draw(batcher, color, silhouette: true);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (rigVisual == null)
			base.RenderReflection(batcher, color);
		else
			rigVisual.Draw(batcher, color, silhouette: true);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (rigVisual == null)
			base.RenderEmissive(batcher);
	}

	public override Rect GetVisualBounds() => rigVisual?.GetVisualBounds() ?? base.GetVisualBounds();
}
