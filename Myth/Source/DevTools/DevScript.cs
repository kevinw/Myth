#if EDITOR && !BROWSER
using System.Collections;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Mono.Cecil;

namespace System.Runtime.CompilerServices
{
	// Not in the BCL; the runtime honors it by name.
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
	{
		public string AssemblyName => assemblyName;
	}
}

namespace Myth
{
	public partial class Manager
	{
		// What a `cs` script sees as top-level names.
		public class ScriptGlobals(Manager manager)
		{
			// `help` lists every member with a [Description].
			[Description("the app: editor + game state")] public readonly Manager M = manager;
			[Description("the live game (in the editor too)")] public Game? G => M.game;
			[Description("the player; null in editor mode")] public Player? P => G?.GetFirst<Player>();
			[Description("the editor's room data")] public Room? Room => M.CurrentRoom;
			[Description("the live actor selected in the editor")] public Actor? Sel => (M.liveSelectedActorId ?? M.SelectedRoomActorId) is { } id ? G?.GetActorById(id) : null;
			[Description("cursor world position")] public Vector2 Mouse => G is { } g ? M.GameMouseWorld(g) : default;
			internal readonly StringBuilder Output = new();
			[Description("add a line to this entry's result")] public void Print(object? value) => Output.AppendLine(DevScript.Describe(value));
			[Description("globals, helpers and session variables")] public string Help() => DevScript.Help();

			// Annotations over the game view that pulse for a few seconds, so an answer can point at what it means.
			[Description("custom ImGui drawing over the game view; toScreen maps world positions")]
			public void Overlay(Action<ImDrawListPtr, Func<Vector2, Vector2>> draw, float seconds = 8)
			{
				M.scriptOverlays.Add((draw, M.Time.Elapsed + TimeSpan.FromSeconds(seconds)));
				DevScript.Running?.Overlays.Add((draw, seconds));
			}
			[Description("remove all overlays")] public void ClearOverlay() => M.scriptOverlays.Clear();
			[Description("outline an actor")] public void Ping(Actor actor, string? label = null, float seconds = 8) => Overlay((draw, screen) =>
			{
				var rect = ((Rect)actor.Hitbox.Rect + actor.Position).Inflate(3);
				draw.AddRect(screen(rect.TopLeft), screen(rect.BottomRight), OverlayColor(), 2, ImDrawFlags.None, 2);
				OverlayLabel(draw, screen(rect.TopLeft), label ?? DevScript.Short(actor));
			}, seconds);
			[Description("circle a world position")] public void Mark(Vector2 world, string? label = null, float seconds = 8) => Overlay((draw, screen) =>
			{
				draw.AddCircle(screen(world), 8, OverlayColor(), 0, 2);
				if (label != null)
					OverlayLabel(draw, screen(world) + new Vector2(10, -8), label);
			}, seconds);
			[Description("line between world positions")] public void Line(Vector2 from, Vector2 to, float seconds = 8) =>
				Overlay((draw, screen) => draw.AddLine(screen(from), screen(to), OverlayColor(), 2), seconds);

			uint OverlayColor() => ImGuiColor(1, 0.55f, 0.2f, 0.65f + 0.35f * MathF.Sin((float)M.Time.Elapsed.TotalSeconds * 8));
			static void OverlayLabel(ImDrawListPtr draw, Vector2 at, string text)
			{
				draw.AddText(at + Vector2.One, ImGuiColor(0, 0, 0, 1), text);
				draw.AddText(at, ImGuiColor(1, 0.75f, 0.45f, 1), text);
			}
		}

		readonly List<(Action<ImDrawListPtr, Func<Vector2, Vector2>> Draw, TimeSpan Until)> scriptOverlays = [];

		void ScriptOverlayImGui()
		{
			for (var i = scriptOverlays.Count - 1; i >= 0; i--)
				if (scriptOverlays[i].Until < Time.Elapsed)
					scriptOverlays.RemoveAt(i);
			if (scriptOverlays.Count == 0 || game is not { } g)
				return;

			var viewport = GameViewRect;
			var draw = ImGui.GetForegroundDrawList();
			draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
			foreach (var overlay in scriptOverlays)
				overlay.Draw(draw, world => playing ? GameWorldToImGui(viewport, g, world) : viewport.Center + (world - editorCamera) * editorZoom);
			draw.PopClipRect();
		}

