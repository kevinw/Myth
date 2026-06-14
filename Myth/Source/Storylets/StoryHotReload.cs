using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Myth.StoryHotReload))]

namespace Myth;

internal static class StoryHotReload
{
	public static void ClearCache(Type[]? updatedTypes)
	{
		if (TouchesStory(updatedTypes))
			Game.RequestStoryReload();
		NotifyGameOfHotReload(updatedTypes);
	}

	public static void UpdateApplication(Type[]? updatedTypes)
	{
		if (TouchesStory(updatedTypes))
			Game.RequestStoryReload();
		NotifyGameOfHotReload(updatedTypes);
	}

	private static void NotifyGameOfHotReload(Type[]? updatedTypes)
	{
		if (updatedTypes is { Length: > 0 })
			Game.NotifyHotReload("Applied edits");
	}

	private static bool TouchesStory(Type[]? updatedTypes)
	{
		if (updatedTypes == null || updatedTypes.Length == 0)
			return true;

		foreach (var type in updatedTypes)
		{
			if (type.Namespace == "Myth" && (
				type.Name.Contains("Story", StringComparison.OrdinalIgnoreCase) ||
				type.Name.Contains("Game", StringComparison.OrdinalIgnoreCase)))
				return true;
		}
		return false;
	}
}
