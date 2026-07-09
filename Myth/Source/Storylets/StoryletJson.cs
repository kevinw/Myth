using System.Text.Json;
using System.Text.Json.Nodes;

namespace Myth;

public static class StoryletJson
{
	public static StoryDeck DeckFromFile(string path, Dictionary<string, object?>? context = null)
	{
		var json = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? [];
		return DeckFromJson(json, context);
	}

	public static StoryDeck DeckFromJson(JsonObject json, Dictionary<string, object?>? context = null)
	{
		var deck = new StoryDeck();
		if (context != null)
			foreach (var (key, value) in context)
				deck.Context[key] = value;

		ReadPacket(deck, json, []);
		return deck;
	}

	static void ReadPacket(StoryDeck deck, JsonObject packet, Dictionary<string, JsonNode?> defaults)
	{
		if (packet["context"] is JsonObject context)
		{
			foreach (var (key, value) in context)
				if (!deck.Context.ContainsKey(key))
					deck.Context[key] = ReadValue(value);
		}

		var inheritedDefaults = new Dictionary<string, JsonNode?>(defaults);
		if (packet["defaults"] is JsonObject packetDefaults)
			foreach (var (key, value) in packetDefaults)
				inheritedDefaults[key] = value?.DeepClone();

		if (packet["storylets"] is not JsonArray storylets)
			return;

		foreach (var node in storylets)
		{
			if (node is not JsonObject storyletJson)
				continue;

			if (storyletJson.ContainsKey("storylets") || storyletJson.ContainsKey("defaults") || storyletJson.ContainsKey("context"))
			{
				ReadPacket(deck, storyletJson, inheritedDefaults);
				continue;
			}

			deck.Add(ReadStorylet(storyletJson, inheritedDefaults));
		}
	}

	static Storylet ReadStorylet(JsonObject json, Dictionary<string, JsonNode?> defaults)
	{
		var merged = new Dictionary<string, JsonNode?>(defaults);
		foreach (var (key, value) in json)
			merged[key] = value;

		if (!merged.TryGetValue("id", out var idNode) || idNode == null)
			throw new InvalidDataException("Storylet is missing an id.");

		var storylet = new Storylet(ReadValue(idNode)?.ToString() ?? "");
		if (ReadString(merged, "condition") is { } condition)
			storylet.Condition = condition;
		if (merged.TryGetValue("priority", out var priority))
			storylet.Priority = ReadValue(priority);
		if (merged.TryGetValue("redraw", out var redraw))
			storylet.Redraw = ReadRedraw(redraw);
		if (merged.TryGetValue("content", out var content) && content is JsonObject contentObject)
			storylet.Content = ReadObject(contentObject);
		if (merged.TryGetValue("outcomes", out var outcomes) && outcomes is JsonObject outcomesObject)
			storylet.Outcomes = ReadOutcomes(outcomesObject);
		return storylet;
	}

	static int ReadRedraw(JsonNode? node)
	{
		var value = ReadValue(node);
		if (value is string text)
		{
			if (string.Equals(text, "always", StringComparison.OrdinalIgnoreCase))
				return Storylet.RedrawAlways;
			if (string.Equals(text, "never", StringComparison.OrdinalIgnoreCase))
				return Storylet.RedrawNever;
		}
		return Convert.ToInt32(value);
	}

	static Dictionary<string, Dictionary<string, object?>> ReadOutcomes(JsonObject json)
	{
		var outcomes = new Dictionary<string, Dictionary<string, object?>>();
		foreach (var (key, value) in json)
			if (value is JsonObject updates)
				outcomes[key] = ReadObject(updates);
		return outcomes;
	}

	static Dictionary<string, object?> ReadObject(JsonObject json)
	{
		var result = new Dictionary<string, object?>();
		foreach (var (key, value) in json)
			result[key] = ReadValue(value);
		return result;
	}

	static string? ReadString(Dictionary<string, JsonNode?> values, string key)
		=> values.TryGetValue(key, out var value) ? ReadValue(value)?.ToString() : null;

	static object? ReadValue(JsonNode? node)
	{
		if (node == null)
			return null;
		if (node is JsonObject obj)
			return ReadObject(obj);
		if (node is JsonArray array)
			return array.Select(ReadValue).ToList();

		var value = node.GetValue<JsonElement>();
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number when value.TryGetInt32(out var i) => i,
			JsonValueKind.Number when value.TryGetDouble(out var d) => d,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => null
		};
	}
}
