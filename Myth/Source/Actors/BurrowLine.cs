/*
Lets the player burrow inside solid surfaces by defining a series of line segments the burrow can follow.
Touching path points form junctions; held input chooses an aligned branch, otherwise momentum wins.
*/
using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/burrow_line", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.System,
	Anchor = SpriteAnchor.Center, LivePositionEditable = true)]
public class BurrowLine : PointPathActor, IActivationSource
{
	// First and Last identify Points[0] and Points[^1]; burrow doors are bidirectional.
	[Flags]
	public enum BurrowDoorPoints
	{
		None = 0,
		First = 1 << 0,
		Last = 1 << 1,
	}

	public static float JunctionDistance => 1f;
	internal const int MaxJunctionTransfers = 8;

	public BurrowDoorPoints BurrowDoors;
	public BurrowDoorPoints BurrowSwitches;
	[ActorRef(typeof(IActivatable))] public string TargetActorId = "";
	public float SpeedScale = 1f;
	public bool RenderProcedural = true;
	[I(0.5f, 12)] public float ProceduralWidth = 3f;
	public Color ProceduralOuterColor = new(0x454a52);
	public Color ProceduralInnerColor = new(0x959ba5);
	float ProceduralDoorRadius => MathF.Max(2f, ProceduralWidth);
	static float ChannelWidthScale => 1.55f;
	static float ChannelOpacity => 0.55f;
	static float StitchSpacing => 14f;
	static float StitchInset => 5f;
	static float GlintSpeed => 18f;
	static float MouthWidth => 10f;
	static int MouthDepth => 4;
	static float TerminalSampleInterval => 0.25f;
	static float SwitchFillTime => 1.25f;
	static float SwitchReleaseTime => 0.35f;
	float firstSwitchFill;
	float lastSwitchFill;
	float terminalSampleTimer;
	Vector2 firstTerminalNormal;
	Vector2 lastTerminalNormal;
	bool firstConnected;
	bool lastConnected;
	bool targetActive;
	bool IActivationSource.ActivationIsPersistent => false;

