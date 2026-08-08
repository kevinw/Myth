using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum ButtonMode { MustHold, PressesOnce }

// A four-way pressure button. Direction is the outward-facing normal: Up sits on a floor,
// Down on a ceiling, and Left/Right on the opposite wall. PressesOnce latches; Persistent
// remembers that latch in the save. MustHold mirrors its physical state to the target.
[GameActor(Sprite = "buttons", BoundsX = -5, BoundsY = -5, BoundsW = 10, BoundsH = 10,
	Tags = ActorTag.System, Anchor = SpriteAnchor.Center, ShowFacing = false, LivePositionEditable = true)]
public class Button : Actor
{
	public Cardinal Direction = Cardinal.Up;
	public ButtonMode Mode;
	public bool Persistent;
	public string TargetActorId = "";
	public string? SurfaceId;

	static int HousingDepth => 3;
	static int HousingHalfWidth => 5;
	static int CapHalfWidth => 3;
	static int CapExtension => 4;
	static int PressedCapExtension => 1;
	static float SidePressDeadzone => 0.1f;

	static readonly Color HousingColor = new(0x392b42);
	static readonly Color CapColor = new(0xb88a52);
	static readonly Color RimColor = new(0xedd19a);

	bool pressed;
	bool targetActive;
	Cardinal builtDirection;

	public Button()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = -2;
	}

	public override bool CastsSpriteShadow => true;
	public override bool LightingDithered => false;

	public override void Added()
	{
		base.Added();
		builtDirection = Direction;
		Hitbox = new(SensorRect);
		pressed = Mode == ButtonMode.PressesOnce && Persistent && Game.IsButtonPressed(ActorId);
	}

	public override void Update()
	{
		base.Update();
		if (builtDirection != Direction)
		{
			builtDirection = Direction;
			Hitbox = new(SensorRect);
		}

		if (pressed && Mode == ButtonMode.PressesOnce)
		{
			if (!targetActive)
				SetTarget(true);
			return;
		}

		var down = IsPressed();
		if (down == pressed)
			return;

		pressed = down;
		SetTarget(pressed);
		AudioEvent("button", "Action", pressed ? "Press" : "Unpress");
		if (pressed && Mode == ButtonMode.PressesOnce && Persistent)
			Game.PressButtonPersistently(ActorId);
	}

	bool IsPressed()
	{
		foreach (var actor in Game.Actors)
		{
			if (actor is not Pawn pawn || !pawn.Hitbox.Overlaps(pawn.Position - Position, Hitbox))
				continue;
			if (Mode == ButtonMode.PressesOnce || Direction.Vertical)
				return true;

			var towardMount = -Direction.Normal;
			if (Vector2.Dot(pawn.ButtonPressIntent, towardMount) > SidePressDeadzone ||
				Vector2.Dot(pawn.Velocity, towardMount) > SidePressDeadzone)
				return true;
		}
		return false;
	}

	void SetTarget(bool active)
	{
		if (Game.GetActorById(TargetActorId) is not IActivatable target)
		{
			targetActive = false;
			return;
		}
		target.SetActive(this, active);
		targetActive = active;
	}

	RectInt HousingRect => OrientedRect(-HousingDepth, 0, HousingHalfWidth);
	RectInt CapRect => OrientedRect(0, pressed ? PressedCapExtension : CapExtension, CapHalfWidth);
	RectInt SensorRect => OrientedRect(0, CapExtension, CapHalfWidth);

	RectInt OrientedRect(int from, int to, int halfWidth)
	{
		var tangent = Direction.TurnRight.Point;
		return RectInt.Between(Direction.Point * from - tangent * halfWidth,
			Direction.Point * to + tangent * halfWidth);
	}

	public override void Render(Batcher batcher)
	{
		var housing = HousingRect;
		var cap = CapRect;
		if (Assets.GetSurface(SurfaceId) is { } surface)
		{
			Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[12];
			BuildRect(triangles[..6], housing);
			BuildRect(triangles[6..], cap);
			surface.Render(batcher, triangles, Time.SecondsF);
		}
		else
		{
			batcher.Rect((Rect)housing, HousingColor);
			batcher.Rect((Rect)cap, CapColor);
		}
		batcher.RectLine((Rect)housing, 1, RimColor * 0.65f);
		batcher.RectLine((Rect)cap, 1, RimColor);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (Assets.GetSurface(SurfaceId) is not { HasNormal: true } surface)
			return;
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[12];
		BuildRect(triangles[..6], HousingRect);
		BuildRect(triangles[6..], CapRect);
		surface.RenderNormals(batcher, triangles, Time.SecondsF);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		batcher.Rect((Rect)HousingRect, color);
		batcher.Rect((Rect)CapRect, color);
	}

	void BuildRect(Span<Surface.Vertex> triangles, RectInt rect)
	{
		var world = (Vector2)Position;
		triangles[0] = new(rect.TopLeft, world + rect.TopLeft);
		triangles[1] = new(rect.TopRight, world + rect.TopRight);
		triangles[2] = new(rect.BottomRight, world + rect.BottomRight);
		triangles[3] = new(rect.TopLeft, world + rect.TopLeft);
		triangles[4] = new(rect.BottomRight, world + rect.BottomRight);
		triangles[5] = new(rect.BottomLeft, world + rect.BottomLeft);
	}

	public override Rect GetVisualBounds()
	{
		var bounds = HousingRect.Conflate(OrientedRect(0, CapExtension, CapHalfWidth));
		return new Rect(Position + bounds.Position, bounds.Size).Inflate(1);
	}
}
