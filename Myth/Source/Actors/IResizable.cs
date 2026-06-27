using Foster.Framework;

namespace Myth;

// Which axes the editor lets an IResizable actor be dragged along. An implementer may expose a
// `public static readonly ResizeAxes ResizableAxes` sibling to DefaultSize to restrict resizing
// (e.g. Jumpthru is X-only); the editor reads it reflectively and defaults to Both when absent.
public enum ResizeAxes
{
	X,
	Y,
	Both
}

// An actor authored as a tile-snapped rectangle the editor can resize with drag handles
// (see Manager.Editor). The runtime footprint lives in Size; the editor reads/writes it through
// the actor's "Size" point property. Implementers must also expose a
// `public static readonly Point2 DefaultSize` giving a freshly placed instance its size — the
// editor discovers it reflectively when building the resizable-type registry.
public interface IResizable
{
	Point2 Size { get; set; }
}
