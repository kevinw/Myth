using System.Numerics;
using Foster.Framework;

namespace Myth;

// Cloth over the legs, from the bone body's costume: a kilt (a ring of pleats hanging from the hips) and trousers (a
// loose tube per leg with a hem that lags behind the ankle). Simulated in view space -- screen x and y plus depth, all
// in pixels -- against the posed legs, so foot IK, squish and the other 2D pose passes have already moved what the
// cloth hangs from and is pushed by. It simulates on the smooth pose and draws on the pixel-snapped one, its own points
// snapped with a little hysteresis so a cloth at rest holds still. Faces are depth-sorted into the bone draw list.
internal sealed partial class HumanoidRigVisual
{
	enum GarmentShape { Quad, Disc }
	readonly record struct GarmentFace(GarmentShape Shape, Vector2 A, Vector2 B, Vector2 C, Vector2 D, float Depth, Color Color);

	struct ClothPoint
	{
		public Vector3 At, Previous;
	}

	// Per garment, per frame: how fast what it hangs from moves and how fast the cloth moves relative to that, and how
	// long the body has been still. Drives the snap hysteresis and settling.
	struct ClothMotion
	{
		public float Anchors, Cloth, Still;

		public void Step(float anchorMoved, float clothMoved, float dt)
		{
			Anchors = MathF.Max(Anchors, anchorMoved / dt);
			Cloth = MathF.Max(Cloth, clothMoved / dt);
		}

		public void EndFrame(float delta)
		{
			Still = Anchors < ClothSleepSpeed ? Still + delta : 0;
			Anchors = Cloth = 0;
		}

		public readonly bool Settling => Still > ClothSleepDelay;
		public readonly float Hysteresis => float.Lerp(ClothSnapStill, ClothSnapMoving, Math.Clamp(Cloth / ClothSnapFastSpeed, 0, 1));
	}

	static float ClothGravity => 600f;
	static float ClothSpring => 420f; // pull back to the rest drape, × the piece's Stiffness
	static float ClothDamping => 14f; // of motion relative to where the cloth hangs from, so it settles without trailing
	static float ClothAirDrag => 1.5f; // of motion through the world: a little trail when the body moves
	// Pixels a drawn cloth point must stray before it snaps to a new pixel: sticky when the cloth is calm, so a still
	// kilt is a still drawing, and responsive at ClothSnapFastSpeed (px/s, relative to the body) and up.
	static float ClothSnapStill => 1.25f;
	static float ClothSnapMoving => 0.5f;
	static float ClothSnapFastSpeed => 40f;
	// Once what the cloth hangs from has moved slower than ClothSleepSpeed (px/s) for ClothSleepDelay, it eases onto its
	// rest drape at ClothSettleRate and holds there until the body moves again.
	static float ClothSleepSpeed => 8f;
	static float ClothSleepDelay => 0.25f;
	static float ClothSettleRate => 6f;
	static float ClothResetDistance => 32f; // anchors jumping further than this in a step (a teleport) re-hang the cloth
	static float KiltWaistRaise => 0.05f; // meters above the hip joints
	static float KiltDepthRatio => 0.75f; // front-to-back over side-to-side
	static float KiltHangDown => 0.8f; // the drape's pull toward true down, over down the body (which leans in a crouch)
	static float KiltLapRadius => 1.7f; // × a thigh's half width: wide enough that the thighs meet, so the front lies across both
	static float KiltPleatShade => 0.86f; // every other pleat
	static float KiltInsideFacing => -0.25f; // panels turned further away than this show their inside, behind the legs
	static float TrouserHemRaise => 0.12f; // of the shin, so the foot shows
	static float TrouserKneeBulge => 0.4f; // × thigh width at a right-angle knee

	readonly List<GarmentFace> garmentFaces = [];
	ClothPoint[] kiltPoints = []; // per pleat: mid, hem
	Vector3[] kiltAnchors = []; // last step's, for motion relative to them
	Vector2[] kiltDrawn = []; // per pleat: mid, hem, as last drawn
	readonly ClothPoint[] trouserHems = new ClothPoint[2];
	readonly Vector3[] trouserKnees = new Vector3[2];
	readonly Vector2[] trouserHemsDrawn = new Vector2[2];
	ClothMotion kiltMotion, trouserMotion;
	float kiltHysteresis, trouserHysteresis; // this frame's, taken before the motion resets
	CostumePiece? clothKilt, clothTrousers;
	Vector3 lastKiltWaist;
	bool clothReady;
	float clothLastDelta = Verlet.MaxStep;