	public BurrowLine()
	{
		Depth = Depths.Ambient;
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Render(Batcher batcher)
	{
		if (!RenderProcedural || ProceduralWidth <= 0)
			return;

		RenderRoundedPath(batcher, ProceduralWidth * ChannelWidthScale,
			Color.Lerp(ProceduralOuterColor, new Color(0x111318), 0.65f) * ChannelOpacity);
		RenderRoundedPath(batcher, ProceduralWidth, ProceduralOuterColor);
		RenderRoundedPath(batcher, ProceduralWidth * 0.45f, ProceduralInnerColor);
		RenderStitches(batcher);
		RenderGlint(batcher);
		RenderTerminals(batcher);
		if (Points.Length == 0)
			return;
		if ((BurrowDoors & BurrowDoorPoints.First) != 0)
			batcher.Circle(Points[0], ProceduralDoorRadius, 12, new Color(0x17191d));
		if ((BurrowDoors & BurrowDoorPoints.Last) != 0)
			batcher.Circle(Points[^1], ProceduralDoorRadius, 12, new Color(0x17191d));
		if ((BurrowSwitches & BurrowDoorPoints.First) != 0)
			RenderSwitch(batcher, Points[0], firstSwitchFill);
		if ((BurrowSwitches & BurrowDoorPoints.Last) != 0)
			RenderSwitch(batcher, Points[^1], lastSwitchFill);
	}

	public override void Update()
	{
		base.Update();
		terminalSampleTimer -= Time.Delta;
		if (terminalSampleTimer <= 0)
		{
			terminalSampleTimer = TerminalSampleInterval;
			RefreshTerminals();
		}
		var player = Game.GetFirst<Player>();
		var firstActive = player?.AtBurrowLineEndpoint(this, 0) == true && IsSwitchPoint(0);
		var last = Points.Length - 1;
		var lastActive = last > 0 && player?.AtBurrowLineEndpoint(this, last) == true && IsSwitchPoint(last);
		firstSwitchFill = Calc.Approach(firstSwitchFill, firstActive ? 1 : 0,
			Time.Delta / (firstActive ? SwitchFillTime : SwitchReleaseTime));
		lastSwitchFill = Calc.Approach(lastSwitchFill, lastActive ? 1 : 0,
			Time.Delta / (lastActive ? SwitchFillTime : SwitchReleaseTime));

		var active = firstActive || lastActive;
		if (active == targetActive)
			return;
		targetActive = active;
		if (Game.GetActorById(TargetActorId) is IActivatable target)
			target.SetActive(this, active);
	}

	public override void Destroyed()
	{
		if (targetActive && Game.GetActorById(TargetActorId) is IActivatable target)
			target.SetActive(this, false);
	}

	void RenderSwitch(Batcher batcher, Vector2 center, float fill)
	{
		var radius = ProceduralDoorRadius + 1;
		var color = Color.Lerp(new Color(0x50606a), new Color(0x8de4d1), fill);
		batcher.Circle(center, radius, 16, new Color(0x17191d));
		batcher.CircleLine(center, radius, 1, 16, color * 0.8f);
		if (fill <= 0)
			return;

		batcher.Circle(center, Calc.Lerp(0.75f, radius - 1, Ease.Cube.Out(fill)), 16, color * (0.35f + fill * 0.45f));
		var turns = 1.25f + fill * 1.5f;
		var previous = center;
		for (var i = 1; i <= 18; i++)
		{
			var t = i / 18f;
			var point = center + Calc.AngleToVector(t * MathF.Tau * turns - Time.SecondsF * (2 + fill * 3)) * (radius - 1) * t;
			batcher.Line(previous, point, 0.75f + fill * 0.5f, Color.White * fill * (1 - t * 0.55f));
			previous = point;
		}
	}

	void RenderTerminals(Batcher batcher)
	{
		if (Points.Length < 2)
			return;

		RenderTerminal(batcher, 0, firstConnected, firstTerminalNormal);
		RenderTerminal(batcher, Points.Length - 1, lastConnected, lastTerminalNormal);
	}

	void RenderTerminal(Batcher batcher, int pointIndex, bool connected, Vector2 normal)
	{
		if (connected || normal == Vector2.Zero || IsDoorPoint(pointIndex) || IsSwitchPoint(pointIndex))
			return;

		var tangent = normal.TurnLeft();
		var center = (Vector2)Points[pointIndex];
		var dark = new Color(0x111318);
		for (var depth = 0; depth < MouthDepth; depth++)
		{
			var halfWidth = MouthWidth * 0.5f - depth;
			var at = center + normal * (depth + 0.5f);
			batcher.Line(at - tangent * halfWidth, at + tangent * halfWidth, 1.25f, dark * (0.9f - depth * 0.12f));
		}

		var front = tangent * MouthWidth * 0.5f;
		var back = normal * MouthDepth;
		var backSide = tangent * (MouthWidth * 0.5f - MouthDepth + 1);
		var rim = Color.Lerp(ProceduralOuterColor, ProceduralInnerColor, 0.55f);
		batcher.Line(center - front, center + front, 1, rim);
		batcher.Line(center - front, center + back - backSide, 1, rim * 0.8f);
		batcher.Line(center + front, center + back + backSide, 1, rim * 0.8f);
		batcher.Line(center + back - backSide, center + back + backSide, 1, ProceduralInnerColor * 0.7f);
	}

	void RefreshTerminals()
	{
		if (Points.Length < 2)
		{
			firstTerminalNormal = lastTerminalNormal = Vector2.Zero;
			firstConnected = lastConnected = false;
			return;
		}

		firstConnected = Game.BurrowLinePointConnected(this, 0);
		lastConnected = Game.BurrowLinePointConnected(this, Points.Length - 1);
		firstTerminalNormal = firstConnected || IsDoorPoint(0) || IsSwitchPoint(0)
			? Vector2.Zero : FindTerminalNormal(0, 1);
		lastTerminalNormal = lastConnected || IsDoorPoint(Points.Length - 1) || IsSwitchPoint(Points.Length - 1)
			? Vector2.Zero : FindTerminalNormal(Points.Length - 1, -1);
	}

	Vector2 FindTerminalNormal(int pointIndex, int direction)
	{
		var neighbor = pointIndex + direction;
		while (neighbor >= 0 && neighbor < Points.Length && Points[neighbor] == Points[pointIndex])
			neighbor += direction;
		if (neighbor < 0 || neighbor >= Points.Length)
			return Vector2.Zero;

		var inward = ((Vector2)(Points[neighbor] - Points[pointIndex])).Normalized();
		var center = (Vector2)Points[pointIndex];
		var normal = Vector2.Zero;
		var bestScore = 0;
		var bestAlignment = float.NegativeInfinity;
		ConsiderTerminalNormal(center, inward, Vector2.UnitX, ref normal, ref bestScore, ref bestAlignment);
		ConsiderTerminalNormal(center, inward, -Vector2.UnitX, ref normal, ref bestScore, ref bestAlignment);
		ConsiderTerminalNormal(center, inward, Vector2.UnitY, ref normal, ref bestScore, ref bestAlignment);
		ConsiderTerminalNormal(center, inward, -Vector2.UnitY, ref normal, ref bestScore, ref bestAlignment);
		return normal;
	}

	void ConsiderTerminalNormal(Vector2 center, Vector2 inward, Vector2 candidate,
		ref Vector2 normal, ref int bestScore, ref float bestAlignment)
	{
		var score = TerminalSolidScore(center, candidate);
		if (score <= 0)
			return;
		var alignment = Vector2.Dot(candidate, inward);
		if (score > bestScore || score == bestScore && alignment > bestAlignment)
		{
			normal = candidate;
			bestScore = score;
			bestAlignment = alignment;
		}
	}

	int TerminalSolidScore(Vector2 center, Vector2 normal)
	{
		var score = 0;
		var tangent = normal.TurnLeft();
		for (var depth = 1; depth <= 4; depth++)
			for (var across = -2; across <= 2; across += 2)
				if (Game.BurrowLineSolidAt(Position + center + normal * depth + tangent * across))
					score++;
		return score;
	}

	void RenderStitches(Batcher batcher)
	{
		var distance = 0f;
		var stitch = 0;
		var next = StitchSpacing * Calc.Lerp(0.85f, 1.15f, StitchUnit(stitch, 0));
		for (var i = 1; i < Points.Length; i++)
		{
			var from = (Vector2)Points[i - 1];
			var delta = (Vector2)(Points[i] - Points[i - 1]);
			var length = delta.Length();
			if (length <= 0)
				continue;

			var heading = delta / length;
			var side = heading.TurnLeft() * (ProceduralWidth + 2f) * 0.5f;
			while (next < distance + length)
			{
				var along = next - distance;
				if (along >= StitchInset && length - along >= StitchInset)
				{
					var center = from + heading * along;
					var scaledSide = side * Calc.Lerp(0.85f, 1.15f, StitchUnit(stitch, 1));
					var lean = heading * Calc.Lerp(-0.65f, 0.65f, StitchUnit(stitch, 2));
					var weight = Calc.Lerp(1.25f, 1.65f, StitchUnit(stitch, 3));
					batcher.Line(center - scaledSide - lean, center + scaledSide + lean, weight,
						Color.Lerp(ProceduralOuterColor, new Color(0x111318), 0.35f));
					batcher.Line(center - scaledSide * 0.72f - lean, center + scaledSide * 0.72f + lean, 0.6f,
						Color.Lerp(ProceduralOuterColor, ProceduralInnerColor, 0.45f));
				}
				stitch++;
				next += StitchSpacing * Calc.Lerp(0.82f, 1.18f, StitchUnit(stitch, 0));
			}
			distance += length;
		}
	}

	float StitchUnit(int stitch, uint salt)
		=> (Hash32.Mix((uint)Position.X * 0x9e3779b9u ^ (uint)Position.Y * 0x85ebca6bu ^
			(uint)(stitch + 1) * 0xc2b2ae35u ^ salt * 0x27d4eb2du) & 0xffff) / 65535f;

	void RenderGlint(Batcher batcher)
	{
		var length = PathLength();
		if (length <= 0)
			return;

		var distance = (Time.SecondsF * GlintSpeed + Position.X * 0.37f + Position.Y * 0.19f) % length;
		if (distance < 0)
			distance += length;
		var center = PointAtDistance(distance) - Position;
		var color = Color.Lerp(ProceduralInnerColor, Color.White, 0.7f);
		batcher.Circle(center, MathF.Max(2, ProceduralWidth), 10, color * 0.28f, color * 0f);
		batcher.Circle(center, MathF.Max(0.65f, ProceduralWidth * 0.22f), 8, color * 0.9f);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (!RenderProcedural || ProceduralWidth <= 0)
			return;

		var radius = ProceduralWidth * 0.5f;
		for (var i = 1; i < Points.Length; i++)
			RenderSegmentNormals(batcher, Points[i - 1], Points[i], radius);
		for (var i = 0; i < Points.Length; i++)
			RenderRoundNormals(batcher, Points[i], radius);
		if (Points.Length == 0)
			return;
		if ((BurrowDoors & BurrowDoorPoints.First) != 0)
			RenderRoundNormals(batcher, Points[0], ProceduralDoorRadius, concave: true);
		if ((BurrowDoors & BurrowDoorPoints.Last) != 0)
			RenderRoundNormals(batcher, Points[^1], ProceduralDoorRadius, concave: true);
		if ((BurrowSwitches & BurrowDoorPoints.First) != 0)
			RenderRoundNormals(batcher, Points[0], ProceduralDoorRadius + 1, concave: true);
		if ((BurrowSwitches & BurrowDoorPoints.Last) != 0)
			RenderRoundNormals(batcher, Points[^1], ProceduralDoorRadius + 1, concave: true);
	}

	void RenderRoundedPath(Batcher batcher, float width, Color color)
	{
		var radius = width * 0.5f;
		for (var i = 1; i < Points.Length; i++)
			if (Points[i - 1] != Points[i])
				batcher.Line(Points[i - 1], Points[i], width, color);
		for (var i = 0; i < Points.Length; i++)
			batcher.Circle(Points[i], radius, 12, color);
	}

	static void RenderSegmentNormals(Batcher batcher, Vector2 from, Vector2 to, float radius)
	{
		var delta = to - from;
		if (delta == Vector2.Zero)
			return;

		var side = new Vector2(-delta.Y, delta.X).Normalized();
		var edgeA = NormalColor(side);
		var edgeB = NormalColor(-side);
		var flat = new Color(0x8080ff);
		var offset = side * radius;
		batcher.Quad(from + offset, from, to, to + offset, edgeA, flat, flat, edgeA);
		batcher.Quad(from, from - offset, to - offset, to, flat, edgeB, edgeB, flat);
	}

	static void RenderRoundNormals(Batcher batcher, Vector2 center, float radius, bool concave = false)
	{
		const int steps = 12;
		var flat = new Color(0x8080ff);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var b = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(center, center + a * radius, center + b * radius,
				flat, NormalColor(concave ? -a : a), NormalColor(concave ? -b : b));
		}
	}

