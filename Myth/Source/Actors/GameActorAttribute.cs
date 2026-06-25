using Foster.Framework;

namespace Myth;

// Declares an Actor subclass as spec/editor-registerable. The source generator finds
// every type carrying this and emits the Factory.Register<T>() calls; Register<T> reads
// it back via typeof(T) (statically known -> trim/AOT safe). The actor also gets a palette
// entry (icon = Sprite, or a sprite named after the class); set HideFromPalette for
// code-only actors (Arrow, WallBullet) that still need a type name for room/spec JSON.
[AttributeUsage(AttributeTargets.Class)]
public sealed class GameActorAttribute : Attribute
{
	public string? Name;          // persisted "Type" in room/spec JSON; defaults to the class name
	public string? Sprite;        // palette icon sprite; defaults to a sprite named after the class
	public int OffsetX, OffsetY;  // palette icon origin offset
	public int BoundsX, BoundsY, BoundsW, BoundsH; // explicit editor bounds; W/H == 0 => auto from sprite
	public ActorTag Tags;
	public bool HideFromPalette; // code-only actors (spawned in code, not placed in the editor)
	public SpriteAnchor Anchor = SpriteAnchor.Auto; // where Position sits within the sprite

	public Point2 Offset => new(OffsetX, OffsetY);
	public RectInt? EditorBounds => BoundsW > 0 && BoundsH > 0 ? new RectInt(BoundsX, BoundsY, BoundsW, BoundsH) : null;
}
