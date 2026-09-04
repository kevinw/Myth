using Kit;

namespace Myth;

static class RigLibrary
{
	static readonly Dictionary<string, OzzRig> rigs = [];

	public static OzzRig Get(string name)
	{
		if (rigs.TryGetValue(name, out var rig))
			return rig;
		using var stream = Assets.Content.OpenRead($"Rigs/{name}.ozzpack");
		return rigs[name] = OzzRig.Load(stream);
	}

	public static void Clear()
	{
		foreach (var rig in rigs.Values)
			rig.Dispose();
		rigs.Clear();
	}
}
