using System.Numerics;
using Foster.Framework;

namespace Myth;

// The sculpted head of an actor that is one (FloatingHead, HeadBoss): its springy turn, face, jelly wobble, dents, and
// the walls it's pressed against, marched crisp with the owner's Squish as stretch. The owner steers it, calls Step and
// FindWalls each update, and forwards its render passes.
sealed class HeadSculpt(Actor owner, float extent = SculptedHead.DefaultExtent)
{
	public float Radius = 12;
	public ContentRef<HeadShapeContent> ShapeId;
	public Color Near = new(0xd9c2a7);
	public Color Eye; // the sockets' glow; default for none
	public Color Bruise = new(0x5a1a38);
	public Color Tint = Color.White; // multiplies color, emissive and the shadow mask's darkness
	public Expression Face;
	public Vector4 Morph; // see SculptedHead.hlsl's Morph
	public float Yaw, YawVelocity, Pitch, PitchVelocity, Roll, RollVelocity;
	public float Wobble, WobblePhase;
	public Vector2 ImpactRest; // the squash the impact spring settles back to
	public Vector2? GazeTarget; // a world point the pupils follow, over the face's own gaze

	static float WobbleDecay => 4.5f;
	static float WobbleRate => 30f;
	static float ContactReach => 1f; // radii: walls nearer than this press into the head
	static float PressSquash => 0.7f; // squash per radius pressed in
	static float ImpactSquash => 0.025f; // squash spring kick per px/sec of impact
	static float SquashStiffness => 260f;
	static float SquashDamping => 9f;
	static float DentRadius => 0.45f; // head radii
	static float DentDepth => 0.26f;
	static float BlinkTime => 0.12f;
	static float SwingFromAcceleration => 1 / 500f; // per px/sec², how far hanging rings lean away from a lurch
	static float SwingFromTurn => 0.08f; // per radian/sec of yaw
	static float SwingStiffness => 70f;
	static float SwingDamping => 2.5f;

	// Per axis (side walls, floor/ceiling): which way the nearest wall face or slope lies, or the last thing it hit, and
	// how far (px) from the center, MaxValue when out of reach.
	readonly (Vector2 Toward, float Gap)[] walls = new (Vector2, float)[2];
	Vector2 impact, impactVelocity; // extra squash along each axis, sprung, so impacts compress and rebound
	readonly Vector4[] dents = new Vector4[SculptedHead.MaxDents];
	int dentCount;
	SculptedHead? head;
	float blinkTimer = 2; // until the next blink, then (negative) how long it's been shut
	Vector2 swing, swingVelocity, lastVelocity; // hanging rings: (sideways, front-back) lean, sprung

	Game Game => owner.Game;
	Time Time => owner.Time;
	Vector2 Center => owner.Position;

	public void Step()
	{
		Wobble *= MathF.Exp(-WobbleDecay * Time.Delta);
		if (Wobble < 0.004f)
			Wobble = 0;
		WobblePhase += WobbleRate * Time.Delta;
		Spring.Step(ref impact, ref impactVelocity, ImpactRest, SquashStiffness, SquashDamping, Time.Delta);
		// Rings hang from the head like pendulums: a lurch leans them back against it, a turn flings them round.
		var acceleration = (owner.Velocity - lastVelocity) / MathF.Max(Time.Delta, 0.001f);
		lastVelocity = owner.Velocity;
		var lean = Vector2.Clamp(new(-acceleration.X * SwingFromAcceleration, YawVelocity * SwingFromTurn), new(-1.2f), new(1.2f));
		Spring.Step(ref swing, ref swingVelocity, lean, SwingStiffness, SwingDamping, Time.Delta);
		blinkTimer -= Time.Delta;
		if (blinkTimer < -BlinkTime)
			blinkTimer = Game.rng.Float(1.5f, 5);
	}

	// Springs the turn toward a target; stiffness 0 lets it coast.
	public void Look(float yaw, float pitch, float stiffness, float damping)
	{
		Spring.Step(ref Yaw, ref YawVelocity, yaw, stiffness, damping, Time.Delta);
		Spring.Step(ref Pitch, ref PitchVelocity, pitch, stiffness, damping, Time.Delta);
	}

