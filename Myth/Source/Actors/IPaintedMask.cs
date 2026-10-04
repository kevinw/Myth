using System.Numerics;

namespace Myth;

// An actor whose look is directed by a mask painted over it in the editor (Manager.UpdateMaskPainting), saved
// as its PaintedMask blob.
public interface IPaintedMask
{
	void PaintMask(Vector2 worldCenter, float worldRadius, float softness, float strength, int slot);
	string SerializeMask();
}
