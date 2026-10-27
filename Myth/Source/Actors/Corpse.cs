using System.Numerics;
using Foster.Framework;

namespace Myth;

// What an enemy leaves behind: its last sprite frame, knocked away from the killing blow, sinking
// back a layer and washing toward the room's backdrop as it settles. Collides with the room only,
// but wind, water, lava and sword swings still move it.
//
// A kill can split the body into pieces. Each cut is a jagged polyline running edge to edge along
// the blow, rasterized onto the sprite's pixel grid so the edges stay crisp; a piece is the pixel
// runs on its side of every cut, and flies off as its own Corpse.
//
// With Tumble on, pieces spin about their center. On the ground the spin is pulled toward a roll
// matching the slide, so friction stops both together, then the piece eases onto its nearest
// quarter turn: it rests with crisp, unrotated pixels and a hitbox turned to match.
public class Corpse : Actor, IHitByHitbox
{
	static float Gravity => 300f;
	static float LiquidGravity => 40f;
	static float LiquidDrag => 3f;
	static float GroundFriction => 300f;
	static float Bounce => 0.2f;
	static float MinBounceSpeed => 40f;
	static float KnockSpeed => 25f;
	static float PopSpeed => 40f;
	static float SplitSpeed => 30f;
	static float NudgeSpeed => 70f;
	static float CutSpread => 0.35f; // radians a cut may stray from the blow's direction
	static float Jag => 2.5f;        // how far each polyline vertex strays from the straight cut, in px
	static float JagStep => 2f;      // px between polyline vertices
	static float FadeDuration => 0.5f;
	static float FadeTint => 0.45f; // how far the sprite multiplies toward the backdrop's depth color
	static float FadeWash => 0.3f;  // how much of the sprite's detail flattens into that color
	static int MaxCorpses => 64;
	static bool Tumble => true;
	static float MinSpin => 4f;      // radians/s
	static float MaxSpin => 12f;
	static float RollGrip => 30f;    // how fast ground contact turns spin into a roll, radians/s²
	static float TopplePull => 40f;  // radians/s² gravity swings a settling piece with
	static float TipEase => 1f;      // scales the tipping angle; below 1 pieces fall flat more readily
	static float SettleSlide => 4f;  // px/s of slide below which a piece settles

	static readonly List<Corpse> existing = [];

	Sprite.Frame frame;
	List<RectInt> rects = []; // this piece's pixels, in sprite-local space
	Point2 pieceOrigin;       // the sprite-local point that sits at Position
	RectInt unturnedHitbox;
	float radius;
	float angle;
	float spin;
	int quarter;              // quarter turns the hitbox is rotated by
	float fade;
	string hitSoundType = "";

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => true;

