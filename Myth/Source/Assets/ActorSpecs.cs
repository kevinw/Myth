using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Foster.Framework;
using LinkDotNet.StringBuilder;

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
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		NewLine = "\n",
		// Same reason as Room.WriteJson: these files are read and edited by hand.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	// Members the trimmer/AOT compiler must keep on every registered actor type: reflection in
	// Apply/SerializeActorDefaults reads public fields/properties, and Create/GetBaseline construct
	// via the parameterless ctor.
	private const DynamicallyAccessedMemberTypes RegisteredActorMembers =
		DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
		DynamicallyAccessedMemberTypes.PublicFields |
		DynamicallyAccessedMemberTypes.PublicProperties;

	// Wraps a registered Type so the DynamicallyAccessedMembers annotation flows through the
	// dictionary. Storing a bare Type in Dictionary<string, Type> drops the annotation, so every
	// TryGetValue consumer would warn (IL2067/IL2072); the annotated property preserves it.
	private readonly record struct ActorType(
		[param: DynamicallyAccessedMembers(RegisteredActorMembers)]
		[property: DynamicallyAccessedMembers(RegisteredActorMembers)]
		Type Type,
		bool ShowFacing);

	private static readonly Dictionary<string, JsonObject> specs = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> specPaths = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, ActorType> actorTypes = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<Type, string> actorNames = new();
	static readonly Dictionary<(MemberInfo Member, Type Attribute), Attribute?> memberAttributes = [];
	static readonly Dictionary<Type, SerializableMember[]> serializableMembers = [];
#if EDITOR
	static readonly Dictionary<string, string> savedSpecJson = new(StringComparer.OrdinalIgnoreCase);
	static readonly HashSet<string> dirtySpecs = new(StringComparer.OrdinalIgnoreCase);
