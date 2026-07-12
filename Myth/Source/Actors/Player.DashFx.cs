using System.Numerics;
using Foster.Framework;

namespace Myth;

// Sprite-edge particles during the dash. Flow orbits the silhouette (SDF
// field, see edge_flow.json); Edge sheds sparks off the leading edge
// (edge_sheen.json). Emission runs while the dash is active; the flow field
// stays bound afterwards so lingering particles ride the silhouette out.
public partial class Player
{
	public enum DashParticles { None, Flow, Edge, Both }

	static DashParticles DashFx => DashParticles.Both;
	static string DashFlowEffect => "edge_flow";
	static string DashEdgeEffect => "edge_sheen";
	static int DashFlowCount => 3; // particles per frame while dashing
	static int DashEdgeCount => 4;

	int dashFlowSlot = -1;
	int dashEdgeSlot = -1;

	bool DashActive => dashCooldownTimer > 0;

	void UpdateDashFx(float dt)
	{
		if (Game.Particles is not { Supported: true } particles || Sprite is not { } sprite)
			return;

		var wantFlow = DashFx is DashParticles.Flow or DashParticles.Both;
		var wantEdge = DashFx is DashParticles.Edge or DashParticles.Both;

		// DashFx is live-tweakable; free slots when a channel switches off.
		if (!wantFlow && dashFlowSlot >= 0)
			particles.ReleaseEffect(ref dashFlowSlot);
		if (!wantEdge && dashEdgeSlot >= 0)
			particles.ReleaseEffect(ref dashEdgeSlot);

		var canvasOffset = Shift - sprite.Origin;

		if (wantFlow)
		{
			if (dashFlowSlot < 0 && DashActive)
				dashFlowSlot = particles.AcquireEffectInstance(DashFlowEffect);
			particles.SetEffectField(dashFlowSlot, "player", AnimationFrameIndex, Position, canvasOffset, Facing.AsInt);
			if (DashActive)
				particles.EmitSpriteEdge(dashFlowSlot, "player", AnimationFrameIndex, Position, canvasOffset, Facing.AsInt, Velocity, DashFlowCount, dt);
		}

		if (wantEdge && DashActive)
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
		particles.ReleaseEffect(ref dashFlowSlot);
		particles.ReleaseEffect(ref dashEdgeSlot);
	}
}