	internal static void Spawn(Actor from, HitInfo? hit = null, int pieces = 1)
	{
		if (from.CurrentSpriteFrame is not { } frame)
			return;

		var game = from.Game;
		existing.Clear();
		foreach (var actor in game.Actors)
			if (actor is Corpse corpse && !game.destroying.Contains(corpse))
				existing.Add(corpse);
		existing.Sort((a, b) => a.CreationOrder.CompareTo(b.CreationOrder));
		for (var i = 0; i < existing.Count + pieces - MaxCorpses && i < existing.Count; i++)
			game.Destroy(existing[i]);

		var sub = frame.Subtexture;
		var drawn = new RectInt((-sub.Frame.Position).RoundToPoint2(), sub.Source.Size.RoundToPoint2());
		var flip = new Point2((int)from.Facing, from.FlipY ? -1 : 1);
		var atPosition = from.Origin - from.Shift; // the sprite-local point at from.Position
		var blow = hit is { } h ? ((from.Hitbox.Rect + from.Position).CenterF - h.AttackBox.Rect.CenterF) * flip : Vector2.UnitX;
		var away = hit is { } by ? MathF.Sign(from.Position.X - by.By.Position.X) : 0;
		var launch = from.Velocity * 0.5f + new Vector2(away * KnockSpeed, -PopSpeed);

		foreach (var rects in Cut(drawn, blow.Normalized(Vector2.UnitX), pieces, game.rng))
		{
			var bounds = Bounds(rects);
			var origin = Tumble ? bounds.Center : bounds.BottomCenter;
			var spread = pieces > 1 ? ((bounds.CenterF - drawn.CenterF) * flip).Normalized() * SplitSpeed : Vector2.Zero;
			game.Create<Corpse>(from.Position + ((origin - atPosition) * flip).RoundToPoint2(), corpse =>
			{
				corpse.frame = frame;
				corpse.rects = rects;
				corpse.pieceOrigin = origin;
				corpse.Facing = from.Facing;
				corpse.FlipY = from.FlipY;
				corpse.Squish = from.Squish;
				corpse.unturnedHitbox = (bounds - origin).Scale(flip);
				corpse.Hitbox = new(corpse.unturnedHitbox);
				corpse.radius = ((Vector2)bounds.Size).Length() / 2;
				corpse.spin = Tumble ? (away != 0 ? away : (int)game.rng.Sign()) * game.rng.Float(MinSpin, MaxSpin) : 0;
				corpse.Depth = from.Depth;
				corpse.hitSoundType = from.HitSoundType;
				corpse.Velocity = launch + spread;
			});
		}
	}

	// Splits the drawn rect into `count` pieces, each cut taken through the largest piece so far.
	static List<List<RectInt>> Cut(RectInt drawn, Vector2 blow, int count, Rng rng)
	{
		var rows = new List<RectInt>();
		for (var y = drawn.Top; y < drawn.Bottom; y++)
			rows.Add(new(drawn.X, y, drawn.Width, 1));

		var pieces = new List<List<RectInt>> { rows };
		while (pieces.Count < count)
		{
			var largest = pieces.MaxBy(Area)!;
			var cut = Split(largest, Calc.AngleToVector(blow.Angle() + rng.Float(-CutSpread, CutSpread)), rng);
			if (cut.Count == 0)
				break;
			pieces.Add(cut);
		}

		for (var i = 0; i < pieces.Count; i++)
			pieces[i] = MergeRows(pieces[i]);
		return pieces;
	}

	// Moves the rows' pixels across a jagged line through their centroid into a new piece.
	static List<RectInt> Split(List<RectInt> rows, Vector2 along, Rng rng)
	{
		var sum = Vector2.Zero;
		foreach (var row in rows)
			sum += row.CenterF * row.Width;
		var center = sum / Area(rows);
		var across = along.TurnRight();
		var extent = ((Vector2)Bounds(rows).Size).Length() / 2 + JagStep;

		Span<float> jag = stackalloc float[(int)(extent * 2 / JagStep) + 2];
		foreach (ref var vertex in jag)
			vertex = rng.Float(-Jag, Jag);

		var kept = new List<RectInt>();
		var cut = new List<RectInt>();
		foreach (var row in rows)
		{
			var runStart = row.X;
			var runCut = false;
			for (var x = row.X; x <= row.Right; x++)
			{
				var isCut = false;
				if (x < row.Right)
				{
					var p = new Vector2(x + 0.5f, row.Y + 0.5f) - center;
					var t = (Vector2.Dot(p, along) + extent) / JagStep;
					var i = Math.Clamp((int)t, 0, jag.Length - 2);
					isCut = Vector2.Dot(p, across) > float.Lerp(jag[i], jag[i + 1], t - i);
				}
				if (x == row.Right || (x > runStart && isCut != runCut))
				{
					(runCut ? cut : kept).Add(new(runStart, row.Y, x - runStart, 1));
					runStart = x;
				}
				runCut = isCut;
			}
		}

		if (kept.Count == 0 || cut.Count == 0)
			return [];
		rows.Clear();
		rows.AddRange(kept);
		return cut;
	}

