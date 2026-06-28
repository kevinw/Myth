using System.Numerics;
using Foster.Framework;

namespace Myth;

// Rainbow SDF "merge" aura: when the player and Bud are both ducking, their
// silhouette distance fields are smooth-min'd into a single blobby rainbow
// outline that bridges the gap between them. Eases in/out with the duck state.
public partial class Player
{
	static float AuraEaseSpeed => 4.3f;   // fade in/out responsiveness
	static float AuraMaxDist => 22f;    // aura reach (px)
	static float AuraStripe => 2.5f;      // rainbow band width (px)
	static float AuraDistScale => 255f; // R8 -> px
	static float AuraK => 10f;          // smooth-min blob roundness / bridge thickness
	static float AuraEnterTime => 1.9f;

	float auraAmount; // eased 0..1
	float auraEnterTimer;


	private bool AuraActive =>
		Velocity.RoundToPoint2() == Point2.Zero && IsDucking && Game.GetFirst<Bud>() is { } bud && bud.IsDucking;

	public void UpdateAura(float dt)
	{
		if (AuraActive)
		{
			auraEnterTimer += Time.Delta;
		}
		else
		{
			auraEnterTimer = 0;
		}

		var target = auraEnterTimer >= AuraEnterTime ? 1f : 0f;
		auraAmount += (target - auraAmount) * (1f - MathF.Exp(-dt * AuraEaseSpeed));
	}

	// Called from Render (inside the player-local matrix; local 0 = player position).
	private void RenderAura(Batcher batcher)
	{
		if (auraAmount <= 0.01f)
			return;
		if (Sprite is not { } playerSprite)
			return;
		if (Game.GetFirst<Bud>() is not { } bud || bud.Sprite is not { } budSprite)
			return;
		if (SpriteSdf.Frame("player", AnimationFrameIndex) is not { } playerSdf)
			return;
		if (SpriteSdf.Frame("bud", bud.AnimationFrameIndex) is not { } budSdf)
			return;

		var margin = new Vector2(SpriteSdf.Margin);

		// Padded SDF rects in player-local space (unflipped; the shader mirrors).
		var playerTopLeft = Shift - playerSprite.Origin - margin;
		var playerSize = playerSdf.Source.Size;

		var budLocal = (Vector2)(bud.Position - Position);
		var budTopLeft = budLocal + bud.Shift - budSprite.Origin - margin;
		var budSize = budSdf.Source.Size;

		var min = Vector2.Min(playerTopLeft, budTopLeft);
		var max = Vector2.Max(playerTopLeft + playerSize, budTopLeft + budSize);
		var regionSize = max - min;
		var atlasSize = SpriteSdf.AtlasSize;

		Span<float> uniforms =
		[
			regionSize.X, regionSize.Y, atlasSize.X, atlasSize.Y,                 // Region
			min.X, min.Y, 0f, 0f,                                                 // RTopLeft
			playerTopLeft.X, playerTopLeft.Y, playerSize.X, playerSize.Y,         // P0
			playerSdf.Source.X, playerSdf.Source.Y, Facing.AsInt, 0f,             // P1
			budTopLeft.X, budTopLeft.Y, budSize.X, budSize.Y,                     // B0
			budSdf.Source.X, budSdf.Source.Y, bud.Facing.AsInt, budLocal.X,       // B1 (w = bud mirror axis)
			Game.SdfAuraScrollTime, AuraMaxDist, AuraStripe, AuraDistScale,       // A0
			AuraK, auraAmount, 0f, 0f,                                            // A1
		];

		Game.RenderSdfAura(
			batcher, uniforms,
			min,
			new Vector2(max.X, min.Y),
			max,
			new Vector2(min.X, max.Y));
	}
}