	Vector3 View(int joint) => new(projectedPose[joint], jointDepth[joint] * Settings.PixelsPerMeter);
	Vector3 World(Vector3 local) => local + new Vector3(owner.Position.X, owner.Position.Y, 0);
	Vector2 Local(Vector3 world) => new(world.X - owner.Position.X, world.Y - owner.Position.Y);

	void SimulateGarments()
	{
		var kilt = default(CostumePiece);
		var trousers = default(CostumePiece);
		if (Settings.BodyStyle == RigBodyStyle.Bones && Assets.GetCostume(Settings.Bones.CostumeId) is { } costume)
			foreach (var piece in costume.Pieces)
				if (piece.Kind == CostumePieceKind.Kilt)
					kilt = piece;
				else if (piece.Kind == CostumePieceKind.Trousers)
					trousers = piece;
		if (kilt != clothKilt || trousers != clothTrousers)
		{
			clothKilt = kilt;
			clothTrousers = trousers;
			clothReady = false;
		}
		if (kilt == null && trousers == null)
			return;
		if (kilt != null && kiltPoints.Length != kilt.Count * 2)
		{
			kiltPoints = new ClothPoint[kilt.Count * 2];
			kiltDrawn = new Vector2[kilt.Count * 2];
			kiltAnchors = new Vector3[kilt.Count];
			clothReady = false;
		}

		var dt = MathF.Max(visualDelta, 0.0001f);
		for (var steps = new Verlet.Steps(dt, ref clothLastDelta); steps.Next(out var step, out var scale);)
		{
			if (kilt != null)
				SimulateKilt(kilt, step, scale);
			if (trousers != null)
			{
				SimulateTrouserHem(trousers, leftLeg, 0, step, scale);
				SimulateTrouserHem(trousers, rightLeg, 1, step, scale);
			}
			clothReady = true;
		}
		kiltHysteresis = kiltMotion.Hysteresis;
		trouserHysteresis = trouserMotion.Hysteresis;
		kiltMotion.EndFrame(dt);
		trouserMotion.EndFrame(dt);
	}

	void BuildGarmentFaces()
	{
		garmentFaces.Clear();
		var kilt = clothKilt;
		var trousers = clothTrousers;
		if (kilt != null)
			AddKiltFaces(kilt);
		if (trousers != null)
		{
			AddTrouserFaces(trousers, leftLeg, SnapCloth(ref trouserHemsDrawn[0], Local(trouserHems[0].At), trouserHysteresis));
			AddTrouserFaces(trousers, rightLeg, SnapCloth(ref trouserHemsDrawn[1], Local(trouserHems[1].At), trouserHysteresis));
			AddTrouserSeat(trousers);
		}
		// Far to near, as the bone list is built; a handful of faces, so insertion sort.
		for (var i = 1; i < garmentFaces.Count; i++)
			for (var j = i; j > 0 && garmentFaces[j - 1].Depth > garmentFaces[j].Depth; j--)
				(garmentFaces[j - 1], garmentFaces[j]) = (garmentFaces[j], garmentFaces[j - 1]);
	}

	// The waist ring: center, side-to-side axis, up the spine, and forward (toward the toes).
	void KiltFrame(CostumePiece kilt, out Vector3 waist, out Vector3 side, out Vector3 up, out Vector3 forward,
		out float sideRadius, out float length)
	{
		var left = View(leftLeg.Hip);
		var right = View(rightLeg.Hip);
		var across = right - left;
		side = across.LengthSquared() > 0.01f ? Vector3.Normalize(across) : Vector3.UnitX;
		var spine = View(spine1) - View(pelvis);
		up = spine - side * Vector3.Dot(spine, side);
		up = up.LengthSquared() > 0.01f ? Vector3.Normalize(up) : -Vector3.UnitY;
		forward = Vector3.Cross(up, side);
		var toes = View(leftLeg.Tip) + View(rightLeg.Tip) - View(leftLeg.Ankle) - View(rightLeg.Ankle);
		if (Vector3.Dot(forward, toes) < 0)
			forward = -forward;
		var ppm = Settings.PixelsPerMeter;
		waist = (left + right) * 0.5f + up * KiltWaistRaise * ppm;
		sideRadius = (across.Length() * 0.5f + Settings.Bones.ThighSize * ppm * 0.5f) * kilt.Width;
		length = kilt.Length * ppm;
	}

