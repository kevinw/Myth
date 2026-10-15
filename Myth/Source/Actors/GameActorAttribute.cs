using System.Diagnostics.CodeAnalysis;
using Foster.Framework;
using System.Text.Json.Nodes;

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
	public string? Icon;          // editor icon name; takes precedence over Sprite for the palette
	public bool EditorMarker;    // draw palette artwork at the authored position while editing
	public int OffsetX, OffsetY;  // palette icon origin offset
	public int BoundsX, BoundsY, BoundsW, BoundsH; // explicit editor bounds; W/H == 0 => auto from sprite
	public ActorTag Tags;
	public bool HideFromPalette; // code-only actors (spawned in code, not placed in the editor)
	public bool ShowFacing = true; // expose inherited Actor.Facing in the inspector
	public bool LivePositionEditable; // authored placement may be moved while the game is running
	public bool LiveResizable; // IResizable Size may be edited safely on the running instance; requires LivePositionEditable
	public SpriteAnchor Anchor = SpriteAnchor.Auto; // where Position sits within the sprite

	public Point2 Offset => new(OffsetX, OffsetY);
	public RectInt? EditorBounds => BoundsW > 0 && BoundsH > 0 ? new RectInt(BoundsX, BoundsY, BoundsW, BoundsH) : null;
}

// Compact metadata for fields shown in the actor inspector.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class IAttribute(float min, float max) : Attribute
{
	public float Min => min;
	public float Max => max;
	// Draw as a dial; consecutive dials flow into a grid (Knobs).
	public bool Knob { get; init; }
}

// String fields are single-line in the inspector; this gives one a text box Lines tall
// (for prose: barks, titles, scripts).
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MultilineAttribute(int lines = 4) : Attribute
{
	public int Lines => Math.Max(1, lines);
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class VisibleWhenAttribute(string member, object value) : Attribute
{
	public string Member => member;
	public string Value => value.ToString()!;
	public bool IsVisibleForActor(JsonObject actor)
		=> string.Equals(ActorSpecs.ReadResolvedString(actor, member), Value, StringComparison.OrdinalIgnoreCase);
}

// A string field holding another actor's Id: the inspector offers the room's actors by
// type and id instead of a free-text box. Kind narrows the list to actors assignable to
// it (e.g. typeof(IActivatable)).
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ActorRefAttribute(Type? kind = null) : Attribute
{
	public Type? Kind => kind;
}

// Flattens a settings object's editable members into its owning actor's existing JSON shape.
// Group controls the inspector header; TrimPrefix shortens labels without changing JSON keys.
[AttributeUsage(AttributeTargets.Field)]
public sealed class ActorSettingsAttribute(
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
		DynamicallyAccessedMemberTypes.PublicProperties)] Type settingsType,
	string? group = null, string? trimPrefix = null) : Attribute
{
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
		DynamicallyAccessedMemberTypes.PublicProperties)]
	public Type SettingsType => settingsType;
	public string? Group => group;
	public string? TrimPrefix => trimPrefix;
}

// Starts an inspector group for this and subsequent editable fields in the declaring type.
// Use [InspectorGroup] to return subsequent fields to the ungrouped section.
[AttributeUsage(AttributeTargets.Field)]
public sealed class InspectorGroupAttribute(string? name = null) : Attribute
{
	public string? Name => name;
}
