using System.Numerics;

namespace Myth;

// Draws one of a Rope's simulated chains through CurveRibbon's material pipeline. The rope keeps
// every bit of its physics and climbing; this only supplies the chain as a spine, so a rope can
// have a surface, a width profile and stamps instead of two-tone 1px lines -- and because Rope
// already samples the Wind field per point, the ribbon sways with the wind for free.
//
// Created and owned by Rope at runtime (one per chain, so a Break() gives the fallen remnant its
// own): never authored, never saved, and so deliberately without a [GameActor] of its own.
public class RopeRibbon : CurveRibbon
{
	internal Rope.Chain? Chain;

	// The chain is already a dense polyline at Rope.SegmentLength, so there is no spline to build.
	protected override void PrepareSpine() { }

	protected override void AppendSpine(List<Vector2> into, float phase)
	{
		if (Chain is not { } chain)
			return;
		foreach (var point in chain.Points)
			into.Add(point - Position);
	}

	// It simulates, so it differs every frame.
	protected override bool SpineChanged => true;
	protected override void CaptureSpine() { }
}