	Vector3 KiltAnchor(int pleat, int count, Vector3 waist, Vector3 side, Vector3 forward, float sideRadius, out Vector3 outward)
	{
		var angle = (pleat + 0.5f) / count * MathF.Tau;
		outward = side * MathF.Cos(angle) + forward * MathF.Sin(angle);
		return waist + side * MathF.Cos(angle) * sideRadius + forward * MathF.Sin(angle) * sideRadius * KiltDepthRatio;
	}

	void SimulateKilt(CostumePiece kilt, float dt, float scale)
	{
		KiltFrame(kilt, out var waist, out var side, out var up, out var forward, out var sideRadius, out var length);
		var worldWaist = World(waist);
		var reset = !clothReady || Vector3.DistanceSquared(worldWaist, lastKiltWaist) > ClothResetDistance * ClothResetDistance;
		lastKiltWaist = worldWaist;
		var spring = ClothSpring * kilt.Stiffness;
		var half = length * 0.5f;
		for (var i = 0; i < kilt.Count; i++)
		{
			var anchor = World(KiltAnchor(i, kilt.Count, waist, side, forward, sideRadius, out var outward));
			var drape = Vector3.Normalize(Vector3.Normalize(Vector3.Lerp(-up, Vector3.UnitY, KiltHangDown)) + outward * kilt.Flare);
			// The rest drape lies over the legs: each joint hangs on from the last and is lifted out of the lap.
			var restMid = anchor + drape * half;
			PushOutOfLegs(ref restMid, anchor, KiltLapRadius);
			restMid = anchor + SafeNormalize(restMid - anchor, drape) * half;
			var restHem = restMid + drape * half;
			PushOutOfLegs(ref restHem, anchor, KiltLapRadius);
			restHem = restMid + SafeNormalize(restHem - restMid, drape) * half;
			ref var mid = ref kiltPoints[i * 2];
			ref var hem = ref kiltPoints[i * 2 + 1];
			var anchorMoved = reset ? Vector3.Zero : anchor - kiltAnchors[i];
			kiltAnchors[i] = anchor;
			if (reset)
			{
				mid.At = mid.Previous = restMid;
				hem.At = hem.Previous = restHem;
				kiltDrawn[i * 2] = Local(restMid).Round();
				kiltDrawn[i * 2 + 1] = Local(restHem).Round();
				continue;
			}
			var hemFrom = hem.At;
			ClothStep(ref mid, restMid, anchorMoved, spring, dt, scale);
			ClothStep(ref hem, restHem, anchorMoved, spring, dt, scale);
			// Constraints and collisions reshape the cloth without launching it: the previous positions move along.
			var midStepped = mid.At;
			var hemStepped = hem.At;
			mid.At = anchor + SafeNormalize(mid.At - anchor, drape) * half;
			PushOutOfLegs(ref mid.At, anchor, KiltLapRadius);
			hem.At = mid.At + SafeNormalize(hem.At - mid.At, drape) * half;
			PushOutOfLegs(ref hem.At, anchor, KiltLapRadius);
			mid.Previous += mid.At - midStepped;
			hem.Previous += hem.At - hemStepped;
			if (kiltMotion.Settling)
			{
				Settle(ref mid, restMid, dt);
				Settle(ref hem, restHem, dt);
			}
			kiltMotion.Step(anchorMoved.Length(), (hem.At - hemFrom - anchorMoved).Length(), dt);
		}
	}

