using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(TinyLink.StoryHotReload))]

namespace TinyLink;

internal static class StoryHotReload
{
	public static void ClearCache(Type[]? updatedTypes)
	{
		if (TouchesStory(updatedTypes))
			Game.RequestStoryReload();
	}

	public static void UpdateApplication(Type[]? updatedTypes)
	{
		if (TouchesStory(updatedTypes))
			Game.RequestStoryReload();
	}

	private static bool TouchesStory(Type[]? updatedTypes)
	{
		if (updatedTypes == null || updatedTypes.Length == 0)
			return true;

		foreach (var type in updatedTypes)
		{
			if (type.Namespace == "TinyLink" && (
				type.Name.Contains("Story", StringComparison.OrdinalIgnoreCase) ||
				type.Name.Contains("Game", StringComparison.OrdinalIgnoreCase)))
				return true;
		}
		return false;
	}
}