		static Vector4 ClaudeTextColor => new(1, 0.62f, 0.3f, 1);
		const string ScriptingWindow = "Scripting";
		bool scriptingOpen;
		string scriptInput = "";
		volatile bool scriptRunning;
		int scriptHistoryShown;

		// The shared `cs` workspace: everything Claude and you have run, plus an input box (Ctrl/Cmd+Enter runs).
		void ScriptingImGui()
		{
			var history = DevScript.History;
			var unseen = 0;
			for (var i = history.Count - 1; i >= 0 && !history[i].Seen; i--)
				unseen++;
			if (!scriptingOpen)
			{
				ScriptingChipImGui(unseen);
				return;
			}

			ImGui.SetNextWindowSize(new Vector2(520, 480), ImGuiCond.FirstUseEver);
			DockNextToolWindow(ScriptingWindow);
			var visible = ImGui.Begin(ScriptingWindow, ref scriptingOpen, unseen > 0 ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None);
			NoteDockedWindow();
			if (visible)
				ScriptingWindowBody(history, unseen);
			ImGui.End();
		}

		// While the window is closed, a corner chip says Claude ran something, and opens the window.
		void ScriptingChipImGui(int unseen)
		{
			if (unseen == 0)
				return;

			ImGui.SetNextWindowPos(GameViewRect.BottomRight - new Vector2(8), ImGuiCond.Always, Vector2.One);
			if (ImGui.Begin("##ScriptingChip", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
				| ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking))
			{
				ImGui.PushStyleColor(ImGuiCol.Text, ClaudeTextColor);
				if (ImGui.SmallButton($"{EditorIcons.Code}  Claude ran {unseen}"))
					OpenScripting("");
				ImGui.PopStyleColor();
			}
			ImGui.End();
		}

		[ConsoleCommand("scripting")]
		void OpenScripting(string _)
		{
			scriptingOpen = true;
			ImGui.SetWindowFocus(ScriptingWindow);
		}