	static void Settle(ref ClothPoint point, Vector3 rest, float dt)
	{
		var amount = 1 - MathF.Exp(-ClothSettleRate * dt);
		point.At = Vector3.Lerp(point.At, rest, amount);
		point.Previous = Vector3.Lerp(point.Previous, rest, amount);
	}

	void SimulateTrouserHem(CostumePiece trousers, Leg leg, int side, float dt, float scale)
	{
		ref var hem = ref trouserHems[side];
		var knee = World(View(leg.Knee));
		var rest = Vector3.Lerp(World(View(leg.Ankle)), knee, TrouserHemRaise);
		var reach = Vector3.Distance(knee, rest);
		var kneeMoved = knee - trouserKnees[side];
		trouserKnees[side] = knee;
		if (!clothReady || Vector3.DistanceSquared(hem.At, rest) > ClothResetDistance * ClothResetDistance)
		{
			hem.At = hem.Previous = rest;
			trouserHemsDrawn[side] = Local(rest).Round();
			return;
		}
		var from = hem.At;
		ClothStep(ref hem, rest, kneeMoved, ClothSpring * trousers.Stiffness, dt, scale);
		var stepped = hem.At;
		hem.At = knee + SafeNormalize(hem.At - knee, rest - knee) * reach;
		hem.Previous += hem.At - stepped;
		if (trouserMotion.Settling)
			Settle(ref hem, rest, dt);
		trouserMotion.Step(kneeMoved.Length(), (hem.At - from - kneeMoved).Length(), dt);
	}

	// Damped relative to what the cloth hangs from (anchorMoved this step), so it settles quickly without lagging
	// behind a moving body; ClothAirDrag adds the little trail through the world.
	static void ClothStep(ref ClothPoint point, Vector3 rest, Vector3 anchorMoved, float spring, float dt, float scale)
	{
		var accel = new Vector3(0, ClothGravity, 0) + (rest - point.At) * spring;
		var from = point.At;
		var moved = (from - point.Previous) * scale * Verlet.Damping(ClothAirDrag, dt);
		point.At = from + anchorMoved + (moved - anchorMoved) * Verlet.Damping(ClothDamping, dt) + accel * (dt * dt);
		point.Previous = from;
	}

	// Only moves to a new pixel once the point has strayed `hysteresis` pixels from the last one.
	Vector2 SnapCloth(ref Vector2 drawn, Vector2 at, float hysteresis)
	{
		if (!Settings.PixelSnap)
			return at;
		if (MathF.Abs(at.X - drawn.X) >= hysteresis)
			drawn.X = MathF.Round(at.X);
		if (MathF.Abs(at.Y - drawn.Y) >= hysteresis)
			drawn.Y = MathF.Round(at.Y);
		return drawn;
	}

	static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
		=> v.LengthSquared() > 0.0001f ? Vector3.Normalize(v) : Vector3.Normalize(fallback);

	// Knees and shins shove the cloth aside: points inside a leg capsule move out of it, on the side the cloth hangs
	// from -- it can't pass through the leg, so a raised thigh carries the front of a kilt over it rather than under.
	void PushOutOfLegs(ref Vector3 world, Vector3 anchor, float fatten)
	{
		var ppm = Settings.PixelsPerMeter;
		var thigh = Settings.Bones.ThighSize * ppm * 0.5f * fatten + 1;
		var shin = Settings.Bones.ShinSize * ppm * 0.5f * fatten + 1;
		PushOutOfCapsule(ref world, World(View(leftLeg.Hip)), World(View(leftLeg.Knee)), thigh, anchor);
		PushOutOfCapsule(ref world, World(View(leftLeg.Knee)), World(View(leftLeg.Ankle)), shin, anchor);
		PushOutOfCapsule(ref world, World(View(rightLeg.Hip)), World(View(rightLeg.Knee)), thigh, anchor);
		PushOutOfCapsule(ref world, World(View(rightLeg.Knee)), World(View(rightLeg.Ankle)), shin, anchor);
	}

