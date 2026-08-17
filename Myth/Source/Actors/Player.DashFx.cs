using System.Numerics;
using Foster.Framework;

namespace Myth;

// Sprite-edge particles shed from the leading edge during the dash.
public partial class Player
{
	static string DashEdgeEffect => "edge_sheen";
	static int DashEdgeCount => 2;

	int dashEdgeSlot = -1;

	bool DashActive => DashBursting;

	void UpdateDashFx(float dt)
	{
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
	{
		if (Game.Particles is not { } particles)
			return;
		particles.ReleaseEffect(ref dashEdgeSlot);
	}
}
