using System.Numerics;
using Foster.Framework;

namespace Myth;

sealed class RigPuppetRuff : Actor
{
	const int FoldCount = 12;

	public RigPuppet Owner = null!;

	readonly float[] compression = new float[FoldCount];
	readonly float[] compressionVelocity = new float[FoldCount];
	readonly float[] contacts = new float[FoldCount];
	readonly float[] targets = new float[FoldCount];
	HumanoidRigVisual.RuffPose pose;

	static float InnerRadiusX => 3.5f;
	static float InnerRadiusY => 1.5f;
	static float OuterRadiusX => 13;
	static float OuterRadiusY => 5;
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
		SyncOwner();
	}

	public override void Update()
	{
		if (Game.destroying.Contains(Owner))
		{
			Game.Destroy(this);
			return;
		}

		SyncOwner();
		UpdateFolds();
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
		var up = (pose.Head - pose.Neck).Normalized(Vector2.UnitY);
		var across = up.TurnLeft();
		var leftArm = new Line(pose.LeftUpperArm, pose.LeftLowerArm);
		var rightArm = new Line(pose.RightUpperArm, pose.RightLowerArm);
		var armRadius = MathF.Max(CollisionRadius, Owner.Rig.BicepSize * 0.45f);
		for (var i = 0; i < FoldCount; i++)
		{
			var angle = (i + 0.5f) * MathF.Tau / FoldCount;
			var fold = new Line(Ellipse(pose.Neck, across, up, angle, InnerRadiusX, InnerRadiusY),
				Ellipse(pose.Neck, across, up, angle, OuterRadiusX, OuterRadiusY));
			var distance = MathF.Sqrt(MathF.Min(fold.ClosestDistanceSquared(leftArm),
				fold.ClosestDistanceSquared(rightArm)));
			contacts[i] = Math.Clamp((armRadius - distance) / armRadius, 0, 1) * MaximumCompression;
		}

		for (var i = 0; i < FoldCount; i++)
			targets[i] = MathF.Max(contacts[i], MathF.Max(
				MathF.Max(contacts[Wrap(i - 1)], contacts[Wrap(i + 1)]) * 0.4f,
				MathF.Max(contacts[Wrap(i - 2)], contacts[Wrap(i + 2)]) * 0.15f));

		var dt = MathF.Min(Time.Delta, 1 / 30f);
		var damping = MathF.Exp(-FoldDamping * dt);
		for (var i = 0; i < FoldCount; i++)
		{
			compressionVelocity[i] += (targets[i] - compression[i]) * FoldSpring * dt;
			compressionVelocity[i] *= damping;
			compression[i] = Math.Clamp(compression[i] + compressionVelocity[i] * dt, -0.1f,
				MaximumCompression);
		}
	}

	internal void DrawBack(Batcher batcher, Color? tint) => Draw(batcher, tint, front: false);
	internal void DrawFront(Batcher batcher, Color? tint) => Draw(batcher, tint, front: true);

	void Draw(Batcher batcher, Color? tint, bool front)
	{
		if ((!Owner.RuffVisible && tint == null) || !Owner.Visible || !Owner.Ruff)
			return;
		pose = Owner.RuffPose;
		var up = (pose.Head - pose.Neck).Normalized(Vector2.UnitY);
		var across = up.TurnLeft();
		var step = MathF.Tau / FoldCount;
		for (var i = 0; i < FoldCount; i++)
		{
			var start = i * step;
			var end = start + step;
			if (MathF.Sin(start + step * 0.5f) < 0 != front)
				continue;
			var startCompression = (compression[Wrap(i - 1)] + compression[i]) * 0.5f;
			var endCompression = (compression[i] + compression[Wrap(i + 1)]) * 0.5f;
			var a = Ellipse(pose.Neck, across, up, start, InnerRadiusX, InnerRadiusY);
			var b = OuterPoint(pose.Neck, across, up, start, startCompression);
			var c = OuterPoint(pose.Neck, across, up, end, endCompression);
			var d = Ellipse(pose.Neck, across, up, end, InnerRadiusX, InnerRadiusY);
			var color = tint ?? (i % 2 == 0 ? Owner.Rig.NearColor :
				Color.Lerp(Owner.Rig.NearColor, Owner.Rig.FarColor, 0.28f));
			batcher.Quad(a, b, c, d, color);
			if (tint == null)
				batcher.QuadLine(a, b, c, d, 0.65f, Owner.Rig.OutlineColor);
		}
	}

	Vector2 OuterPoint(Vector2 center, Vector2 across, Vector2 up, float angle, float amount)
		=> Ellipse(center, across, up, angle,
			float.Lerp(OuterRadiusX, InnerRadiusX + 1, amount / MaximumCompression),
			float.Lerp(OuterRadiusY, InnerRadiusY + 0.5f, amount / MaximumCompression));

	static Vector2 Ellipse(Vector2 center, Vector2 across, Vector2 up, float angle, float radiusX, float radiusY)
		=> center + across * MathF.Cos(angle) * radiusX + up * MathF.Sin(angle) * radiusY;

	static int Wrap(int index) => (index + FoldCount) % FoldCount;

	public override Rect GetVisualBounds() => new(Position.X - 17, Position.Y - 48, 34, 34);
}