	static void PushOutOfCapsule(ref Vector3 point, Vector3 from, Vector3 to, float radius, Vector3 anchor)
	{
		var segment = to - from;
		var lengthSquared = segment.LengthSquared();
		var t = lengthSquared > 0.0001f ? Math.Clamp(Vector3.Dot(point - from, segment) / lengthSquared, 0, 1) : 0;
		var closest = from + segment * t;
		var away = point - closest;
		if (away.LengthSquared() >= radius * radius)
			return;
		// Across the leg, toward the anchor's side; along it, the point stays where it was.
		var toAnchor = anchor - closest;
		if (lengthSquared > 0.0001f)
			toAnchor -= segment * (Vector3.Dot(toAnchor, segment) / lengthSquared);
		point = closest + SafeNormalize(toAnchor, away) * radius;
	}

	void AddKiltFaces(CostumePiece kilt)
	{
		KiltFrame(kilt, out var waist, out var side, out _, out var forward, out var sideRadius, out _);
		var ppm = Settings.PixelsPerMeter;
		// The legs are inside the kilt: any panel not turned away covers them, whatever depth it hangs at.
		var legsFront = MathF.Max(MathF.Max(jointDepth[leftLeg.Knee], jointDepth[leftLeg.Ankle]),
			MathF.Max(jointDepth[rightLeg.Knee], jointDepth[rightLeg.Ankle]));
		legsFront = MathF.Max(legsFront, MathF.Max(jointDepth[leftLeg.Hip], jointDepth[rightLeg.Hip])) + 0.001f;
		for (var i = 0; i < kilt.Count; i++)
		{
			var next = (i + 1) % kilt.Count;
			var anchorA = KiltAnchor(i, kilt.Count, waist, side, forward, sideRadius, out var outwardA);
			var anchorB = KiltAnchor(next, kilt.Count, waist, side, forward, sideRadius, out var outwardB);
			var midA = kiltPoints[i * 2].At;
			var midB = kiltPoints[next * 2].At;
			var hemA = kiltPoints[i * 2 + 1].At;
			var hemB = kiltPoints[next * 2 + 1].At;
			var drawnMidA = SnapCloth(ref kiltDrawn[i * 2], Local(midA), kiltHysteresis);
			var drawnMidB = SnapCloth(ref kiltDrawn[next * 2], Local(midB), kiltHysteresis);
			var drawnHemA = SnapCloth(ref kiltDrawn[i * 2 + 1], Local(hemA), kiltHysteresis);
			var drawnHemB = SnapCloth(ref kiltDrawn[next * 2 + 1], Local(hemB), kiltHysteresis);
			if (Settings.PixelSnap)
			{
				anchorA = anchorA with { X = MathF.Round(anchorA.X), Y = MathF.Round(anchorA.Y) };
				anchorB = anchorB with { X = MathF.Round(anchorB.X), Y = MathF.Round(anchorB.Y) };
			}
			// Lit by how squarely the panel faces the camera (+depth); the inside shows darker.
			var facing = (outwardA.Z + outwardB.Z) * 0.5f;
			var color = kilt.Fill * (float.Lerp(0.6f, 1, (facing + 1) * 0.5f) * (i % 2 == 0 ? 1 : KiltPleatShade));
			color.A = 255;
			var upperDepth = (anchorA.Z + anchorB.Z + midA.Z + midB.Z) * 0.25f / ppm;
			var lowerDepth = (midA.Z + midB.Z + hemA.Z + hemB.Z) * 0.25f / ppm;
			if (facing > KiltInsideFacing)
			{
				upperDepth = MathF.Max(upperDepth, legsFront);
				lowerDepth = MathF.Max(lowerDepth, legsFront);
			}
			garmentFaces.Add(new(GarmentShape.Quad, new(anchorA.X, anchorA.Y), new(anchorB.X, anchorB.Y), drawnMidB, drawnMidA,
				upperDepth, color));
			garmentFaces.Add(new(GarmentShape.Quad, drawnMidA, drawnMidB, drawnHemB, drawnHemA, lowerDepth, color));
		}
	}

