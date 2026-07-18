using System.Numerics;
using System.Text.Json.Nodes;

namespace Myth;

public sealed record NavTraceOptions(
	bool Enabled,
	string? OutputPath,
	string? RoomId,
	string? GoalId,
	int? EdgeFrom,
	int? EdgeTo,
	bool IncludeFrames
);

public sealed class NavTrace : IDisposable
{
	private readonly NavTraceOptions options;
	private readonly TextWriter writer;
	private readonly bool ownsWriter;

	public bool IncludeFrames => options.IncludeFrames;

	public NavTrace(NavTraceOptions options)
	{
		this.options = options;
		if (!string.IsNullOrWhiteSpace(options.OutputPath))
		{
			writer = new StreamWriter(options.OutputPath);
			ownsWriter = true;
		}
		else
		{
			writer = Console.Out;
			ownsWriter = false;
		}
	}

	public static NavTrace? Create(NavTraceOptions options)
		=> options.Enabled ? new NavTrace(options) : null;

	public void Event(Game game, string name, params (string Key, object? Value)[] fields)
		=> Write(game, name, edge: null, requireEdgeMatch: false, fields);

	public void EdgeEvent(Game game, string name, PlatformNavGraph.Edge edge, params (string Key, object? Value)[] fields)
		=> Write(game, name, edge, requireEdgeMatch: true, fields);

	public void Frame(Game game, Bud bud)
	{
		if (!IncludeFrames)
			return;
		Write(
			game,
			"budFrame",
			edge: null,
			requireEdgeMatch: false,
			("pos", Vec(bud.Position)),
			("vel", Vec(bud.Velocity)),
			("grounded", bud.IsGrounded),
			("label", bud.DebugNavLabel),
			("startNode", bud.DebugNavStartNodeId),
			("goalNode", bud.DebugNavGoalNodeId),
			("targetNode", bud.DebugNavTargetNodeId)
		);
	}

	public void Dispose()
	{
		writer.Flush();
		if (ownsWriter)
			writer.Dispose();
	}

	private void Write(
		Game game,
		string name,
		PlatformNavGraph.Edge? edge,
		bool requireEdgeMatch,
		params (string Key, object? Value)[] fields)
	{
		if (!ShouldWrite(game, edge, requireEdgeMatch))
			return;

		// Build a JsonObject DOM and emit it with ToJsonString(): the node tree serializes
		// reflection-free, so this survives trimming/NativeAOT (no IL2026/IL3050) where the
		// JsonSerializer.Serialize<Dictionary<string,object>> overload would not.
		var json = new JsonObject
		{
			["event"] = name,
			["frame"] = game.Time.Frame,
			["time"] = Math.Round(game.Time.Seconds, 4),
			["room"] = game.CurrentRoom?.Id,
			["goal"] = (game.GymTarget as Goal)?.ActorId
		};
		if (edge.HasValue)
		{
			json["edgeFrom"] = edge.Value.From;
			json["edgeTo"] = edge.Value.To;
			json["edgeKind"] = edge.Value.Kind.ToString();
			if (edge.Value.Jump is { } jump)
				json["jump"] = new JsonObject
				{
					["direction"] = jump.Direction,
					["runup"] = jump.RunupTime,
					["hold"] = jump.HoldTime,
					["airDirection"] = jump.AirDirection,
					["switch"] = jump.SwitchTime,
					["wall"] = jump.WallJump
				};
		}
		foreach (var (key, value) in fields)
			json[key] = ToNode(value);

		writer.WriteLine(json.ToJsonString());
	}

	// Converts a boxed trace field value to a JsonNode without reflection. Covers the scalar and
	// float[] (Vec) shapes the trace emits; anything unexpected falls back to its string form.
	private static JsonNode? ToNode(object? value) => value switch
	{
		null => null,
		JsonNode node => node,
		string s => JsonValue.Create(s),
		bool b => JsonValue.Create(b),
		int i => JsonValue.Create(i),
		long l => JsonValue.Create(l),
		float f => JsonValue.Create(f),
		double d => JsonValue.Create(d),
		float[] arr => new JsonArray(Array.ConvertAll(arr, v => (JsonNode?)JsonValue.Create(v))),
		_ => JsonValue.Create(value.ToString())
	};

	private bool ShouldWrite(Game game, PlatformNavGraph.Edge? edge, bool requireEdgeMatch)
	{
		if (!string.IsNullOrWhiteSpace(options.RoomId) &&
			!string.Equals(game.CurrentRoom?.Id, options.RoomId, StringComparison.OrdinalIgnoreCase))
			return false;

		if (!string.IsNullOrWhiteSpace(options.GoalId) &&
			!string.Equals((game.GymTarget as Goal)?.ActorId, options.GoalId, StringComparison.OrdinalIgnoreCase))
			return false;

		if (options.EdgeFrom.HasValue || options.EdgeTo.HasValue)
		{
			if (!edge.HasValue)
				return !requireEdgeMatch;
			if (options.EdgeFrom.HasValue && edge.Value.From != options.EdgeFrom.Value)
				return false;
			if (options.EdgeTo.HasValue && edge.Value.To != options.EdgeTo.Value)
				return false;
		}

		return true;
	}

	public static float[] Vec(Vector2 value)
		=> [(float)Math.Round(value.X, 3), (float)Math.Round(value.Y, 3)];
}