#endif

	public static IEnumerable<(string Id, JsonObject Spec)> Specs => specs.Select(it => (it.Key, it.Value));

	// Registered actor type-name -> CLR type. The editor uses this to discover capabilities
	// (e.g. IResizable) without hardcoding type lists.
	public static IEnumerable<KeyValuePair<string, Type>> ActorTypes =>
		actorTypes.Select(it => new KeyValuePair<string, Type>(it.Key, it.Value.Type));
	public readonly record struct EditableProperty(
		string Name,
		string DisplayName,
		string JsonName,
		string? Group,
		Type Type,
		Type DeclaringType,
		JsonNode? Value,
		JsonNode? ResetValue,
		bool HasLocalValue,
		IAttribute? Inspector,
		MultilineAttribute? Multiline,
		ActorRefAttribute? ActorRef,
		VisibleWhenAttribute? VisibleWhen);

	readonly record struct SerializableMember(MemberInfo Member, MemberInfo? Settings, string DisplayName, string? Group)
	{
		public string Name => Member.Name;
		public Type Type => GetMemberType(Member);
		public Type DeclaringType => Member.DeclaringType ?? throw new InvalidOperationException();

		public object? GetValue(Actor actor)
			=> ActorSpecs.GetMemberValue(Member, Settings == null ? actor : ActorSpecs.GetMemberValue(Settings, actor)!);

		public void SetValue(Actor actor, object? value)
			=> ActorSpecs.SetMemberValue(Member, Settings == null ? actor : ActorSpecs.GetMemberValue(Settings, actor)!, value);
	}

	public static void ClearSpecs()
	{
		specs.Clear();
		specPaths.Clear();
#if EDITOR
		savedSpecJson.Clear();
		dirtySpecs.Clear();
#endif
	}

	public static void ClearActorTypes()
	{
		actorTypes.Clear();
		actorNames.Clear();
		memberAttributes.Clear();
		serializableMembers.Clear();
	}

	public static void RegisterActor<[DynamicallyAccessedMembers(RegisteredActorMembers)] T>(string name, bool showFacing) where T : Actor
	{
		actorTypes[name] = new ActorType(typeof(T), showFacing);
		actorNames[typeof(T)] = name;
	}

	public static Type? GetActorType(string name)
		=> actorTypes.TryGetValue(name, out var actorType) ? actorType.Type : null;

	public static string? ReadResolvedString(JsonObject spec, string key)
		=> ReadStringValue(GetResolvedProperty(spec, key));

	// JSON has no multi-line string, so a string property may also be written as an array of
	// lines -- what keeps a Script or a Text legible (and editable) in a hand-edited room file.
	// Both spellings read back identically; see StringToJson for the way back out.
	public static string? ReadStringValue(JsonNode? node)
		=> node switch
		{
			JsonArray lines => string.Join('\n', lines.Select(line => SingleLineString(line) ?? "")),
			null => null,
			_ => SingleLineString(node)
		};

	static string? SingleLineString(JsonNode? node)
		=> node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

	// Multi-line values go back out as an array of lines rather than one "\n"-escaped string,
	// so what the editor writes stays as readable as what you'd hand-author.
	public static JsonNode StringToJson(string value)
	{
		if (!value.Contains('\n'))
			return JsonValue.Create(value);

		var lines = new JsonArray();
		foreach (var line in value.ReplaceLineEndings("\n").Split('\n'))
			lines.Add((JsonNode)JsonValue.Create(line)); // the JsonNode overload; Add<T> isn't trim/AOT safe
		return lines;
	}

	public static Point2? ReadResolvedPoint(JsonObject spec, string key)
		=> JsonUtil.ReadPoint(GetResolvedProperty(spec, key));

	// An actor's placement. Position is a framework key (Actor.Position is [NoSerialize], so the key
	// is not member-derived) -- read it through here rather than a literal or a nameof that would
	// drift from the real key on rename.
	public static Point2? ReadResolvedPosition(JsonObject spec)
		=> ReadResolvedPoint(spec, PositionKey);

	public static float ReadResolvedFloat(JsonObject spec, string key, float fallback = 0)
		=> GetResolvedProperty(spec, key) is { } node && JsonUtil.TryReadFloat(node, out var value) ? value : fallback;

	public const string ContentFolder = "Actors";

	public static void LoadSpecs()
	{
		ClearSpecs();
		if (!Assets.Content.DirectoryExists(ContentFolder))
			return;

		foreach (var key in Assets.Content.EnumerateDirectory(ContentFolder, "*.json", SearchOption.AllDirectories))
		{
			try
			{
				var spec = JsonUtil.Parse(Assets.Content.ReadAllText(key))?.AsObject();
				if (spec == null)
					continue;
				JsonUtil.NormalizeStringValues(spec);

				var name = ReadString(spec, NameKey) ?? ReadString(spec, IdKey);
				if (string.IsNullOrWhiteSpace(name))
				{
					name = Path.GetFileNameWithoutExtension(key);
					spec[NameKey] = name;
				}

				specs[name] = spec;
				// Spec editing writes back to the source file; zip content has none, so the
				// prefab editor's save path is simply absent there.
				if (!Assets.ContentIsZip)
					specPaths[name] = Path.Join(Assets.AssetsPath, key);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Failed to load actor spec '{key}': {e}");
			}
		}
	}

	[UnconditionalSuppressMessage("Trimming", "IL2072", Justification =
		"ContentRef<T> is a value type; creating its zero value does not require preserved constructors.")]
	public static void CollectContentReferences(JsonObject spec, ContentDependencyCollector collector, string origin)
	{
		var resolved = Resolve(spec);
		if (ReadString(resolved, TypeKey) is not { } typeName || !actorTypes.TryGetValue(typeName, out var type))
			return;
		foreach (var member in GetSerializableMembers(type.Type))
			if (member.Type.IsValueType && typeof(IContentReference).IsAssignableFrom(member.Type) &&
				ReadStringValue(GetResolvedProperty(resolved, member.Name)) is { Length: > 0 } name &&
				Activator.CreateInstance(member.Type) is IContentReference reference)
				reference.WithName(name).Collect(collector, $"{origin} field {member.Name}");
	}

	public static JsonObject? Get(string id)
		=> specs.GetValueOrDefault(id);

	public static JsonObject? Resolve(string id)
		=> specs.TryGetValue(id, out var spec) ? Resolve(spec) : null;

	public static JsonObject Resolve(JsonObject spec)
		=> Resolve(spec, []);

	public static string? GetTypeName(JsonObject spec)
		=> GetResolvedProperty(spec, TypeKey)?.GetValue<string>();

	public static JsonNode? GetResolvedProperty(JsonObject spec, string name)
	{
		for (var depth = 0; depth < 64; depth++)
		{
			if (JsonUtil.TryGetProperty(spec, name, out var value))
				return value;
			if (ReadString(spec, FromKey) is not { } from || !specs.TryGetValue(from, out spec!))
				return null;
		}
		return null;
	}

	public static Actor? Create(Game game, string id, Point2? position = null)
	{
		if (!specs.TryGetValue(id, out var spec))
		{
			Console.Error.WriteLine($"Actor spec '{id}' not found.");
			return null;
		}

		return Create(game, spec, position);
	}

	public static Actor? Create(Game game, JsonObject spec, Point2? position = null, string? sessionDestroyKey = null)
	{
		var resolved = Resolve(spec);
		var typeName = ReadString(resolved, TypeKey);
		if (string.IsNullOrWhiteSpace(typeName) || !actorTypes.TryGetValue(typeName, out var type))
		{
			// todo: in release build, make this a log?
			throw new Exception($"Actor type '{typeName}' not registered.");
		}

		var actorPosition = position ?? ReadPoint(resolved, PositionKey) ?? Point2.Zero;
		return game.Create(type.Type, actorPosition, actor =>
		{
			actor.SessionDestroyKey = sessionDestroyKey;
			Apply(actor, type.Type, resolved);
#if EDITOR
			actor.AuthoredSpec = spec;
#endif
		});
	}

	public static JsonObject CloneObject(JsonObject source)
		=> JsonUtil.NormalizeStringValues(source.DeepClone().AsObject());

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
		var inherited = ReadString(spec, FromKey) is { } from && specs.TryGetValue(from, out var inheritedSpec)
			? inheritedSpec
			: null;
		foreach (var member in GetSerializableMembers(type.Type))
		{
			if (!type.ShowFacing && member.Name == nameof(Actor.Facing))
				continue;
			var jsonName = member.Name;
			var hasLocal = JsonUtil.ContainsProperty(spec, jsonName);
			JsonNode? value = null;
			JsonNode? resetValue = null;
			if (JsonUtil.TryGetProperty(resolved, jsonName, out var resolvedValue) && resolvedValue != null)
				value = resolvedValue.DeepClone();
			else if (JsonUtil.TryGetProperty(defaults, jsonName, out var defaultValue) && defaultValue != null)
				value = defaultValue.DeepClone();
			if (inherited != null && GetResolvedProperty(inherited, jsonName) is { } inheritedValue)
				resetValue = inheritedValue;
			else if (JsonUtil.TryGetProperty(defaults, jsonName, out var resetDefaultValue))
				resetValue = resetDefaultValue;

			yield return new EditableProperty(member.Name, member.DisplayName, jsonName, member.Group,
				member.Type, member.DeclaringType,
				value, resetValue, hasLocal,
				GetMemberAttribute<IAttribute>(member.Member),
				GetMemberAttribute<MultilineAttribute>(member.Member),
				GetMemberAttribute<ActorRefAttribute>(member.Member),
				GetMemberAttribute<VisibleWhenAttribute>(member.Member));
		}
	}

	public static IEnumerable<EditableProperty> GetEditableProperties(Actor actor)
	{
		if (!actorNames.TryGetValue(actor.GetType(), out var typeName) || !actorTypes.TryGetValue(typeName, out var type))
			yield break;

		foreach (var member in GetSerializableMembers(type.Type))
		{
			if (!type.ShowFacing && member.Name == nameof(Actor.Facing))
				continue;
			yield return new EditableProperty(member.Name, member.DisplayName, member.Name, member.Group,
				member.Type, member.DeclaringType, ToJsonNode(member.GetValue(actor)), null, false,
				GetMemberAttribute<IAttribute>(member.Member),
				GetMemberAttribute<MultilineAttribute>(member.Member),
				GetMemberAttribute<ActorRefAttribute>(member.Member),
				GetMemberAttribute<VisibleWhenAttribute>(member.Member));
		}
	}

	public static bool SetEditableProperty(Actor actor, string name, JsonNode? node)
	{
		if (node == null || !actorNames.TryGetValue(actor.GetType(), out var typeName) ||
			!actorTypes.TryGetValue(typeName, out var type))
			return false;

		foreach (var member in GetSerializableMembers(type.Type))
			if (member.Name == name && TryReadValue(node, member.Type, member.GetValue(actor), out var value))
			{
				member.SetValue(actor, value);
				return true;
			}
		return false;
	}

	static T? GetMemberAttribute<T>(MemberInfo member) where T : Attribute
	{
		var key = (member, typeof(T));
		if (!memberAttributes.TryGetValue(key, out var attribute))
			memberAttributes[key] = attribute = member.GetCustomAttribute<T>();
		return (T?)attribute;
	}

	public static void SetProperty(string id, string jsonName, JsonNode? value)
	{
		if (!specs.TryGetValue(id, out var spec))
			return;

		JsonUtil.RemoveProperty(spec, jsonName);
		spec[jsonName] = value?.DeepClone();
#if EDITOR
		dirtySpecs.Add(id);
#endif
	}

	public static void SetProperty(JsonObject spec, string jsonName, JsonNode? value)
	{
		JsonUtil.RemoveProperty(spec, jsonName);
		spec[jsonName] = value?.DeepClone();
	}

	public static void ResetProperty(string id, string jsonName)
	{
		if (specs.TryGetValue(id, out var spec))
		{
			JsonUtil.RemoveProperty(spec, jsonName);
#if EDITOR
			dirtySpecs.Add(id);
#endif
		}
	}

	public static void ResetProperty(JsonObject spec, string jsonName)
		=> JsonUtil.RemoveProperty(spec, jsonName);

	public static void SaveSpec(string id)
	{
		if (!specs.TryGetValue(id, out var spec) || !specPaths.TryGetValue(id, out var path))
			return;

		var json = ToJsonString(PruneSpecForSave(id, spec));
		File.WriteAllText(path, json);
#if EDITOR
		savedSpecJson[id] = json;
		dirtySpecs.Remove(id);
#endif
	}

	public static string CaptureSpecJson(string id)
		=> specs.TryGetValue(id, out var spec) ? ToJsonString(PruneSpecForSave(id, spec)) : "{}";

	public static void ApplySpecJson(string id, string jsonText)
	{
		var spec = JsonUtil.NormalizeStringValues(JsonUtil.Parse(jsonText)?.AsObject() ?? []);
		if (string.IsNullOrWhiteSpace(ReadString(spec, NameKey)))
			spec[NameKey] = id;
		specs[id] = spec;
#if EDITOR
		UpdateSpecDirty(id);
#endif
	}