	static Color NormalColor(Vector2 direction)
		=> new(direction.X * 0.5f + 0.5f, direction.Y * 0.5f + 0.5f, 0.5f, 1f);

	public bool TryGetEndpoint(Vector2 near, float maxDistance, out int segment, out float t, out int direction, out Vector2 point, out Vector2 heading)
	{
		segment = 0;
		t = 0;
		direction = 1;
		point = Position;
		heading = Vector2.UnitX;
		if (Points.Length < 2)
			return false;

		var first = WorldPoint(0);
		var last = WorldPoint(Points.Length - 1);
		var firstDistance = Vector2.DistanceSquared(near, first);
		var lastDistance = Vector2.DistanceSquared(near, last);
		if (MathF.Min(firstDistance, lastDistance) > maxDistance * maxDistance)
			return false;

		if (firstDistance <= lastDistance)
		{
			point = first;
			heading = SegmentHeading(0, 1);
			return true;
		}

		segment = Points.Length - 2;
		t = 1;
		direction = -1;
		point = last;
		heading = SegmentHeading(segment, -1);
		return true;
	}

	public bool NearEndpoint(Vector2 point, float distance)
		=> Points.Length > 0 &&
			(MathF.Min(Vector2.DistanceSquared(point, WorldPoint(0)), Vector2.DistanceSquared(point, WorldPoint(Points.Length - 1))) <= distance * distance);