	// Stacks identical runs on consecutive rows into one rect, so an uncut body draws as a single quad.
	static List<RectInt> MergeRows(List<RectInt> rows)
	{
		var merged = new List<RectInt>();
		foreach (var row in rows)
		{
			var i = merged.FindLastIndex(m => m.Bottom == row.Y && m.X == row.X && m.Width == row.Width);
			if (i >= 0)
				merged[i] = merged[i] with { Height = merged[i].Height + 1 };
			else
				merged.Add(row);
		}
		return merged;
	}

	static int Area(List<RectInt> rects)
	{
		var area = 0;
		foreach (var rect in rects)
			area += rect.Width * rect.Height;
		return area;
	}

	static RectInt Bounds(List<RectInt> rects)
	{
		var bounds = rects[0];
		foreach (var rect in rects)
			bounds = bounds.Conflate(rect);
		return bounds;
	}

	// Enemies that fly through walls (Mosquito) can die with a piece inside one.
	public override void Added() => TryUnstick(-Vector2.UnitY);

	public override void Update()
	{
		UpdateLiquidState();
		if (InLava)
		{
			Game.PlayAudioEvent("liquid/steam", "Size", 4, Position);
			Game.Destroy(this);
			return;
		}

		if (InLiquid)
		{
			Velocity = Calc.Approach(Velocity, Vector2.Zero, Velocity.Length() * LiquidDrag * Time.Delta);
			Velocity.Y += LiquidGravity * Time.Delta;
		}
		else
			Velocity.Y += Gravity * Time.Delta;

		var grounded = Grounded();
		if (grounded)
			Velocity.X = Calc.Approach(Velocity.X, 0, GroundFriction * Time.Delta);
		if (Tumble)
			UpdateTumble(grounded);

		fade = Calc.Approach(fade, 1, Time.Delta / FadeDuration);
		Depth = (int)MathF.Round(float.Lerp(Depths.Enemy, Depths.Ambient, fade));
		base.Update();
	}

	void UpdateTumble(bool grounded)
	{
		const float QuarterTurn = MathF.PI / 2;
		if (grounded && MathF.Abs(Velocity.X) >= SettleSlide)
			spin = Calc.Approach(spin, Velocity.X / radius, RollGrip * Time.Delta);
		else if (grounded)
		{
			// Rest on the nearest face, unless the lean has swung the center of mass out past the
			// corner of the pixels it stands on -- then topple onto the next face over.
			var rest = MathF.Round(angle / QuarterTurn);
			var lean = angle - rest * QuarterTurn;
			var (fromLeft, fromRight) = Footing((int)rest);
			var target = rest;
			if (lean > MathF.Atan2(-fromRight.X, -fromRight.Y) * TipEase)
				target++;
			else if (-lean > MathF.Atan2(fromLeft.X, -fromLeft.Y) * TipEase)
				target--;
			target *= QuarterTurn;

			var toward = MathF.Sign(target - angle);
			spin += toward * TopplePull * Time.Delta;
			if (toward == 0 || MathF.Sign(target - angle - spin * Time.Delta) != toward)
			{
				if (MathF.Abs(spin) > 2)
					Squish = new Vector2(1.15f, 0.85f);
				angle = target;
				spin = 0;
			}
		}
		angle += spin * Time.Delta;

		var turned = ((int)MathF.Round(angle / QuarterTurn) % 4 + 4) % 4;
		if (turned == quarter)
			return;
		var previous = Hitbox;
		Hitbox = new(unturnedHitbox.RotateRight(turned));
		if (TryUnstick(-Vector2.UnitY))
			quarter = turned;
		else
			Hitbox = previous;
	}