	void AddTrouserFaces(CostumePiece trousers, Leg leg, Vector2 hemAt)
	{
		var ppm = Settings.PixelsPerMeter;
		var hip = projectedPose[leg.Hip];
		var knee = projectedPose[leg.Knee];
		var thigh = Settings.Bones.ThighSize * ppm * trousers.Width;
		var shin = Settings.Bones.ShinSize * ppm * trousers.Width;
		var upper = knee - hip;
		var lower = hemAt - knee;
		var bend = upper.LengthSquared() > 0.01f && lower.LengthSquared() > 0.01f
			? MathF.Abs(MathF.Sin(MathF.Atan2(upper.X * lower.Y - upper.Y * lower.X, Vector2.Dot(upper, lower)))) : 0;
		var kneeWidth = float.Lerp(thigh, shin, 0.5f) + thigh * TrouserKneeBulge * bend;
		var hemWidth = shin * (1 + trousers.Flare);
		// Just in front of the leg's own bones, so the far leg's trousers stay behind the near one.
		var depth = (jointDepth[leg.Hip] + jointDepth[leg.Knee] + jointDepth[leg.Ankle]) / 3 + 0.001f;
		var color = trousers.Fill * (leg.Hip == (backLeg == Side.Left ? leftLeg.Hip : rightLeg.Hip) ? 0.8f : 1f);
		color.A = 255;
		RigDraw.BoneQuad(hip, knee, thigh, kneeWidth, out var a, out var b, out var c, out var d);
		garmentFaces.Add(new(GarmentShape.Quad, a, b, c, d, depth, color));
		RigDraw.BoneQuad(knee, hemAt, kneeWidth, hemWidth, out a, out b, out c, out d);
		garmentFaces.Add(new(GarmentShape.Quad, a, b, c, d, depth, color));
		garmentFaces.Add(new(GarmentShape.Disc, knee, new(kneeWidth * 0.5f, 0), default, default, depth, color));
		garmentFaces.Add(new(GarmentShape.Disc, hip, new(thigh * 0.5f, 0), default, default, depth, color));
	}

	// Joins the legs at the top, so the trousers read as one garment rather than two sleeves.
	void AddTrouserSeat(CostumePiece trousers)
	{
		var ppm = Settings.PixelsPerMeter;
		const float SeatDrop = 0.35f; // along the thighs
		var left = projectedPose[leftLeg.Hip];
		var right = projectedPose[rightLeg.Hip];
		var leftLow = Vector2.Lerp(left, projectedPose[leftLeg.Knee], SeatDrop);
		var rightLow = Vector2.Lerp(right, projectedPose[rightLeg.Knee], SeatDrop);
		var raise = new Vector2(0, -Settings.Bones.ThighSize * ppm * 0.5f);
		var color = trousers.Fill * 0.9f;
		color.A = 255;
		garmentFaces.Add(new(GarmentShape.Quad, left + raise, right + raise, rightLow, leftLow,
			MathF.Min(jointDepth[leftLeg.Hip], jointDepth[rightLeg.Hip]) - 0.001f, color));
	}

	void AddGarmentsThrough(List<RigDrawItem> items, ref int index, float depth)
	{
		while (index < garmentFaces.Count && garmentFaces[index].Depth <= depth)
			items.Add(new(RigDrawKind.Garment, index++, false));
	}

	void DrawGarment(Batcher batcher, int index, Color? tint, float grow = 0)
	{
		var face = garmentFaces[index];
		var color = tint ?? face.Color;
		if (face.Shape == GarmentShape.Disc)
		{
			batcher.Circle(face.A, face.B.X + grow, 10, color);
			return;
		}
		if (grow <= 0)
		{
			batcher.Quad(face.A, face.B, face.C, face.D, color);
			return;
		}
		// Outline: the quad nudged a pixel each way, under the fills.
		foreach (var nudge in (ReadOnlySpan<Vector2>)[Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY])
			batcher.Quad(face.A + nudge * grow, face.B + nudge * grow, face.C + nudge * grow, face.D + nudge * grow, color);
	}

	Rect GarmentBounds(Rect bounds)
	{
		foreach (var face in garmentFaces)
			bounds = face.Shape == GarmentShape.Disc
				? bounds.Conflate(new Rect(face.A - Vector2.One * (face.B.X + 1), Vector2.One * (face.B.X + 1) * 2))
				: bounds.Conflate(face.A).Conflate(face.B).Conflate(face.C).Conflate(face.D);
		return bounds;
	}
}