	public bool IsDoorPoint(int pointIndex)
		=> pointIndex == 0 && (BurrowDoors & BurrowDoorPoints.First) != 0 ||
			pointIndex == Points.Length - 1 && (BurrowDoors & BurrowDoorPoints.Last) != 0;

	public bool IsSwitchPoint(int pointIndex)
		=> pointIndex == 0 && (BurrowSwitches & BurrowDoorPoints.First) != 0 ||
			pointIndex == Points.Length - 1 && (BurrowSwitches & BurrowDoorPoints.Last) != 0;

	public bool TryGetSwitchDistance(int segment, float t, int direction, out float distance)
	{
		distance = 0;
		var point = direction > 0 ? Points.Length - 1 : 0;
		if (!IsSwitchPoint(point))
			return false;

		while (segment >= 0 && segment < Points.Length - 1)
		{
			var length = Vector2.Distance(Points[segment], Points[segment + 1]);
			distance += (direction > 0 ? 1 - t : t) * length;
			if (!NextSegment(ref segment, ref t, direction))
				return true;
		}
		return false;
	}

	public bool TryGetSwitchNear(Vector2 world, float distance, out int pointIndex, out Vector2 point)
	{
		pointIndex = -1;
		point = default;
		if (Points.Length == 0)
			return false;
		if (IsSwitchPoint(0) && Vector2.DistanceSquared(world, WorldPoint(0)) <= distance * distance)
			pointIndex = 0;
		else if (IsSwitchPoint(Points.Length - 1) &&
			Vector2.DistanceSquared(world, WorldPoint(Points.Length - 1)) <= distance * distance)
			pointIndex = Points.Length - 1;
		if (pointIndex < 0)
			return false;
		point = WorldPoint(pointIndex);
		return true;
	}