	// The piece's pixel centroid relative to the outer corners of its bottom row of pixels, once
	// turned `turns` quarter turns: the edges it would pivot on to topple left or right.
	(Vector2 FromLeft, Vector2 FromRight) Footing(int turns)
	{
		var flip = new Point2((int)Facing, FlipY ? -1 : 1);
		turns = (turns % 4 + 4) % 4;
		var sum = Vector2.Zero;
		var area = 0;
		var bottom = int.MinValue;
		var left = 0;
		var right = 0;
		foreach (var rect in rects)
		{
			var turned = (rect - pieceOrigin).Scale(flip).RotateRight(turns);
			sum += turned.CenterF * turned.Area;
			area += turned.Area;
			if (turned.Bottom > bottom)
				(bottom, left, right) = (turned.Bottom, turned.Left, turned.Right);
			else if (turned.Bottom == bottom)
				(left, right) = (Math.Min(left, turned.Left), Math.Max(right, turned.Right));
		}
		var centroid = sum / area;
		return (centroid - new Vector2(left, bottom), centroid - new Vector2(right, bottom));
	}

	public override void OnCollideX()
	{
		var speed = Velocity.X;
		StopX();
		Velocity.X = -speed * Bounce;
		spin *= -Bounce;
	}

	public override void OnCollideY()
	{
		var speed = Velocity.Y;
		StopY();
		if (speed > MinBounceSpeed)
		{
			Velocity.Y = -speed * Bounce;
			Squish = new Vector2(1.25f, 0.8f);
		}
	}

	public bool HitByHitbox(RectInt hitbox)
	{
		if (!Hitbox.Overlaps(hitbox - Position))
			return false;
		var away = MathF.Sign(Position.X - hitbox.Center.X);
		Velocity += new Vector2(away * NudgeSpeed, -NudgeSpeed);
		Squish = new Vector2(0.8f, 1.2f);
		if (hitSoundType != "")
			AudioEvent("player/sword_hit", "hit_destination", hitSoundType);
		return true;
	}

	public override Rect GetVisualBounds() => (Hitbox.Rect + Position).Inflate((int)MathF.Ceiling(radius) + 2);

	void DrawPiece(Batcher batcher, in Subtexture sub, Color color)
	{
		if (sub.Texture is not { } texture)
			return;
		var atlasSize = new Vector2(texture.Width, texture.Height);
		var toAtlas = sub.Source.Position + sub.Frame.Position; // sprite-local pixel -> atlas pixel
		// Flip in sprite space, turn, then squish in world space so a landing squash stays vertical.
		batcher.PushMatrix(Matrix3x2.CreateScale((int)Facing, FlipY ? -1 : 1) * Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateScale(Squish));
		foreach (var rect in rects)
		{
			Rect local = rect - pieceOrigin;
			var uv = rect + toAtlas;
			batcher.Quad(texture,
				local.TopLeft, local.TopRight, local.BottomRight, local.BottomLeft,
				uv.TopLeft / atlasSize, uv.TopRight / atlasSize, uv.BottomRight / atlasSize, uv.BottomLeft / atlasSize,
				color);
		}
		batcher.PopMatrix();
	}

	public override void Render(Batcher batcher)
	{
		var depthColor = Pen.DepthColorFor(Room?.BackdropColor ?? Myth.Room.DefaultBackdropColor);
		var wash = fade * FadeWash;
		batcher.PushMode(new Color(1 - wash, wash, 0, 0));
		DrawPiece(batcher, frame.Subtexture, Color.Lerp(Color.White, depthColor, fade * FadeTint));
		batcher.PopMode();
	}

	public override void RenderNormals(Batcher batcher) => DrawPiece(batcher, frame.NormalSubtexture, Color.White);
	public override void RenderReflection(Batcher batcher, Color color) => DrawPiece(batcher, frame.Subtexture, color);

	// The glow goes out as it dies; alpha stays put so it still occludes like the live sprite did.
	public override void RenderEmissive(Batcher batcher)
	{
		var glow = 1 - fade;
		if (glow > 0)
			DrawPiece(batcher, frame.EmissiveSubtexture, new Color(glow, glow, glow, 1));
	}
}
