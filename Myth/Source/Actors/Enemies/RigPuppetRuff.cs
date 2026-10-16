using System.Numerics;
using Foster.Framework;

namespace Myth;

sealed class RigPuppetRuff : Actor, IHumanoidRigAccessoryVisual
{
	const int FoldCount = 12;
	const int FacesPerFold = 4;

	readonly record struct Face(Vector2 A, Vector2 B, Vector2 C, Vector2 D, float Depth, Color Color);

	public RigPuppet Owner = null!;
	public float SpinSpeed;

	readonly float[] compression = new float[FoldCount];
	readonly float[] compressionVelocity = new float[FoldCount];
	readonly float[] contacts = new float[FoldCount];
	readonly float[] targets = new float[FoldCount];
	readonly Face[] faces = new Face[FoldCount * FacesPerFold];
	HumanoidRigVisual.RuffPose pose;
	float spin;
	float breathPhase;

	static float InnerRadius => 3.5f;
	static float OuterRadius => 13;
	static float Thickness => 2.5f;
	static float BreathAmount => 0.055f;
	static float BreathSpeed => 1.35f;
	static float CollisionRadius => 2;
	static float MaximumCompression => 0.72f;
	static float FoldSpring => 110;
	static float FoldDamping => 13;

	public RigPuppetRuff()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		breathPhase = Position.X * 0.173f + Position.Y * 0.317f;
		SyncOwner();
		BuildFaces();
	}

	public override void Update()
	{
		if (Game.destroying.Contains(Owner))
		{
			Game.Destroy(this);
			return;
		}

		SyncOwner();
		spin = MathF.IEEERemainder(spin + SpinSpeed * Time.Delta, MathF.Tau);
		UpdateFolds();
		BuildFaces();
	}

	void SyncOwner()
	{
		Position = Owner.Position;
		KeepOnRoomTransition = Owner.KeepOnRoomTransition;
		pose = Owner.RuffPose;
	}

	void UpdateFolds()
	{
		Array.Clear(contacts);
		RuffAxes(out _, out var side, out var forward);
		var scale = Owner.Rig.PixelsPerMeter;
		var innerRadius = InnerRadius / scale;
		var outerRadius = OuterRadius / scale;
		var armRadius = MathF.Max(CollisionRadius / scale, Owner.Rig.Bones.BicepSize * 0.45f);
		for (var i = 0; i < FoldCount; i++)
		{
			var angle = (i + 0.5f) * MathF.Tau / FoldCount + spin;
			var from = RuffPoint(pose.Neck, side, forward, angle, innerRadius);
			var to = RuffPoint(pose.Neck, side, forward, angle, outerRadius);
			var distance = MathF.Sqrt(MathF.Min(
				SegmentDistanceSquared(from, to, pose.LeftUpperArm, pose.LeftLowerArm),
				SegmentDistanceSquared(from, to, pose.RightUpperArm, pose.RightLowerArm)));
			contacts[i] = Math.Clamp((armRadius - distance) / armRadius, 0, 1) * MaximumCompression;
		}

		for (var i = 0; i < FoldCount; i++)
			targets[i] = MathF.Max(contacts[i], MathF.Max(
				MathF.Max(contacts[Wrap(i - 1)], contacts[Wrap(i + 1)]) * 0.4f,
				MathF.Max(contacts[Wrap(i - 2)], contacts[Wrap(i + 2)]) * 0.15f));

		var dt = MathF.Min(Time.Delta, 1 / 30f);
		for (var i = 0; i < FoldCount; i++)
		{
			Spring.Step(ref compression[i], ref compressionVelocity[i], targets[i],
				FoldSpring, FoldDamping, dt);
			compression[i] = Math.Clamp(compression[i], -0.1f, MaximumCompression);
		}
	}

	int IHumanoidRigAccessoryVisual.DrawItemCount => faces.Length;
	float IHumanoidRigAccessoryVisual.GetDrawItemDepth(int index) => faces[index].Depth;
	void IHumanoidRigAccessoryVisual.DrawItem(Batcher batcher, int index, Color? tint)
	{
		var face = faces[index];
		batcher.Quad(face.A, face.B, face.C, face.D, tint ?? face.Color);
		if (tint == null)
		{
			DrawEdge(batcher, face.A, face.B);
			DrawEdge(batcher, face.B, face.C);
			DrawEdge(batcher, face.C, face.D);
			DrawEdge(batcher, face.D, face.A);
		}
	}

	void BuildFaces()
	{
		RuffAxes(out var up, out var side, out var forward);
		var scale = Owner.Rig.PixelsPerMeter;
		var innerRadius = InnerRadius / scale;
		var halfThickness = up * (Thickness / scale * 0.5f);
		var step = MathF.Tau / FoldCount;
		var breath = 1 + MathF.Sin(Time.SecondsF * BreathSpeed + breathPhase) * BreathAmount;
		var at = 0;
		for (var i = 0; i < FoldCount; i++)
		{
			var start = i * step + spin;
			var end = start + step;
			var startCompression = (compression[Wrap(i - 1)] + compression[i]) * 0.5f;
			var endCompression = (compression[i] + compression[Wrap(i + 1)]) * 0.5f;
			var innerStart = RuffPoint(pose.Neck, side, forward, start, innerRadius);
			var outerStart = OuterPoint(pose.Neck, side, forward, start, startCompression, breath, scale);
			var outerEnd = OuterPoint(pose.Neck, side, forward, end, endCompression, breath, scale);
			var innerEnd = RuffPoint(pose.Neck, side, forward, end, innerRadius);
			var color = i % 2 == 0 ? Owner.Rig.Bones.NearColor :
				Color.Lerp(Owner.Rig.Bones.NearColor, Owner.Rig.Bones.FarColor, 0.28f);
			var underside = Color.Lerp(color, Owner.Rig.Bones.FarColor, 0.45f);
			var outerWall = Color.Lerp(color, Owner.Rig.Bones.FarColor, 0.65f);
			var innerWall = Color.Lerp(Owner.Rig.Bones.FarColor, Owner.Rig.OutlineColor, 0.45f);
			faces[at++] = ProjectFace(innerStart + halfThickness, outerStart + halfThickness,
				outerEnd + halfThickness, innerEnd + halfThickness, color);
			faces[at++] = ProjectFace(innerEnd - halfThickness, outerEnd - halfThickness,
				outerStart - halfThickness, innerStart - halfThickness, underside);
			faces[at++] = ProjectFace(outerStart - halfThickness, outerStart + halfThickness,
				outerEnd + halfThickness, outerEnd - halfThickness, outerWall);
			faces[at++] = ProjectFace(innerEnd - halfThickness, innerEnd + halfThickness,
				innerStart + halfThickness, innerStart - halfThickness, innerWall);
		}
		SortFaces();
	}

	Face ProjectFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
	{
		var pa = pose.Projection.Project(a, out var da);
		var pb = pose.Projection.Project(b, out var db);
		var pc = pose.Projection.Project(c, out var dc);
		var pd = pose.Projection.Project(d, out var dd);
		return new(pa, pb, pc, pd, (da + db + dc + dd) * 0.25f, color);
	}

	void SortFaces()
	{
		for (var i = 1; i < faces.Length; i++)
		{
			var face = faces[i];
			var at = i;
			while (at > 0 && faces[at - 1].Depth > face.Depth)
			{
				faces[at] = faces[at - 1];
				at--;
			}
			faces[at] = face;
		}
	}

	void DrawEdge(Batcher batcher, Vector2 from, Vector2 to)
	{
		if (Vector2.DistanceSquared(from, to) > 0.01f)
			batcher.Line(from, to, 0.65f, Owner.Rig.OutlineColor);
	}

	void RuffAxes(out Vector3 up, out Vector3 side, out Vector3 forward)
	{
		up = Normalize(pose.Head - pose.Neck, Vector3.UnitY);
		var shoulders = pose.RightUpperArm - pose.LeftUpperArm;
		side = Normalize(shoulders - up * Vector3.Dot(shoulders, up), Vector3.UnitX);
		forward = Normalize(Vector3.Cross(up, side), Vector3.UnitZ);
	}

	public Vector2 ProjectOrbitPoint(float angle, float radius)
	{
		RuffAxes(out _, out var side, out var forward);
		return pose.Projection.Project(RuffPoint(pose.Neck, side, forward, angle + spin,
			radius / Owner.Rig.PixelsPerMeter), out _);
	}

	static Vector3 Normalize(Vector3 value, Vector3 fallback)
		=> value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;

	static Vector3 OuterPoint(Vector3 center, Vector3 side, Vector3 forward, float angle, float amount,
		float breath, float scale)
		=> RuffPoint(center, side, forward, angle,
			(InnerRadius + (float.Lerp(OuterRadius, InnerRadius + 1, amount / MaximumCompression) - InnerRadius) * breath) / scale);

	static Vector3 RuffPoint(Vector3 center, Vector3 side, Vector3 forward, float angle, float radius)
		=> center + (side * MathF.Cos(angle) + forward * MathF.Sin(angle)) * radius;

	static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
	{
		var d1 = q1 - p1;
		var d2 = q2 - p2;
		var r = p1 - p2;
		var a = Vector3.Dot(d1, d1);
		var e = Vector3.Dot(d2, d2);
		var f = Vector3.Dot(d2, r);
		float s;
		float t;
		if (a <= 0.000001f && e <= 0.000001f)
			return Vector3.DistanceSquared(p1, p2);
		if (a <= 0.000001f)
		{
			s = 0;
			t = Math.Clamp(f / e, 0, 1);
		}
		else
		{
			var c = Vector3.Dot(d1, r);
			if (e <= 0.000001f)
			{
				t = 0;
				s = Math.Clamp(-c / a, 0, 1);
			}
			else
			{
				var b = Vector3.Dot(d1, d2);
				var denominator = a * e - b * b;
				s = denominator != 0 ? Math.Clamp((b * f - c * e) / denominator, 0, 1) : 0;
				t = (b * s + f) / e;
				if (t < 0)
				{
					t = 0;
					s = Math.Clamp(-c / a, 0, 1);
				}
				else if (t > 1)
				{
					t = 1;
					s = Math.Clamp((b - c) / a, 0, 1);
				}
			}
		}
		return Vector3.DistanceSquared(p1 + d1 * s, p2 + d2 * t);
	}

	static int Wrap(int index) => (index + FoldCount) % FoldCount;

	public override Rect GetVisualBounds() => new(Position.X - 17, Position.Y - 48, 34, 34);
}