	public float SwitchFill(int pointIndex)
		=> pointIndex == 0 ? firstSwitchFill : lastSwitchFill;

	public bool Advance(ref int segment, ref float t, int direction, float distance,
		out Vector2 point, out Vector2 heading, out float remaining, out int junctionPoint)
	{
		var reachedEnd = false;
		junctionPoint = -1;
		while (distance > 0 && segment >= 0 && segment < Points.Length - 1)
		{
			var length = Vector2.Distance(Points[segment], Points[segment + 1]);
			if (length <= 0)
			{
				if (!NextSegment(ref segment, ref t, direction))
				{
					reachedEnd = true;
					break;
				}
				continue;
			}

			var available = (direction > 0 ? 1 - t : t) * length;
			if (distance < available)
			{
				t += direction * distance / length;
				distance = 0;
			}
			else
			{
				t = direction > 0 ? 1 : 0;
				distance -= available;
				junctionPoint = direction > 0 ? segment + 1 : segment;
				if (Game.BurrowLinePointConnected(this, junctionPoint))
				{
					reachedEnd = true;
					break;
				}
				if (!NextSegment(ref segment, ref t, direction))
				{
					reachedEnd = true;
					break;
				}
			}
		}

		point = Position + Vector2.Lerp(Points[segment], Points[segment + 1], t);
		heading = SegmentHeading(segment, direction);
		remaining = reachedEnd ? distance : 0;
		return reachedEnd;
	}

	public bool TryFindJunction(int segment, float t, Vector2 steeringIntent, float maxDistance,
		int preferredDirection, out int direction, out float distance)
	{
		var forward = TryFindJunction(segment, t, 1, steeringIntent, maxDistance, out var forwardDistance);
		var backward = TryFindJunction(segment, t, -1, steeringIntent, maxDistance, out var backwardDistance);
		if (!forward && !backward)
		{
			direction = preferredDirection;
			distance = 0;
			return false;
		}

		direction = !backward || forward && (forwardDistance < backwardDistance ||
			forwardDistance == backwardDistance && preferredDirection > 0) ? 1 : -1;
		distance = direction > 0 ? forwardDistance : backwardDistance;
		return true;
	}

