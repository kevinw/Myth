using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace TinyLink;

internal static class JsonUtil
{
	public static bool ContainsProperty(JsonObject json, string key)
		=> TryGetProperty(json, key, out _);

	public static bool TryGetProperty(JsonObject json, string key, out JsonNode? value)
	{
		if (json.TryGetPropertyValue(key, out value))
			return true;

		foreach (var property in json)
		{
			if (!string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
				continue;

			value = property.Value;
			return true;
		}

		value = null;
		return false;
	}

	public static void RemoveProperty(JsonObject json, string key)
	{
		if (json.Remove(key))
			return;

		foreach (var property in json.ToArray())
		{
			if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
				json.Remove(property.Key);
		}
	}

	public static string? ReadString(JsonObject json, string key)
		=> TryGetProperty(json, key, out var node) && node != null ? node.GetValue<string>() : null;

	public static string ReadString(JsonObject json, string key, string fallback)
		=> ReadString(json, key) ?? fallback;

	public static bool ReadBool(JsonObject json, string key, bool fallback = false)
	{
		if (!TryGetProperty(json, key, out var node) || node == null)
			return fallback;

		return bool.TryParse(node.ToString(), out var value) ? value : fallback;
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
		return node != null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	public static bool TryReadFloat(JsonNode? node, out float value)
	{
		value = 0;
		return node != null && float.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}
}