		void ScriptingWindowBody(List<DevScript.Entry> history, int unseen)
		{
			for (var i = history.Count - unseen; i < history.Count; i++)
				history[i].Seen = true;

			ImGui.PushFont(imgui.MonoFont);
			var style = ImGui.GetStyle();
			var inputHeight = ImGui.GetTextLineHeight() * 5 + style.FramePadding.Y * 2;
			if (ImGui.BeginChild("##history", new Vector2(0, -(inputHeight + style.ItemSpacing.Y)), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
			{
				for (var i = 0; i < history.Count; i++)
					ScriptEntryImGui(history[i], i);
				if (scriptRunning)
					ImGui.TextDisabled("running...");
				if (history.Count != scriptHistoryShown)
				{
					ImGui.SetScrollHereY(1);
					scriptHistoryShown = history.Count;
				}
			}
			ImGui.EndChild();

			if (ImGui.InputTextMultiline("##input", ref scriptInput, 1 << 16, new Vector2(-1, inputHeight), ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AllowTabInput)
				&& !scriptRunning && scriptInput.Trim().Length > 0)
			{
				scriptRunning = true;
				var code = scriptInput;
				Task.Run(async () =>
				{
					await DevScript.Execute(code, fromClaude: false);
					scriptRunning = false;
				});
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(OperatingSystem.IsMacOS() ? "Cmd+Enter runs" : "Ctrl+Enter runs");
			ImGui.PopFont();
		}

		void ScriptEntryImGui(DevScript.Entry entry, int index)
		{
			ImGui.PushID(index);
			var start = ImGui.GetCursorPos();
			ImGui.TextColored(entry.FromClaude ? ClaudeTextColor : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], entry.FromClaude ? "claude>" : "you>");
			if (entry.Overlays.Count > 0)
			{
				ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
				if (ImGui.SmallButton(EditorIcons.Visible))
					foreach (var (draw, seconds) in entry.Overlays)
						scriptOverlays.Add((draw, Time.Elapsed + TimeSpan.FromSeconds(seconds)));
				ImGui.PopFont();
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Show its overlays again");
			}
			var tagBottom = ImGui.GetCursorPosY();

			ImGui.SetCursorPos(start + new Vector2(ImGui.CalcTextSize("claude>").X + ImGui.GetStyle().ItemSpacing.X, 0));
			ImGui.BeginGroup();
			ImGui.TextUnformatted(entry.Code);
			if (ImGui.IsItemHovered())
			{
				ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
				ImGui.SetTooltip("Click to edit");
			}
			if (ImGui.IsItemClicked())
				scriptInput = entry.Code;

			ImGui.PushStyleColor(ImGuiCol.Text, entry.Failed ? new Vector4(1, 0.4f, 0.4f, 1) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
			ImGui.TextUnformatted(entry.Expanded ? entry.Result : entry.Preview);
			ImGui.PopStyleColor();
			if (entry.Preview.Length != entry.Result.Length && ImGui.SmallButton(entry.Expanded ? "less" : $"{entry.HiddenLines} more lines"))
				entry.Expanded = !entry.Expanded;
			ImGui.EndGroup();
			if (ImGui.GetCursorPosY() < tagBottom)
				ImGui.Dummy(new Vector2(0, tagBottom - ImGui.GetCursorPosY()));
			ImGui.Separator();
			ImGui.PopID();
		}
	}

	// `cs <code>` dev command and the Scripting window: C# against the live session, REPL-style --
	// variables and helpers persist across calls until `cs-reset`, shared by Claude (over the pipe) and
	// you. Each fresh session starts by running DevScripts/helpers.csx. Accessibility is off: Roslyn
	// binds with IgnoreAccessibility, and each emitted submission is patched with IgnoresAccessChecksTo
	// so the runtime agrees.
	static class DevScript
	{
		public class Entry(bool fromClaude, string code)
		{
			public readonly bool FromClaude = fromClaude;
			public readonly string Code = code;
			public string Result = "";
			public bool Failed, Expanded;
			public bool Seen = !fromClaude;
			public readonly List<(Action<ImDrawListPtr, Func<Vector2, Vector2>> Draw, float Seconds)> Overlays = [];

			const int PreviewLines = 8;
			string? preview;
			public string Preview => preview ??= Result.Split('\n') is { Length: > PreviewLines } lines ? string.Join('\n', lines[..PreviewLines]) : Result;
			public int HiddenLines => Result.AsSpan().Count('\n') + 1 - PreviewLines;
		}

		// Game thread only. Persisted so you and Claude can both look back across launches.
		public static readonly List<Entry> History = LoadHistory();
		internal static Entry? Running;
		static readonly string historyPath = Path.GetFullPath("obj/dev-script-history.jsonl");
		static readonly SemaphoreSlim gate = new(1, 1);
		static readonly string helpersPath = Path.GetFullPath("DevScripts/helpers.csx");

		static ScriptState<object?>? state;
		static Manager.ScriptGlobals? globals;
		static ScriptOptions? options;

		static readonly FieldInfo lazyCompilation = typeof(Script).GetField("_lazyCompilation", BindingFlags.NonPublic | BindingFlags.Instance)!;
		static readonly FieldInfo lazyExecutor = typeof(Script<object?>).GetField("_lazyExecutor", BindingFlags.NonPublic | BindingFlags.Instance)!;
		static readonly Type binderFlags = typeof(CSharpCompilation).Assembly.GetType("Microsoft.CodeAnalysis.CSharp.BinderFlags")!;
		static readonly MethodInfo withBinderFlags = typeof(CSharpCompilationOptions).GetMethod("WithTopLevelBinderFlags", BindingFlags.NonPublic | BindingFlags.Instance)!;
		static readonly PropertyInfo topLevelBinderFlags = typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags", BindingFlags.NonPublic | BindingFlags.Instance)!;
		static readonly Submissions submissions = new();

		// Later submissions reference earlier ones by name; everything else falls through to the default context.
		class Submissions() : AssemblyLoadContext("dev-scripts")
		{
			public readonly Dictionary<string, Assembly> ByName = [];
			protected override Assembly? Load(AssemblyName name) => ByName.GetValueOrDefault(name.Name!);
		}

		public static string Reset()
		{
			state = null;
			globals = null;
			return "cs state cleared";
		}

		// Compiles off the game thread and runs on it, one call at a time so each chains from the last one's state.
		public static async Task<string> Execute(string code, bool fromClaude)
		{
			await gate.WaitAsync();
			try
			{
				return await DevHost.OnGameThread(Compile(new Entry(fromClaude, code)));
			}
			finally
			{
				gate.Release();
			}
		}

		static Func<Manager, string> Compile(Entry entry)
		{
			try
			{
				options ??= ScriptOptions.Default
					.AddReferences(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location.Length > 0))
					.AddImports("System", "System.Linq", "System.Collections.Generic", "System.Numerics", "System.Text", "Foster.Framework", "Kit", "Myth");
				var previous = state;
				var head = previous?.Script ?? CSharpScript.Create<object?>(File.ReadAllText(helpersPath), options.WithFilePath(helpersPath), typeof(Manager.ScriptGlobals));
				var script = head.ContinueWith<object?>(entry.Code.Trim() is "help" or "?" ? "Help()" : entry.Code, options);
				if (((previous == null ? Emit(head) : null) ?? Emit(script)) is { } errors)
					return _ => Record(entry, errors, failed: true);
				var endsInValue = EndsInValue(script);
				return manager => Run(manager, entry, script, previous, endsInValue);
			}
			catch (Exception e)
			{
				return _ => Record(entry, $"error: {e}", failed: true);
			}
		}

		static string Run(Manager manager, Entry entry, Script<object?> script, ScriptState<object?>? previous, bool endsInValue)
		{
			globals ??= new(manager);
			globals.Output.Clear();
			Running = entry;
			try
			{
				state = (previous == null ? script.RunAsync(globals) : script.RunFromAsync(previous)).GetAwaiter().GetResult();
			}
			catch (Exception e)
			{
				return Record(entry, $"{globals.Output}error: {e}", failed: true);
			}
			finally
			{
				Running = null;
			}
			if (endsInValue)
				globals.Output.Append(Describe(state.ReturnValue));
			else if (globals.Output.Length == 0)
				globals.Output.Append("ok");
			return Record(entry, globals.Output.ToString().TrimEnd(), failed: false);
		}

		static string Record(Entry entry, string result, bool failed)
		{
			entry.Result = result;
			entry.Failed = failed;
			History.Add(entry);
			try
			{
				File.AppendAllText(historyPath, new JsonObject
				{
					["claude"] = entry.FromClaude,
					["code"] = entry.Code,
					["result"] = result.Length > 4000 ? result[..4000] : result,
					["failed"] = failed,
				}.ToJsonString() + "\n");
			}
			catch (IOException) { }
			return result;
		}

		static List<Entry> LoadHistory()
		{
			try
			{
				return File.ReadLines(historyPath).TakeLast(200).Select(line => JsonNode.Parse(line)!)
					.Select(j => new Entry((bool)j["claude"]!, (string)j["code"]!) { Result = (string?)j["result"] ?? "", Failed = (bool?)j["failed"] ?? false, Seen = true })
					.ToList();
			}
			catch (Exception)
			{
				return [];
			}
		}

		// A trailing non-void expression without a semicolon is the REPL's answer, even when it's null.
		static bool EndsInValue(Script script)
		{
			var compilation = script.GetCompilation();
			var tree = compilation.SyntaxTrees.Single();
			return tree.GetRoot().ChildNodes().LastOrDefault() is GlobalStatementSyntax { Statement: ExpressionStatementSyntax { SemicolonToken.IsMissing: true } statement }
				&& compilation.GetSemanticModel(tree).GetTypeInfo(statement.Expression).Type?.SpecialType != SpecialType.System_Void;
		}

		public static string Help()
		{
			var sb = new StringBuilder();
			void Section(string title, IEnumerable<(string Name, string Text)> rows)
			{
				sb.AppendLine(title);
				foreach (var (name, text) in rows)
					sb.AppendLine($"  {name,-30} {text}");
			}
			string Signature(string name, IEnumerable<(string Name, bool Optional)> parameters) =>
				$"{name}({string.Join(", ", parameters.Select(p => p.Optional ? p.Name + "?" : p.Name))})";

			Section("Globals", typeof(Manager.ScriptGlobals).GetMembers().Select(m => (Member: m, Help: m.GetCustomAttribute<DescriptionAttribute>()))
				.Where(m => m.Help != null).OrderBy(m => m.Member is MethodInfo)
				.Select(m => (m.Member switch
				{
					MethodInfo method => Signature(method.Name, method.GetParameters().Select(p => (p.Name!, p.HasDefaultValue))),
					PropertyInfo property => $"{property.Name} : {property.PropertyType.Name}",
					FieldInfo field => $"{field.Name} : {field.FieldType.Name}",
					_ => m.Member.Name,
				}, m.Help!.Description)));

			// A helper's text is the comment block right above it.
			var helpers = CSharpSyntaxTree.ParseText(File.ReadAllText(helpersPath), CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
			Section("\nHelpers (DevScripts/helpers.csx)", helpers.GetRoot().ChildNodes().OfType<MethodDeclarationSyntax>().Select(method =>
			{
				var comment = new List<string>();
				var newlines = 0;
				foreach (var trivia in method.GetLeadingTrivia())
				{
					if (trivia.IsKind(SyntaxKind.EndOfLineTrivia) && ++newlines > 1)
						comment.Clear();
					else if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
					{
						comment.Add(trivia.ToString().TrimStart('/', ' '));
						newlines = 0;
					}
				}
				return (Signature($"{method.Identifier}{method.TypeParameterList}", method.ParameterList.Parameters.Select(p => (p.Identifier.Text, p.Default != null))),
					string.Join(" ", comment));
			}));

			if (state?.Variables.Length > 0)
				Section("\nSession variables", state.Variables.Reverse().DistinctBy(v => v.Name).Reverse()
					.Select(v => ($"{v.Name} : {v.Type.Name}", Short(v.Value))));
			return sb.Append("\n`help` or `?` shows this. Ctrl/Cmd+Enter runs.").ToString();
		}

		static string? Emit(Script script)
		{
			var compilation = (CSharpCompilation)script.GetCompilation();
			var compileOptions = compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All);
			var flags = Convert.ToUInt32(topLevelBinderFlags.GetValue(compileOptions)) | Convert.ToUInt32(Enum.Parse(binderFlags, "IgnoreAccessibility"));
			compilation = compilation.WithOptions((CSharpCompilationOptions)withBinderFlags.Invoke(compileOptions, [Enum.ToObject(binderFlags, flags)])!);
			lazyCompilation.SetValue(script, compilation);

			using var pe = new MemoryStream();
			var result = compilation.Emit(pe);
			if (!result.Success)
				return "error: " + string.Join('\n', result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

			pe.Position = 0;
			using var assembly = AssemblyDefinition.ReadAssembly(pe);
			var module = assembly.MainModule;
			var ctor = module.ImportReference(typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)]));
			foreach (var reference in compilation.ReferencedAssemblyNames)
				assembly.CustomAttributes.Add(new CustomAttribute(ctor) { ConstructorArguments = { new(module.TypeSystem.String, reference.Name) } });
			using var patched = new MemoryStream();
			assembly.Write(patched);
			patched.Position = 0;
			var loaded = submissions.LoadFromStream(patched);
			submissions.ByName[loaded.GetName().Name!] = loaded;

			var entry = compilation.GetEntryPoint(default)!;
			var factory = loaded.GetType(entry.ContainingType.MetadataName)!.GetMethod(entry.MetadataName)!;
			lazyExecutor.SetValue(script, factory.CreateDelegate<Func<object[], Task<object?>>>());
			return null;
		}