	bool TryFindJunction(int segment, float t, int direction, Vector2 steeringIntent,
		float maxDistance, out float distance)
	{
		distance = 0;
		while (segment >= 0 && segment < Points.Length - 1)
		{
			var length = Vector2.Distance(Points[segment], Points[segment + 1]);
			distance += (direction > 0 ? 1 - t : t) * length;
			if (distance > maxDistance)
				return false;

			var pointIndex = direction > 0 ? segment + 1 : segment;
			var travelHeading = SegmentHeading(segment, direction);
			if (Game.BurrowLinePointConnected(this, pointIndex) &&
				Game.TryContinueBurrowLine(this, pointIndex, segment, direction, travelHeading, steeringIntent,
					out _, out _, out _, out _, out _, out var branchHeading) &&
				Game.BurrowLineHeadingFollowsSteering(branchHeading, travelHeading, steeringIntent))
				return true;

			if (!NextSegment(ref segment, ref t, direction))
				return false;
		}

		return false;
	}

	public void Trace(int segment, float t, int travelDirection, Vector2 steeringIntent,
		float distance, List<Vector2> points, List<Vector2> backward)
	{
		points.Clear();
		backward.Clear();
		TraceDirection(segment, t, travelDirection, distance, points, steeringIntent: steeringIntent);
		TraceDirection(segment, t, -travelDirection, distance, backward);
		for (var i = 1; i < backward.Count; i++)
			points.Insert(0, backward[i]);
	}

	internal void TraceDirection(int segment, float t, int direction, float distance, List<Vector2> into,
		bool includeStart = true, Vector2 steeringIntent = default, int junctionsRemaining = MaxJunctionTransfers)
	{
		if (includeStart)
			into.Add(Position + Vector2.Lerp(Points[segment], Points[segment + 1], t));
		while (distance > 0)
		{
			var from = (Vector2)Points[segment];
			var to = (Vector2)Points[segment + 1];
			var length = Vector2.Distance(from, to);
			var available = (direction > 0 ? 1 - t : t) * length;
			if (distance < available && length > 0)
			{
				t += direction * distance / length;
				into.Add(Position + Vector2.Lerp(from, to, t));
				return;
			}

			distance -= available;
			var endpoint = Position + (direction > 0 ? to : from);
			var pointIndex = direction > 0 ? segment + 1 : segment;
			into.Add(endpoint);
			if (junctionsRemaining > 0 && Game.BurrowLinePointConnected(this, pointIndex) &&
				Game.TryContinueBurrowLine(this, pointIndex, segment, direction, SegmentHeading(segment, direction),
					steeringIntent, out var branch, out var branchSegment, out var branchT, out var branchDirection,
					out var branchPoint, out _))
			{
				if (Vector2.DistanceSquared(endpoint, branchPoint) > 0.001f)
					into.Add(branchPoint);
				branch!.TraceDirection(branchSegment, branchT, branchDirection, distance, into, includeStart: false,
					steeringIntent: steeringIntent, junctionsRemaining: junctionsRemaining - 1);
				return;
			}
			if (!NextSegment(ref segment, ref t, direction))
				return;

		}
	}

	bool NextSegment(ref int segment, ref float t, int direction)
	{
		segment += direction;
		if (segment < 0 || segment >= Points.Length - 1)
		{
			segment = Math.Clamp(segment, 0, Points.Length - 2);
			t = direction > 0 ? 1 : 0;
			return false;
		}

		t = direction > 0 ? 0 : 1;
		return true;
	}

	internal Vector2 SegmentHeading(int segment, int direction)
	{
		var delta = (Vector2)(Points[segment + 1] - Points[segment]);
		return delta.LengthSquared() > 0 ? Vector2.Normalize(delta) * direction : Vector2.UnitX * direction;
	}
}
