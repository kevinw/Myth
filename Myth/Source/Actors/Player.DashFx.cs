using System.Numerics;
using Foster.Framework;

namespace Myth;

// Sprite-edge particles shed from the leading edge during the dash.
public partial class Player
{
	static string DashEdgeEffect => "edge_sheen";
	static int DashEdgeCount => 2;
	static float DashSpriteSkew => 0.13f;

	int dashEdgeSlot = -1;

	// The dash tears the frame behind it, sorting its path away from the direction of travel.
	static float WakeLife => 0.45f;
	static float WakeWidth => 14f;
	static float WakeReach => 28f;
	readonly PixelSortTrail wake = new(32);

	bool DashActive => DashBursting;

	void UpdateDashFx(float dt)
	{
		wake.Advance(dt);
		if (IsDashRunning() || DashBursting)
		{
			var direction = DashBursting ? dashDirection : dashRunDirection;
			Skew = direction * DashSpriteSkew;
			direction = direction != 0 ? direction : Math.Sign(Velocity.X);
			wake.Record((Hitbox.Rect + Position).CenterF, direction > 0 ? PixelSortDirection.Left : PixelSortDirection.Right);
		}

		if (Game.Particles is not { Supported: true } particles || Sprite is not { } sprite)
			return;

		var canvasOffset = Shift - sprite.Origin;
		if (DashActive)
		{
			if (dashEdgeSlot < 0)
				dashEdgeSlot = particles.AcquireEffect(DashEdgeEffect);
			particles.EmitSpriteEdge(dashEdgeSlot, "player", AnimationFrameIndex, Position, canvasOffset, Facing.AsInt, Velocity, DashEdgeCount, dt);
		}
	}

	public override void StirPixelSort(PixelSortCanvas canvas)
	{
		base.StirPixelSort(canvas);
		wake.Stir(canvas, WakeLife, WakeWidth, new(PixelSortLayer.Frame, default, 1, Reach: WakeReach, RelaxRate: 60));
	}

	void ReleaseDashFx()
		=> ReleaseParticleEffect(ref dashEdgeSlot);
}
