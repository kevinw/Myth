
using System.Globalization;
using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/text", Icon = "timeline", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.System, LivePositionEditable = true)]
public class CurveControl : Actor
{
	public enum Mode
	{
		Time,
		PlayerPosition
	}

	public Mode mode = Mode.Time;

	public Vector2 PlayerPositionScale = new(-1, 0);

	[ActorRef(typeof(CurveRibbon))]
	public string TargetActorId = "";

	// Grow sweeps between Min and Max. Speed 0 holds it at the midpoint, so Min == Max pins it.
	[I(0, 1)] public float Min = 0;
	[I(0, 1)] public float Max = 1;
	[I(0, 4)] public float Speed = 1;

	public CurveControl()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Update()
	{
		var target = Game.GetActorById<CurveRibbon>(TargetActorId);
		if (target == null) return;

		if (mode == Mode.Time)
		{
			target.Grow = Calc.Lerp(Min, Max, (MathF.Sin(Time.SecondsF * Speed) + 1.0f) * 0.5f);
		} else if (mode == Mode.PlayerPosition) {
			var player = Game.GetFirst<Player>();
			if (player == null) return;
			var room = Room;
			if (room == null) return;
			var positionInRoom = (Vector2)(player.Position - room.WorldBounds.Position);
			positionInRoom.X /= room.WorldBounds.Width;
			positionInRoom.Y /= room.WorldBounds.Height;
			target.Grow = Calc.Clamp(0.15f + (1.0f - positionInRoom.X), 0, 1);
		}
	}
}
