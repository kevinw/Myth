using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

internal static class JsonUtil
{
	// Authored content (rooms, actor specs, tile kits, storylets) is hand-edited, so parse it
	// the way an editor's user expects: a trailing comma left behind mid-edit is not an error,
	// and // notes are skipped. Neither survives a re-save -- the DOM doesn't carry them.
	static readonly JsonDocumentOptions LenientOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	public static JsonNode? Parse(string json)
		=> JsonNode.Parse(json, documentOptions: LenientOptions);

	public static JsonNode? Parse(ReadOnlySpan<byte> utf8Json)
		=> JsonNode.Parse(utf8Json, documentOptions: LenientOptions);

	public static bool ContainsProperty(JsonObject json, string key)
		=> TryGetProperty(json, key, out _);

	public static JsonObject NormalizeStringValues(JsonObject json)
	{
		NormalizeStringValues((JsonNode)json);
		return json;
	}

	public static JsonNode? NormalizeStringValues(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject json:
				foreach (var (key, value) in json.ToArray())
					if (NormalizeStringValue(value) is { } normalized)
						json[key] = normalized;
					else
						NormalizeStringValues(value);
				break;
			case JsonArray array:
				for (var i = 0; i < array.Count; i++)
					if (NormalizeStringValue(array[i]) is { } normalized)
						array[i] = normalized;
					else
						NormalizeStringValues(array[i]);
				break;
		}

		return node;
	}

	static JsonNode? NormalizeStringValue(JsonNode? node)
		=> node is JsonValue value && value.GetValueKind() == JsonValueKind.String
			? JsonValue.Create(value.GetValue<string>())
			: null;

	public static bool TryGetProperty(JsonObject json, string key, out JsonNode? value)
		=> json.TryGetPropertyValue(key, out value);

	public static void RemoveProperty(JsonObject json, string key)
		=> json.Remove(key);

	public static string? ReadString(JsonObject json, string key)
		=> TryGetProperty(json, key, out var node) && node != null ? node.GetValue<string>() : null;

	public static string ReadString(JsonObject json, string key, string fallback)
		=> ReadString(json, key) ?? fallback;

	public static bool ReadBool(JsonObject json, string key, bool fallback = false)
	{
		if (!TryGetProperty(json, key, out var node) || node == null)
			return fallback;

		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<bool>(out var value))
				return value;
			if (jsonValue.TryGetValue<string>(out var text) && bool.TryParse(text, out value))
				return value;
		}
		return fallback;
	}

	public static int ReadInt(JsonObject json, string key, int fallback = 0)
		=> TryGetProperty(json, key, out var node) && TryReadInt(node, out var value) ? value : fallback;

	public static float ReadFloat(JsonObject json, string key, float fallback = 0)
		=> TryGetProperty(json, key, out var node) && TryReadFloat(node, out var value) ? value : fallback;

	public static Point2? ReadPoint(JsonObject json, string key)
		=> TryGetProperty(json, key, out var node) ? ReadPoint(node) : null;

	public static Point2? ReadPoint(JsonNode? node)
	{
		if (node is JsonArray { Count: >= 2 } array &&
			TryReadInt(array[0], out var x) &&
			TryReadInt(array[1], out var y))
			return new Point2(x, y);

		return null;
	}

	public static Vector2? ReadVector2(JsonObject json, string key)
		=> TryGetProperty(json, key, out var node) ? ReadVector2(node) : null;

	public static Vector2? ReadVector2(JsonNode? node)
	{
		if (node is JsonArray { Count: >= 2 } array &&
			TryReadFloat(array[0], out var x) &&
			TryReadFloat(array[1], out var y))
			return new Vector2(x, y);

		return null;
	}

	public static Color? ReadColor(JsonObject json, string key)
		=> TryGetProperty(json, key, out var node) ? ReadColor(node) : null;

	public static Color? ReadColor(JsonNode? node)
	{
		if (node is JsonValue)
		{
			var text = node.GetValue<string>().TrimStart('#');
			if (uint.TryParse(text, NumberStyles.HexNumber, null, out var hex))
				return text.Length <= 6 ? new Color((int)hex) : new Color(hex);
		}

		return null;
	}

	public static string ColorToHex(Color color)
		=> $"{color.R:X2}{color.G:X2}{color.B:X2}";

	public static bool TryReadInt(JsonNode? node, out int value)
	{
		value = 0;
		if (node is not JsonValue json)
			return false;
		if (json.TryGetValue(out value))
			return true;
		if (json.TryGetValue<string>(out var text))
			return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		return false;
	}

	public static bool TryReadFloat(JsonNode? node, out float value)
	{
		value = 0;
		if (node is not JsonValue json)
			return false;
		if (json.TryGetValue(out value))
			return true;
		if (json.TryGetValue<string>(out var text))
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		return false;
	}
}
