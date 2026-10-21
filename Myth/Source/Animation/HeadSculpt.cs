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
	public Vector2 PatternBoost; // how much further the head shape's pattern spreads and glows
	public float VisualFPS; // how often it re-marches; 0 for every frame
	public Vector4 Jiggliness = Vector4.One; // scales how far its flesh lags behind a lurch or spin: jowls, ears, nose, brow

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
	static float BlinkLight => 0.35f; // of an eye's light left through a blink
	static float SwingFromAcceleration => 1 / 500f; // per px/sec², how far hanging rings lean away from a lurch
	static float SwingFromTurn => 0.08f; // per radian/sec of yaw
	static float SwingStiffness => 70f;
	static float SwingDamping => 2.5f;
	// Flesh that lags and bounces, each part its own (stiffness, damping), softened by the shape's FleshSoftness.
	static float JowlStiffness => 55f;
	static float JowlDamping => 3.5f;
	static float EarStiffness => 30f;
	static float EarDamping => 2.2f;
	static float NoseStiffness => 130f;
	static float NoseDamping => 6f;
	static float BrowStiffness => 210f;
	static float BrowDamping => 9f;
	static float JiggleInertia => 0.1f; // how much of a lurch or spin (radii/sec at the part) the flesh lags behind
	static float JiggleFromHit => 1.5f; // radii/sec a hit shoves the flesh nearest it
	static float JiggleFromFace => 0.7f; // how much of an expression's move the brow, jowls and cheeks lag behind

	// Per axis (side walls, floor/ceiling): which way the nearest wall face or slope lies, or the last thing it hit, and
	// how far (px) from the center, MaxValue when out of reach.
	readonly (Vector2 Toward, float Gap)[] walls = new (Vector2, float)[2];
	Vector2 impact, impactVelocity; // extra squash along each axis, sprung, so impacts compress and rebound
	readonly Vector4[] dents = new Vector4[SculptedHead.MaxDents];
	int dentCount;
	SculptedHead? head;
	float blinkTimer = 2; // until the next blink, then (negative) how long it's been shut
	Vector2 swing, swingVelocity, lastVelocity; // hanging rings: (sideways, front-back) lean, sprung
	// Per SculptedHead.FleshAnchors part, head-local: how far the flesh is dragged, sprung back to rest.
	readonly Vector3[] jiggle = new Vector3[SculptedHead.MaxJiggles], jiggleVelocity = new Vector3[SculptedHead.MaxJiggles];
	Vector3 lastSpin;
	Expression lastFace;
	float lastPuff;
	Vector3 wobbleAt; // head-local point the last hit's wobble spreads from
	float visualTimer;
	bool visualDue = true;
	// Hits waiting on the head's last render to come back from the GPU: where they landed on its texture, dent scale.
	readonly List<(Vector2 Pixel, float Scale)> pendingDents = [];
	TextureDownload<Half> positionDownload;
	TextureDownload<Color> normalDownload;
	Half[] positions = [];
	Color[] normals = [];
	int downloadSize;

	Game Game => owner.Game;
	Time Time => owner.Time;
	Vector2 Center => owner.Position;

	// Where an eye (side -1 or 1) sits in the world, and how much light gets out: 0 shut, 1 open, past that wide, dimmed
	// through a blink.
	public (Vector2 At, float Open) EyeLight(float side)
	{
		var eye = SculptedHead.EyeInView(Assets.GetHeadShape(ShapeId), side, Yaw, Pitch, Roll, Morph.Z);
		var open = Math.Clamp(1 - Face.LidClose, 0, 1.3f) * (blinkTimer < 0 ? BlinkLight : 1);
		return (Center + new Vector2(eye.X, -eye.Y) * owner.Squish * Radius, open);
	}

	// Where anvil piece i joins the anvil, in the world.
	public Vector2 AnvilPieceAt(int i)
	{
		var at = SculptedHead.AnvilInView(Assets.GetHeadShape(ShapeId), i, Yaw, Pitch, Roll);
		return Center + new Vector2(at.X, -at.Y) * owner.Squish * Radius;
	}

	public void Step()
	{
		if (pendingDents.Count > 0)
			LandDents();
		Wobble *= MathF.Exp(-WobbleDecay * Time.Delta);
		if (Wobble < 0.004f)
			(Wobble, wobbleAt) = (0, Vector3.Zero);
		WobblePhase += WobbleRate * Time.Delta;
		Spring.Step(ref impact, ref impactVelocity, ImpactRest, SquashStiffness, SquashDamping, Time.Delta);
		// Rings hang from the head like pendulums: a lurch leans them back against it, a turn flings them round.
		var acceleration = (owner.Velocity - lastVelocity) / MathF.Max(Time.Delta, 0.001f);
		lastVelocity = owner.Velocity;
		var lean = Vector2.Clamp(new(-acceleration.X * SwingFromAcceleration, YawVelocity * SwingFromTurn), new(-1.2f), new(1.2f));
		Spring.Step(ref swing, ref swingVelocity, lean, SwingStiffness, SwingDamping, Time.Delta);
		Jiggle(acceleration * Time.Delta);
		visualTimer += Time.Delta;
		if (VisualFPS <= 0 || visualTimer >= 1 / VisualFPS)
			(visualTimer, visualDue) = (VisualFPS <= 0 ? 0 : visualTimer % (1 / VisualFPS), true);
		blinkTimer -= Time.Delta;
		if (blinkTimer < -BlinkTime)
			blinkTimer = Game.rng.Float(1.5f, 5);
	}

	// The flesh lags behind a lurch (px/sec this step) and the head's spin, and behind the brow, mouth and cheeks as
	// they move, then springs back.
	void Jiggle(Vector2 lurch)
	{
		var shape = Assets.GetHeadShape(ShapeId);
		var softness = shape.FleshSoftness;
		Span<Vector4> anchors = stackalloc Vector4[SculptedHead.MaxJiggles];
		SculptedHead.FleshAnchors(shape, anchors);
		var (_, toHead) = SculptedHead.Rotations(Yaw, Pitch, Roll);
		var shove = Vector3.TransformNormal(new(lurch.X, -lurch.Y, 0), toHead) / Radius;
		var spin = new Vector3(PitchVelocity, YawVelocity, -RollVelocity);
		var face = Face;
		jiggle[5].Y -= ((face.BrowRaise - lastFace.BrowRaise) * 0.08f - (face.BrowTilt - lastFace.BrowTilt) * 0.14f) * JiggleFromFace;
		for (var side = 0; side < 2; side++)
			jiggle[side] += new Vector3((side * 2 - 1) * -(Morph.Y - lastPuff) * 0.2f, (face.MouthOpen - lastFace.MouthOpen) * 0.1f, 0) * JiggleFromFace;
		for (var i = 0; i < jiggle.Length; i++)
		{
			if (anchors[i].W <= 0 || softness <= 0)
			{
				(jiggle[i], jiggleVelocity[i]) = (Vector3.Zero, Vector3.Zero);
				continue;
			}
			var (stiffness, damping, jiggliness) = i switch
			{
				< 2 => (JowlStiffness, JowlDamping, Jiggliness.X),
				< 4 => (EarStiffness, EarDamping, Jiggliness.Y),
				4 => (NoseStiffness, NoseDamping, Jiggliness.Z),
				_ => (BrowStiffness, BrowDamping, Jiggliness.W),
			};
			jiggleVelocity[i] -= (shove + Vector3.Cross(spin - lastSpin, new(anchors[i].X, anchors[i].Y, anchors[i].Z))) * JiggleInertia * jiggliness;
			Spring.Step(ref jiggle[i], ref jiggleVelocity[i], Vector3.Zero, stiffness / (softness * softness), damping / softness, Time.Delta);
			if (jiggle[i].LengthSquared() < 1e-5f && jiggleVelocity[i].LengthSquared() < 1e-4f)
				(jiggle[i], jiggleVelocity[i]) = (Vector3.Zero, Vector3.Zero);
		}
		(lastSpin, lastFace, lastPuff) = (spin, face, Morph.Y);
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

	// Carves a dent where a hit lands on the head as last drawn, in head-local space so it turns with the head; scale
	// sizes it. It lands once that render reads back from the GPU: the same frame on desktop, a frame or so in a browser.
	public void Dent(Vector2 worldPoint, float scale = 1)
	{
		if (head?.PositionTexture is not { } positionTexture)
			return;
		if (pendingDents.Count == 0)
		{
			downloadSize = head.Size;
			positionDownload = positionTexture.RequestDownload(positionDownload);
			normalDownload = head.RawNormalTexture!.RequestDownload(normalDownload);
		}
		pendingDents.Add((worldPoint - (Vector2)owner.RenderPosition + new Vector2(downloadSize / 2), scale));
	}

	void LandDents()
	{
		var pixels = downloadSize * downloadSize;
		if (normals.Length != pixels)
			(positions, normals) = (new Half[pixels * 4], new Color[pixels]);
		if (!positionDownload.TryRead(positions) || !normalDownload.TryRead(normals))
			return;
		var toHead = SculptedHead.Rotations(Yaw, Pitch, Roll).ToHead;
		var center = new Vector2(downloadSize / 2);
		var bounds = new RectInt(0, 0, downloadSize, downloadSize);
		foreach (var (pixel, scale) in pendingDents)
			// Falls back toward the center when it misses the silhouette.
			for (var (attempt, offset) = (0, pixel - center); attempt <= 8; attempt++, offset *= 0.8f)
			{
				var at = (center + offset).FloorToPoint2();
				var i = at.Y * downloadSize + at.X;
				if (!bounds.Contains(at) || (float)positions[i * 4 + 3] < 0.5f)
					continue;
				var point = new Vector3((float)positions[i * 4], (float)positions[i * 4 + 1], (float)positions[i * 4 + 2]);
				var screen = normals[i].ToVector3() * 2 - Vector3.One;
				Dent(point, Vector3.Normalize(Vector3.TransformNormal(screen with { Y = -screen.Y }, toHead)), scale);
				break;
			}
		pendingDents.Clear();
	}

	void Dent(Vector3 point, Vector3 normal, float scale)
	{
		// The wobble spreads from the hit, and the flesh nearest it is shoved in.
		wobbleAt = point;
		Span<Vector4> anchors = stackalloc Vector4[SculptedHead.MaxJiggles];
		SculptedHead.FleshAnchors(Assets.GetHeadShape(ShapeId), anchors);
		for (var i = 0; i < jiggle.Length; i++)
			jiggleVelocity[i] -= normal * JiggleFromHit *
				Math.Clamp(1 - Vector3.Distance(point, new(anchors[i].X, anchors[i].Y, anchors[i].Z)) / (anchors[i].W + 0.5f), 0, 1);
		if (dentCount == dents.Length)
			Array.Copy(dents, 1, dents, 0, --dentCount);
		dents[dentCount++] = new(point + normal * (DentRadius - DentDepth) * scale, DentRadius * scale);
	}

	SculptedHead Current()
	{
		if (VisualFPS > 0 && head?.ColorTexture != null && !visualDue)
			return head;
		visualDue = false;
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
			center, dents.AsSpan(0, dentCount), Wobble, WobblePhase, Bruise, contacts[..contactCount], owner.Squish, Morph, swing, PatternBoost,
			jiggle, wobbleAt);
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
