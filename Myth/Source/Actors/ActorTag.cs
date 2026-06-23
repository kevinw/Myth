namespace Myth;

// Editor-facing categories for the actor palette. Filters are built straight from
// Enum.GetValues, so adding a category here is all the editor needs to show a subset.
[Flags]
public enum ActorTag
{
	None       = 0,
	Enemy      = 1 << 0,
	Hazard     = 1 << 1,
	Door       = 1 << 2,
	Light      = 1 << 3,
	Decoration = 1 << 4,
	Boss       = 1 << 5,
	System     = 1 << 6,
	Flying     = 1 << 7,
}