#if EDITOR
	public static bool CanSaveSpec(string id) => specPaths.ContainsKey(id);
	public static bool IsSpecDirty(string id) => dirtySpecs.Contains(id);

	public static void CreateSpec(string id, string path, JsonObject spec)
	{
		spec = CloneObject(spec);
		spec[NameKey] = id;
		specs[id] = spec;
		specPaths[id] = path;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		SaveSpec(id);
	}

	public static void DeleteSpec(string id)
	{
		if (specPaths.Remove(id, out var path) && File.Exists(path))
			File.Delete(path);
		specs.Remove(id);
		savedSpecJson.Remove(id);
		dirtySpecs.Remove(id);
	}

	public static void CaptureSavedSpecState()
	{
		savedSpecJson.Clear();
		dirtySpecs.Clear();
		foreach (var (id, _) in specs)
			savedSpecJson[id] = CaptureSpecJson(id);
	}

	static void UpdateSpecDirty(string id)
	{
		if (savedSpecJson.TryGetValue(id, out var saved) && CaptureSpecJson(id) == saved)
			dirtySpecs.Remove(id);
		else
			dirtySpecs.Add(id);
	}
#endif

	public static JsonNode? ToJsonNode(object? value)
	{
		return value switch
		{
			null => null,
			bool it => JsonValue.Create(it),
			int it => JsonValue.Create(it),
			float it => JsonValue.Create(it),
			string it => StringToJson(it),
			IContentReference it => StringToJson(it.Name),
			Point2 it => new JsonArray(it.X, it.Y),
			Point2[] it => PointArrayToJson(it),
			Vector2 it => new JsonArray(it.X, it.Y),
			Color it => JsonValue.Create(JsonUtil.ColorToHex(it)),
			Signs it => JsonValue.Create(it.ToString()),
			Cardinal it => JsonValue.Create(it.ToString()),
			Enum it => JsonValue.Create(it.ToString()),
			_ => null
		};
	}

	static JsonArray PointArrayToJson(Point2[] points)
	{
		var array = new JsonArray();
		foreach (var point in points)
			array.Add((JsonNode)new JsonArray(point.X, point.Y));
		return array;
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
			? GetSerializableMembers(resolvedType.Type).ToDictionary(it => it.Name, StringComparer.OrdinalIgnoreCase)
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

	// type is the actor's registered CLR type (annotated so its members survive trimming); it
	// equals actor.GetType() but carries the DynamicallyAccessedMembers annotation GetType() drops.
	private static void Apply(Actor actor, [DynamicallyAccessedMembers(RegisteredActorMembers)] Type type, JsonObject spec)
	{
		actor.ActorId = ReadString(spec, IdKey);
		foreach (var member in GetSerializableMembers(type))
		{
			if (!JsonUtil.TryGetProperty(spec, member.Name, out var node) || node == null)
				continue;

			if (TryReadValue(node, member.Type, member.GetValue(actor), out var value))
				member.SetValue(actor, value);
		}
	}

	private static JsonObject GetBaseline(JsonObject local, JsonObject resolved)
	{
		if (ReadString(local, FromKey) is { } from && specs.TryGetValue(from, out var inherited))
			return Resolve(inherited);

		var typeName = ReadString(resolved, TypeKey);
		if (typeName == null || !actorTypes.TryGetValue(typeName, out var type))
			return [];

		var actor = (Actor?)Activator.CreateInstance(type.Type);
		return actor != null ? SerializeActorDefaults(type.Type, actor) : [];
	}

	private static JsonObject SerializeActorDefaults([DynamicallyAccessedMembers(RegisteredActorMembers)] Type type, Actor actor)
	{
		var json = new JsonObject();
		foreach (var member in GetSerializableMembers(type))
		{
			var value = member.GetValue(actor);
			if (ToJsonNode(value) is { } node)
			{
				json[member.Name] = node;
			}
		}
		return json;
	}

	private static object? GetMemberValue(MemberInfo member, object target)
		=> member switch
		{
			FieldInfo field => field.GetValue(target),
			PropertyInfo property => property.GetValue(target),
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

	private static SerializableMember[] GetSerializableMembers(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
	{
		if (serializableMembers.TryGetValue(type, out var cached))
			return cached;

		var members = new List<SerializableMember>();
		AddSerializableMembers(members, type);
		return serializableMembers[type] = [.. members];
	}

	static void AddSerializableMembers(List<SerializableMember> members,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
			DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
	{
		if (type.BaseType is { } baseType)
			AddSerializableMembers(members, baseType);

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
		string? group = null;
		foreach (var field in type.GetFields(flags))
		{
			if (GetMemberAttribute<InspectorGroupAttribute>(field) is { } groupBoundary)
				group = groupBoundary.Name;
			if (field.IsStatic || field.GetCustomAttribute<NoSerializeAttribute>() != null)
				continue;
			if (IsSupportedType(field.FieldType))
			{
				if (!field.IsInitOnly)
					members.Add(new(field, null, field.Name, group));
			}
			else if (GetMemberAttribute<ActorSettingsAttribute>(field) is { } settings)
				AddSettingsMembers(members, field, settings.SettingsType, settings);
		}

		foreach (var property in type.GetProperties(flags))
		{
			// Get-only properties (computed getters like Actor.Origin) have no setter, so they're
			// skipped here automatically — no [NoSerialize] needed on them.
			if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || property.GetCustomAttribute<NoSerializeAttribute>() != null)
				continue;
			if (IsSupportedType(property.PropertyType))
				members.Add(new(property, null, property.Name, null));
		}
	}

	static void AddSettingsMembers(List<SerializableMember> destination, MemberInfo settingsMember,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
			DynamicallyAccessedMemberTypes.PublicProperties)] Type settingsType, ActorSettingsAttribute settings)
	{
		if (GetMemberType(settingsMember) != settingsType)
			throw new InvalidOperationException($"Actor settings member '{settingsMember.Name}' must be {settingsType.Name}.");
		AddSettingsMembers(destination, settingsMember, settingsType, settings, settings.Group);
	}

	static void AddSettingsMembers(List<SerializableMember> destination, MemberInfo settingsMember,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields |
			DynamicallyAccessedMemberTypes.PublicProperties)] Type settingsType, ActorSettingsAttribute settings,
		string? defaultGroup)
	{
		if (settingsType.BaseType is { } baseType)
			AddSettingsMembers(destination, settingsMember, baseType, settings, defaultGroup);

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
		var group = defaultGroup;
		foreach (var field in settingsType.GetFields(flags))
		{
			if (GetMemberAttribute<InspectorGroupAttribute>(field) is { } groupBoundary)
				group = groupBoundary.Name;
			if (!field.IsInitOnly && !field.IsStatic && field.GetCustomAttribute<NoSerializeAttribute>() == null &&
				IsSupportedType(field.FieldType))
				AddSettingsMember(destination, settingsMember, field, settings, group);
		}
		foreach (var property in settingsType.GetProperties(flags))
			if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 &&
				property.GetCustomAttribute<NoSerializeAttribute>() == null && IsSupportedType(property.PropertyType))
				AddSettingsMember(destination, settingsMember, property, settings, defaultGroup);
	}

	static void AddSettingsMember(List<SerializableMember> destination, MemberInfo settingsMember, MemberInfo member,
		ActorSettingsAttribute settings, string? group)
	{
		if (destination.Exists(it => string.Equals(it.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
			throw new InvalidOperationException($"Actor setting '{member.Name}' is exposed more than once.");
		destination.Add(new(member, settingsMember, SettingsDisplayName(member.Name, settings.TrimPrefix), group));
	}

	static string SettingsDisplayName(string name, string? trimPrefix)
	{
		var value = trimPrefix != null && name.StartsWith(trimPrefix, StringComparison.Ordinal) && name.Length > trimPrefix.Length
			? name.AsSpan(trimPrefix.Length)
			: name.AsSpan();
		var label = new ValueStringBuilder(stackalloc char[value.Length * 2]);
		for (var i = 0; i < value.Length; i++)
		{
			if (i > 0 && char.IsUpper(value[i]) && (!char.IsUpper(value[i - 1]) ||
				i + 1 < value.Length && char.IsLower(value[i + 1])))
				label.Append(' ');
			label.Append(value[i]);
		}
		var result = label.ToString();
		label.Dispose();
		return result;
	}

	private static bool IsSupportedType(Type type)
		=> type == typeof(bool) ||
		   type == typeof(int) ||
		   type == typeof(float) ||
		   type == typeof(string) ||
		   type == typeof(Point2) ||
		   type == typeof(Point2?) ||
		   type == typeof(Point2[]) ||
		   type == typeof(Vector2) ||
		   type == typeof(Color) ||
		   type == typeof(Signs) ||
		   type == typeof(Cardinal) ||
		   typeof(IContentReference).IsAssignableFrom(type) ||
		   type.IsEnum;

	public static bool IsContentReference(Type type) => typeof(IContentReference).IsAssignableFrom(type);

	private static Type GetMemberType(MemberInfo member)
		=> member switch
		{
			FieldInfo field => field.FieldType,
			PropertyInfo property => property.PropertyType,
			_ => throw new NotSupportedException(member.MemberType.ToString())
		};

	private static void SetMemberValue(MemberInfo member, object target, object? value)
	{
		switch (member)
		{
			case FieldInfo field:
				field.SetValue(target, value);
				break;
			case PropertyInfo property:
				property.SetValue(target, value);
				break;
		}
	}

	private static bool TryReadValue(JsonNode node, Type type, object? currentValue, out object? value)
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
				value = ReadStringValue(node);
			else if (currentValue is IContentReference reference && ReadStringValue(node) is { } name)
				value = reference.WithName(name);
			else if (type == typeof(Point2))
				value = ReadPoint(node);
			else if (type == typeof(Point2?))
				value = ReadPoint(node);
			else if (type == typeof(Point2[]))
				value = ReadPointArray(node);
			else if (type == typeof(Vector2))
				value = JsonUtil.ReadVector2(node);
			else if (type == typeof(Color))
				value = JsonUtil.ReadColor(node);
			else if (type == typeof(Signs))
				value = ReadSigns(node);
			else if (type == typeof(Cardinal))
				value = ReadCardinal(node);
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

	public static Point2[]? ReadPointArray(JsonNode? node)
	{
		if (node is not JsonArray array)
			return null;

		var points = new Point2[array.Count];
		for (var i = 0; i < array.Count; i++)
			if (ReadPoint(array[i]) is { } point)
				points[i] = point;
			else
				return null;
		return points;
	}

	public static Signs ReadSigns(JsonNode? node)
	{
		if (node == null)
			return Signs.Positive;
		if (JsonUtil.TryReadInt(node, out var asInt))
			return Signs.FromInt(asInt);
		return Signs.Parse(node.GetValue<string>());
	}

	public static Cardinal? ReadCardinal(JsonNode? node)
	{
		var text = node?.GetValue<string>();
		for (var raw = Cardinal.RightValue; raw <= Cardinal.UpValue; raw++)
		{
			var direction = Cardinal.FromRawValue(raw);
			if (string.Equals(text, direction.ToString(), StringComparison.OrdinalIgnoreCase))
				return direction;
		}
		return null;
	}

	public static string? ReadString(JsonObject json, string key)
		=> ReadStringValue(JsonUtil.TryGetProperty(json, key, out var node) ? node : null);
}
