using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Foster.Framework;

namespace Myth;

public static class ActorSpecs
{
	private const string TypeKey = "Type";
	private const string FromKey = "From";
	private const string IdKey = "Id";
	private const string NameKey = "Name";
	private const string PositionKey = "Position";
	// NewLine = "\n" keeps serialized JSON LF on every platform (the indented writer would
	// otherwise emit CRLF on Windows), so room/spec files don't churn whitespace diffs.
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NewLine = "\n" };

	private static readonly Dictionary<string, JsonObject> specs = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> specPaths = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Type> actorTypes = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<Type, string> actorNames = new();

	public static IEnumerable<(string Id, JsonObject Spec)> Specs => specs.Select(it => (it.Key, it.Value));
	public readonly record struct EditableProperty(string Name, string JsonName, Type Type, JsonNode? Value, bool HasLocalValue);

	public static void ClearSpecs()
	{
		specs.Clear();
		specPaths.Clear();
	}

	public static void ClearActorTypes()
	{
		actorTypes.Clear();
		actorNames.Clear();
	}

	public static void RegisterActor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name) where T : Actor
	{
		actorTypes[name] = typeof(T);
		actorNames[typeof(T)] = name;
	}

	public static void LoadSpecs(string path)
	{
		ClearSpecs();
		if (!Directory.Exists(path))
			return;

		foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
		{
			try
			{
				var spec = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
				if (spec == null)
					continue;

				var name = ReadString(spec, NameKey) ?? ReadString(spec, IdKey);
				if (string.IsNullOrWhiteSpace(name))
				{
					name = Path.GetFileNameWithoutExtension(file);
					spec[NameKey] = name;
				}

				specs[name] = spec;
				specPaths[name] = file;
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Failed to load actor spec '{file}': {e}");
			}
		}
	}

	public static JsonObject? Get(string id)
		=> specs.GetValueOrDefault(id);

	public static JsonObject? Resolve(string id)
		=> specs.TryGetValue(id, out var spec) ? Resolve(spec) : null;

	public static JsonObject Resolve(JsonObject spec)
		=> Resolve(spec, []);

	public static string? GetTypeName(JsonObject spec)
		=> ReadString(Resolve(spec), TypeKey);

	public static Actor? Create(Game game, string id, Point2? position = null)
	{
		if (!specs.TryGetValue(id, out var spec))
		{
			Console.Error.WriteLine($"Actor spec '{id}' not found.");
			return null;
		}

		return Create(game, spec, position);
	}

	public static Actor? Create(Game game, JsonObject spec, Point2? position = null)
	{
		var resolved = Resolve(spec);
		var typeName = ReadString(resolved, TypeKey);
		if (string.IsNullOrWhiteSpace(typeName) || !actorTypes.TryGetValue(typeName, out var type))
		{
			// todo: in release build, make this a log?
			throw new Exception($"Actor type '{typeName}' not registered.");
		}

		var actorPosition = position ?? ReadPoint(resolved, PositionKey) ?? Point2.Zero;
		return game.Create(type, actorPosition, actor => Apply(actor, resolved));
	}

	public static JsonObject CloneObject(JsonObject source)
		=> source.DeepClone().AsObject();

	public static string GenerateActorId()
	{
		Span<byte> bytes = stackalloc byte[8];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}

	public static void AssignNewActorId(JsonObject actor)
		=> SetProperty(actor, IdKey, JsonValue.Create(GenerateActorId()));

	public static void EnsureActorId(JsonObject actor)
	{
		if (string.IsNullOrWhiteSpace(ReadString(actor, IdKey)))
			AssignNewActorId(actor);
	}

	public static string? GetActorId(JsonObject actor)
		=> ReadString(actor, IdKey);

	public static string ToJsonString(JsonObject json)
		=> CompactPointArrays(json.ToJsonString(JsonOptions));

	private static string CompactPointArrays(string json)
		=> Regex.Replace(
			json,
			@"\[\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\s*\]",
			"[$1, $2]");

	public static IEnumerable<EditableProperty> GetEditableProperties(string id)
	{
		if (!specs.TryGetValue(id, out var spec))
			yield break;

		foreach (var property in GetEditableProperties(spec))
			yield return property;
	}

	public static IEnumerable<EditableProperty> GetEditableProperties(JsonObject spec)
	{
		var resolved = Resolve(spec);
		var typeName = ReadString(resolved, TypeKey);
		if (typeName == null || !actorTypes.TryGetValue(typeName, out var type))
			yield break;

		var defaults = GetBaseline(new JsonObject { [TypeKey] = typeName }, resolved);
		foreach (var member in GetSerializableMembers(type))
		{
			var jsonName = member.Name;
			var hasLocal = JsonUtil.ContainsProperty(spec, jsonName);
			JsonNode? value = null;
			if (JsonUtil.TryGetProperty(resolved, jsonName, out var resolvedValue) && resolvedValue != null)
				value = resolvedValue.DeepClone();
			else if (JsonUtil.TryGetProperty(defaults, jsonName, out var defaultValue) && defaultValue != null)
				value = defaultValue.DeepClone();

			yield return new EditableProperty(member.Name, jsonName, GetMemberType(member), value, hasLocal);
		}
	}

	public static void SetProperty(string id, string jsonName, JsonNode? value)
	{
		if (!specs.TryGetValue(id, out var spec))
			return;

		JsonUtil.RemoveProperty(spec, jsonName);
		spec[jsonName] = value?.DeepClone();
	}

	public static void SetProperty(JsonObject spec, string jsonName, JsonNode? value)
	{
		JsonUtil.RemoveProperty(spec, jsonName);
		spec[jsonName] = value?.DeepClone();
	}

	public static void ResetProperty(string id, string jsonName)
	{
		if (specs.TryGetValue(id, out var spec))
			JsonUtil.RemoveProperty(spec, jsonName);
	}

	public static void ResetProperty(JsonObject spec, string jsonName)
		=> JsonUtil.RemoveProperty(spec, jsonName);

	public static void SaveSpec(string id)
	{
		if (!specs.TryGetValue(id, out var spec) || !specPaths.TryGetValue(id, out var path))
			return;

		File.WriteAllText(path, ToJsonString(PruneSpecForSave(id, spec)));
	}

	public static string CaptureSpecJson(string id)
		=> specs.TryGetValue(id, out var spec) ? ToJsonString(PruneSpecForSave(id, spec)) : "{}";

	public static void ApplySpecJson(string id, string jsonText)
	{
		var spec = JsonNode.Parse(jsonText)?.AsObject() ?? [];
		if (string.IsNullOrWhiteSpace(ReadString(spec, NameKey)))
			spec[NameKey] = id;
		specs[id] = spec;
	}

	public static JsonNode? ToJsonNode(object? value)
	{
		return value switch
		{
			null => null,
			bool it => JsonValue.Create(it),
			int it => JsonValue.Create(it),
			float it => JsonValue.Create(it),
			string it => JsonValue.Create(it),
			Point2 it => new JsonArray(it.X, it.Y),
			Vector2 it => new JsonArray(it.X, it.Y),
			Color it => JsonValue.Create(JsonUtil.ColorToHex(it)),
			Signs it => JsonValue.Create(it.ToString()),
			Enum it => JsonValue.Create(it.ToString()),
			_ => null
		};
	}

	public static JsonObject PruneForSave(JsonObject spec)
	{
		var resolved = Resolve(spec);
		var baseline = GetBaseline(spec, resolved);
		var output = new JsonObject();

		CopyIfPresent(spec, output, IdKey, IdKey);
		CopyIfPresent(spec, output, FromKey, FromKey);
		if (JsonUtil.ContainsProperty(spec, TypeKey))
			CopyIfPresent(spec, output, TypeKey, TypeKey);
		else if (!output.ContainsKey(FromKey) && ReadString(resolved, TypeKey) is { } typeName)
			output[TypeKey] = typeName;
		CopyIfPresent(spec, output, PositionKey, PositionKey);

		var memberNames = ReadString(resolved, TypeKey) is { } resolvedTypeName &&
						  actorTypes.TryGetValue(resolvedTypeName, out var resolvedType)
			? GetSerializableMembers(resolvedType).ToDictionary(it => it.Name, StringComparer.OrdinalIgnoreCase)
			: [];

		foreach (var (key, value) in spec)
		{
			if (IsReservedKey(key) || value == null)
				continue;

			var outputKey = memberNames.TryGetValue(key, out var member) ? member.Name : key;

			if (JsonUtil.TryGetProperty(baseline, outputKey, out var baselineValue) && JsonNode.DeepEquals(value, baselineValue))
				continue;

			output[outputKey] = value.DeepClone();
		}

		return output;
	}

	private static JsonObject PruneSpecForSave(string name, JsonObject spec)
	{
		var output = PruneForSave(spec);
		JsonUtil.RemoveProperty(output, IdKey);
		JsonUtil.RemoveProperty(output, NameKey);
		output.Insert(0, NameKey, JsonValue.Create(ReadString(spec, NameKey) ?? ReadString(spec, IdKey) ?? name));
		return output;
	}

	private static JsonObject Resolve(JsonObject spec, HashSet<string> visiting)
	{
		var merged = new JsonObject();
		var from = ReadString(spec, FromKey);
		if (!string.IsNullOrWhiteSpace(from))
		{
			if (!visiting.Add(from))
				throw new InvalidOperationException($"Actor spec inheritance cycle includes '{from}'.");

			if (!specs.TryGetValue(from, out var inherited))
				throw new InvalidOperationException($"Actor spec '{from}' not found.");

			merged = Resolve(inherited, visiting);
			visiting.Remove(from);
		}

		foreach (var (key, value) in spec)
		{
			JsonUtil.RemoveProperty(merged, key);
			merged[key] = value?.DeepClone();
		}

		return merged;
	}

	private static void Apply(Actor actor, JsonObject spec)
	{
		actor.ActorId = ReadString(spec, IdKey);
		foreach (var member in GetSerializableMembers(actor.GetType()))
		{
			if (!JsonUtil.TryGetProperty(spec, member.Name, out var node) || node == null)
				continue;

			if (TryReadValue(node, GetMemberType(member), out var value))
				SetMemberValue(member, actor, value);
		}
	}

	private static JsonObject GetBaseline(JsonObject local, JsonObject resolved)
	{
		if (ReadString(local, FromKey) is { } from && specs.TryGetValue(from, out var inherited))
			return Resolve(inherited);

		var typeName = ReadString(resolved, TypeKey);
		if (typeName == null || !actorTypes.TryGetValue(typeName, out var type))
			return [];

		var actor = (Actor?)Activator.CreateInstance(type);
		return actor != null ? SerializeActorDefaults(actor) : [];
	}

	private static JsonObject SerializeActorDefaults(Actor actor)
	{
		var json = new JsonObject();
		foreach (var member in GetSerializableMembers(actor.GetType()))
		{
			var value = GetMemberValue(member, actor);
			if (ToJsonNode(value) is { } node)
			{
				json[member.Name] = node;
			}
		}
		return json;
	}

	private static object? GetMemberValue(MemberInfo member, Actor actor)
		=> member switch
		{
			FieldInfo field => field.GetValue(actor),
			PropertyInfo property => property.GetValue(actor),
			_ => null
		};

	private static void CopyIfPresent(JsonObject source, JsonObject destination, string sourceKey, string destinationKey)
	{
		if (JsonUtil.TryGetProperty(source, sourceKey, out var value) && value != null)
			destination[destinationKey] = value.DeepClone();
	}

	private static bool IsReservedKey(string key)
		=> string.Equals(key, IdKey, StringComparison.OrdinalIgnoreCase) ||
		   string.Equals(key, FromKey, StringComparison.OrdinalIgnoreCase) ||
		   string.Equals(key, TypeKey, StringComparison.OrdinalIgnoreCase) ||
		   string.Equals(key, PositionKey, StringComparison.OrdinalIgnoreCase);

	private static IEnumerable<MemberInfo> GetSerializableMembers([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
	{
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

		foreach (var field in type.GetFields(flags))
		{
			if (field.IsInitOnly || field.IsStatic || field.GetCustomAttribute<NoSerializeAttribute>() != null)
				continue;
			if (IsSupportedType(field.FieldType))
				yield return field;
		}

		foreach (var property in type.GetProperties(flags))
		{
			if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || property.GetCustomAttribute<NoSerializeAttribute>() != null)
				continue;
			if (IsSupportedType(property.PropertyType))
				yield return property;
		}
	}

	private static bool IsSupportedType(Type type)
		=> type == typeof(bool) ||
		   type == typeof(int) ||
		   type == typeof(float) ||
		   type == typeof(string) ||
		   type == typeof(Point2) ||
		   type == typeof(Vector2) ||
		   type == typeof(Color) ||
		   type == typeof(Signs) ||
		   type.IsEnum;

	private static Type GetMemberType(MemberInfo member)
		=> member switch
		{
			FieldInfo field => field.FieldType,
			PropertyInfo property => property.PropertyType,
			_ => throw new NotSupportedException(member.MemberType.ToString())
		};

	private static void SetMemberValue(MemberInfo member, Actor actor, object? value)
	{
		switch (member)
		{
			case FieldInfo field:
				field.SetValue(actor, value);
				break;
			case PropertyInfo property:
				property.SetValue(actor, value);
				break;
		}
	}

	private static bool TryReadValue(JsonNode node, Type type, out object? value)
	{
		value = null;

		try
		{
			if (type == typeof(bool))
				value = node.GetValue<bool>();
			else if (type == typeof(int))
				value = node.GetValue<int>();
			else if (type == typeof(float))
				value = node.GetValue<float>();
			else if (type == typeof(string))
				value = node.GetValue<string>();
			else if (type == typeof(Point2))
				value = ReadPoint(node);
			else if (type == typeof(Vector2))
				value = JsonUtil.ReadVector2(node);
			else if (type == typeof(Color))
				value = JsonUtil.ReadColor(node);
			else if (type == typeof(Signs))
				value = ReadSigns(node);
			else if (type.IsEnum)
				value = Enum.Parse(type, node.GetValue<string>(), ignoreCase: true);
			else
				return false;

			return value != null;
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to read actor property '{node}': {e.Message}");
			return false;
		}
	}

	public static Point2? ReadPoint(JsonObject json, string key)
		=> JsonUtil.ReadPoint(json, key);

	public static Point2? ReadPoint(JsonNode? node)
		=> JsonUtil.ReadPoint(node);

	public static Signs ReadSigns(JsonNode? node)
	{
		if (node == null)
			return Signs.Positive;
		if (JsonUtil.TryReadInt(node, out var asInt))
			return Signs.FromInt(asInt);
		return Signs.Parse(node.GetValue<string>());
	}

	public static string? ReadString(JsonObject json, string key)
		=> JsonUtil.ReadString(json, key);
}
