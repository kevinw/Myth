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

	bool DashActive => DashBursting;

	void UpdateDashFx(float dt)
	{
		if (IsDashRunning() || DashBursting)
			Skew = (DashBursting ? dashDirection : dashRunDirection) * DashSpriteSkew;

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

	void ReleaseDashFx()
		=> ReleaseParticleEffect(ref dashEdgeSlot);
}