	static readonly Point2[] Sides = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];

	public void FindWalls()
	{
		walls[0].Gap = walls[1].Gap = float.MaxValue;
		var half = owner.Hitbox.Rect.Width / 2;
		foreach (var side in Sides)
			for (var gap = half; gap < Radius * ContactReach; gap++)
			{
				var probe = owner.Hitbox.Rect + owner.Position + side * (gap - half + 1);
				if (!Game.OverlapsRoomCollision(owner, probe))
					continue;
				var axis = side.X != 0 ? 0 : 1;
				var wall = ((Vector2)side, (float)gap);
				// Slopes are floors and ceilings: the vertical probe presses it flat along the slope's own normal.
				if (Game.TryFindSurface(owner, probe, side, 0, out var hit) && hit.Slope is { } slope)
				{
					if (axis == 0)
						break;
					Game.CollisionRoomFor(owner)!.GetSlopeSurface(slope, out var left, out _);
					wall = (-hit.Normal, Vector2.Dot(Center - left, hit.Normal));
				}
				if (wall.Item2 < walls[axis].Gap)
					walls[axis] = wall;
				break;
			}
	}

	// A hit against a wall along an axis at a speed: squashes it, harder the faster.
	public void Impact(int axis, float speed)
	{
		impactVelocity[axis] += MathF.Abs(speed) * ImpactSquash;
		walls[axis].Toward = axis == 0 ? new(MathF.Sign(speed), 0) : new(0, MathF.Sign(speed));
	}

	// Carves a dent where a hit lands on the visible surface, in head-local space so it turns with the head.
	public void Dent(Vector2 worldPoint)
	{
		var across = (worldPoint - Center) / Radius * new Vector2(1, -1);
		if (!SculptedHead.Raycast(Assets.GetHeadShape(ShapeId), across, Yaw, Pitch, Roll, out var point, out var normal))
			return;
		if (dentCount == dents.Length)
			Array.Copy(dents, 1, dents, 0, --dentCount);
		dents[dentCount++] = new(point + normal * (DentRadius - DentDepth), DentRadius);
	}

	SculptedHead Current()
	{
		head ??= new(Game.Manager.GraphicsDevice, outline: false, extent);
		var far = RigDraw.Multiply(Near, new Color(0x5e4f90));
		var center = (Vector2)owner.RenderPosition;
		Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
		// Walls in reach press it flat and squash it; impacts squash it further, then it springs back.
		Span<Vector4> contacts = stackalloc Vector4[SculptedHead.MaxContacts];
		var contactCount = 0;
		for (var axis = 0; axis < 2; axis++)
		{
			var (toward, gap) = walls[axis];
			var wall = gap / Radius;
			var squash = MathF.Round(Math.Clamp(1 - MathF.Max(0, ContactReach - wall) * PressSquash - impact[axis], 0.55f, 1.3f) * 64) / 64;
			if (toward != Vector2.Zero && (wall < ContactReach || squash != 1))
				contacts[contactCount++] = new(toward, MathF.Min(wall, 10), squash);
		}
		var face = Face;
		if (blinkTimer < 0)
			face = face with { LidClose = 1 };
		// The pupils turn on toward the target past what the head has already turned.
		if (GazeTarget is { } target)
		{
			var toward = (target - Center) / (Radius * 4);
			face = face with { GazeX = Math.Clamp(toward.X - Yaw, -1, 1), GazeY = Math.Clamp(-toward.Y - Pitch, -1, 1) };
		}
		head.Render(Assets.GetHeadShape(ShapeId), Radius, Yaw, Pitch, Roll, face, Near, Color.Lerp(Near, far, 0.5f), far, Eye,
			BodyShadeMode.Banded, new(0, 0.5f, 1, 0), lights[..HumanoidRigVisual.GatherLights(Game, lights, center, Radius, inside: Radius)],
			center, dents.AsSpan(0, dentCount), Wobble, WobblePhase, Bruise, contacts[..contactCount], owner.Squish, Morph, swing);
		return head;
	}

	void Draw(Batcher batcher, Texture? texture, Color color)
	{
		if (texture != null)
			batcher.Image(texture, new Vector2(-head!.Size / 2), color);
	}

	public void Render(Batcher batcher) => Draw(batcher, Current().ColorTexture, Tint);
	public void RenderNormals(Batcher batcher) => Draw(batcher, Current().NormalTexture, Color.White);
	public void RenderEmissive(Batcher batcher) => Draw(batcher, Current().SpecularTexture, Tint);
	public void RenderShadow(Batcher batcher, Color color) => Draw(batcher, Current().MaskTexture, color);

	public Rect VisualBounds => Rect.CenteredAt(Center, new Vector2(Radius * extent * 2.2f));

	public void Dispose()
	{
		head?.Dispose();
		head = null;
	}
}
