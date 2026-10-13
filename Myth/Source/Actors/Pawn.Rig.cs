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
		PixelsPerMeter = 8.5f,
		HeadSize = 1.6f,
		EyeSize = 0.5f,
		TorsoSize = 2,
		BicepSize = 1,
		ForearmSize = 1,
		ThighSize = 1.5f,
		TurnInertia = 0.3f,
		SkinMode = RigSkinMode.Solid,
	};

	HumanoidRigVisual? rigVisual;
	string rigClip = "Idle_Loop";
	bool rigRestart;

	// Locomotion clips play at this rate at full ground speed, slowing with the pawn.
	static float RigGaitRate => 2.2f;
	static float RigGaitMinRate => 0.4f;
	static Vector2 RigHatOffset => new(0, -1);

	protected bool RigDrawing => rigVisual != null;

	static string? RigClipFor(string animation) => animation switch
	{
		"idle" or "look_up" or "skid" or "wall_slide" or "climb" or "climb_idle" or "climb_down" => "Idle_Loop",
		"walk" => "Walk_Loop",
		"run" or "run_start" => "Jog_Fwd_Loop",
		"duck" or "burrow" => "Crouch_Idle_Loop",
		"crawl" => "Crouch_Fwd_Loop",
		"roll" or "back_dash" => "Roll",
		"hurt" => "Hit_Knockback",
		"sword" => "Sword_Regular_A",
		_ when animation.StartsWith("jump") || animation is "dive" or "climb_fall" => "Jump_Loop",
		_ when animation.Contains("attack") => "Sword_Regular_A",
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
				rigVisual = new(this, settings);
				rigVisual.Initialize(rigClip);
			}
		}
		if (rigVisual == null)
			return;

		var gait = rigClip is "Walk_Loop" or "Jog_Fwd_Loop" or "Crouch_Fwd_Loop";
		rigVisual.GaitActive = gait;
		rigVisual.PlaybackSpeed = gait ? MathF.Max(RigGaitMinRate, MathF.Abs(Velocity.X) / MaxGroundSpeed * RigGaitRate) : 1;
		rigVisual.Play(rigClip, restart: rigRestart);
		rigRestart = false;
		// Snap like a sprite: the rig's own turn only advances grounded and stopped, so it would moonwalk.
		if (!rigVisual.FacingMatches)
		{
			rigVisual.ResetTurn();
			rigVisual.ResetFeet();
		}
		rigVisual.Update(grounded, footIK: grounded, loop: rigClip.EndsWith("_Loop"));
	}

	// Where a hat sits on the rig's head, local to the pawn; null when drawing the sprite.
	protected Vector2? RigHatPosition => rigVisual == null ? null
		: rigVisual.HeadPosition - Vector2.UnitY * rigVisual.Settings.HeadSize + RigHatOffset;
	protected float RigHeadRoll => rigVisual?.HeadRoll ?? 0;

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
