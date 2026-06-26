using Foster.Framework;

namespace Myth;

// An actor authored as a tile-snapped rectangle the editor can resize with drag handles
// (see Manager.Editor). The runtime footprint lives in Size; the editor reads/writes it through
// the actor's "Size" point property. Implementers must also expose a
// `public static readonly Point2 DefaultSize` giving a freshly placed instance its size — the
// editor discovers it reflectively when building the resizable-type registry.
public interface IResizable
{
	Point2 Size { get; set; }
}