		// ToString when the type has a meaningful one, otherwise every instance field (private and inherited too).
		public static string Describe(object? value)
		{
			if (value is null or string)
				return value as string ?? "null";
			if (value is IEnumerable items)
			{
				var sb = new StringBuilder();
				var count = 0;
				foreach (var item in items)
					if (count++ < 50)
						sb.AppendLine(Short(item));
				return sb.Append($"({count} items)").ToString();
			}

			var type = value.GetType();
			var declaring = type.GetMethod("ToString", Type.EmptyTypes)!.DeclaringType;
			if (declaring != typeof(object) && declaring != typeof(ValueType))
				return value.ToString()!;

			var dump = new StringBuilder(type.Name).AppendLine(" {");
			for (var t = type; t != null && t != typeof(object); t = t.BaseType)
				foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					dump.AppendLine($"  {field.Name.Replace("k__BackingField", "").Trim('<', '>')} = {Short(field.GetValue(value))}");
			return dump.Append('}').ToString();
		}

		public static string Short(object? value)
		{
			var text = value is Actor actor ? $"{actor.GetType().Name}:{actor.ActorId} @{actor.Position - (actor.Room?.WorldBounds.TopLeft ?? default)}"
				: value is IEnumerable items and not string
				? $"[{string.Join(", ", items.Cast<object?>().Take(8))}]"
				: value?.ToString()?.ReplaceLineEndings(" ") ?? "null";
			return text.Length > 120 ? text[..120] + "…" : text;
		}
	}
}
#endif
