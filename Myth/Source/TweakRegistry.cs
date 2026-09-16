#if !DEBUG || BROWSER
namespace Myth;

static class TweakRegistry
{
	public static bool Hovered => false;
}
#else
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;

namespace Myth;

// Debug-only backing store + ImGui panel for `.T()` tweakable literals (see Tweak.cs). Each `.T()`
// call site self-registers on first execution, keyed by (file, line). The panel lets you search all
// seen tweaks, "watch" (pin) any of them, and it auto-surfaces every tweak that lives in the current
// `git diff` — parsed straight from source so a diff'd tweak shows up even before its line has run.
static class TweakRegistry
{
	internal sealed class Entry
	{
		public string File = "";      // absolute, normalized
		public int Line;
		public string Expr = "";      // the literal as written, e.g. "16f"
		public string Context = "";   // enclosing member name, auto-detected from source
		public bool IsInt;
		public float Default;         // current source literal (refreshed each execution)
		public float Value;           // slider override
		public float Min, Max;
		public bool HasRange;         // range came from an explicit .T(min, max)
		public bool HasOverride;      // slider has been dragged
		public bool Watched;          // pinned by the user
		public bool InDiff;           // sits inside the current git diff
		public bool Seen;             // executed at least once this session
		public bool WritePending;     // source was edited; waiting for hot reload to run the new literal
		public string PendingExpr = "";
		public string ShortFile = "";
	}

	static readonly Dictionary<(string, int), Entry> entries = [];
	static readonly List<Entry> sortedEntries = [];
	static bool sortedEntriesDirty = true;

	public static bool Open = false;

	// --- resolution (called from Tweak.T on the hot path) ---

	public static float ResolveFloat(string file, int line, string expr, float value, float? min, float? max)
	{
		var e = GetOrAdd(file, line, expr, value, isInt: false, min, max);
		if (!e.Seen)
			e.Seen = true;
		if (UpdatePendingWrite(e, expr, value))
			return e.Value;
		if (!e.HasOverride)
			e.Default = value; // track live source value (hot reload) until the user overrides
		return e.HasOverride ? e.Value : value;
	}

	public static int ResolveInt(string file, int line, string expr, int value, int? min, int? max)
	{
		var e = GetOrAdd(file, line, expr, value, isInt: true, min, max);
		if (!e.Seen)
			e.Seen = true;
		if (UpdatePendingWrite(e, expr, value))
			return (int)MathF.Round(e.Value);
		if (!e.HasOverride)
			e.Default = value;
		return e.HasOverride ? (int)MathF.Round(e.Value) : value;
	}

	static Entry GetOrAdd(string file, int line, string expr, float value, bool isInt, float? min, float? max)
	{
		var key = (file, line);
		if (!entries.TryGetValue(key, out var e))
		{
			var normalized = Normalize(file);
			e = new Entry
			{
				File = normalized,
				Line = line,
				Expr = expr,
				IsInt = isInt,
				Default = value,
				Value = value,
				Context = ResolveContext(normalized, line),
				ShortFile = Path.GetFileName(normalized)
			};
			ApplyRange(e, value, isInt, min, max);
			entries[key] = e;
			sortedEntriesDirty = true;
		}
		else if (min is { } lo && max is { } hi)
		{
			if (!e.HasRange || e.Min != lo || e.Max != hi)
			{
				e.Min = lo;
				e.Max = hi;
				e.HasRange = true;
			}
		}
		if (!string.IsNullOrEmpty(expr) && !e.WritePending && e.Expr != expr)
			e.Expr = expr;
		return e;
	}

	static bool UpdatePendingWrite(Entry e, string expr, float value)
	{
		if (!e.WritePending)
			return false;
		if (expr == e.PendingExpr)
		{
			e.WritePending = false;
			e.PendingExpr = "";
			e.Expr = expr;
			e.Default = value;
			e.Value = value;
			e.HasOverride = false;
			return false;
		}
		return true;
	}

	static void ApplyRange(Entry e, float value, bool isInt, float? min, float? max)
	{
		if (min is { } lo && max is { } hi)
		{
			e.Min = lo; e.Max = hi; e.HasRange = true;
			return;
		}
		// Heuristic: bracket 0 and 4x the value on the side the value lies. Keeps the slider useful
		// without an explicit range; the user can add .T(min, max) when the guess is wrong.
		if (value == 0)
		{
			e.Min = 0;
			e.Max = isInt ? 10 : 1;
		}
		else if (value > 0)
		{
			e.Min = 0;
			e.Max = value * 4;
		}
		else
		{
			e.Min = value * 4;
			e.Max = 0;
		}
	}

	static string Normalize(string path)
	{
		if (Path.IsPathFullyQualified(path))
			return path;
		try { return Path.GetFullPath(path); }
		catch { return path; }
	}

	// A member declaration: `<type> Name(` / `<type> Name =>` / `<type> Name =` / `<type> Name {`.
	static readonly Regex DeclRx = new(
		@"(?:void|bool|byte|int|uint|long|float|double|string|var|const|Vector2|Vector3|Color|Point2|Rect|RectInt|[A-Z][A-Za-z0-9_<>?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|=>|=|\{)",
		RegexOptions.Compiled);

	static readonly Dictionary<string, string[]?> fileLineCache = [];

	// Best-effort "name" for an inline literal: scan upward for the nearest enclosing member/local
	// declaration and take its identifier. Falls back to empty (panel then shows file:line instead).
	static string ResolveContext(string file, int line)
	{
		if (!fileLineCache.TryGetValue(file, out var lines))
		{
			try { lines = File.ReadAllLines(file); }
			catch { lines = null; }
			fileLineCache[file] = lines;
		}
		if (lines == null)
			return "";
		if (line < 1 || line > lines.Length)
			return "";
		for (var i = line - 1; i >= Math.Max(0, line - 40); i--)
		{
			var m = DeclRx.Match(lines[i]);
			if (m.Success)
				return m.Groups[1].Value;
		}
		return "";
	}

	// --- git diff scan ---

	static string? repoRoot;
	static bool scannedOnce;

	static string? RepoRoot()
	{
		if (repoRoot != null)
			return repoRoot.Length == 0 ? null : repoRoot;
		repoRoot = RunGit(AppContext.BaseDirectory, "rev-parse --show-toplevel")?.Trim() ?? "";
		return repoRoot.Length == 0 ? null : repoRoot;
	}

	static readonly Regex HunkRx = new(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);
	// A numeric literal immediately followed by `.T(` — the tweak marker.
	static readonly Regex MarkerRx = new(@"(-?\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdD]?)\s*\.\s*T\s*\(", RegexOptions.Compiled);

	public static void ScanDiff()
	{
		foreach (var e in entries.Values)
			e.InDiff = false;

		var root = RepoRoot();
		if (root == null)
			return;

		var diff = RunGit(root, "diff --unified=0 --no-color HEAD");
		if (diff == null)
			return;

		// Collect changed line ranges (new-side) per file.
		var ranges = new Dictionary<string, List<(int start, int end)>>();
		string? current = null;
		foreach (var raw in diff.Split('\n'))
		{
			if (raw.StartsWith("+++ b/", StringComparison.Ordinal))
			{
				var rel = raw[6..].Trim();
				current = rel == "/dev/null" ? null : Normalize(Path.Combine(root, rel));
				continue;
			}
			if (current == null)
				continue;
			var m = HunkRx.Match(raw);
			if (!m.Success)
				continue;
			var start = int.Parse(m.Groups[1].Value);
			var count = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
			if (count <= 0)
				continue;
			if (!ranges.TryGetValue(current, out var list))
				ranges[current] = list = new();
			list.Add((start, start + count - 1));
		}

		// Pre-register / flag every `.T(` marker that falls inside a changed range.
		foreach (var (file, list) in ranges)
		{
			var attr = File.GetAttributes(file);
			if (attr.HasFlag(FileAttributes.Directory)) {
				continue;
			}

			string[] lines;
			try { lines = File.ReadAllLines(file); }
			catch
			{
				Console.WriteLine("ScanDiff: Error reading file: " + file);
				continue;
			}

			foreach (var (start, end) in list)
				for (var ln = start; ln <= end && ln <= lines.Length; ln++)
				{
					foreach (Match mk in MarkerRx.Matches(lines[ln - 1]))
					{
						var text = mk.Groups[1].Value;
						if (!TryParseLiteral(text, out var value, out var isInt))
							continue;
						var e = GetOrAdd(file, ln, text, value, isInt, null, null);
						e.InDiff = true;
						if (!e.Seen)
							e.Default = value; // unseen: trust the parsed source literal
					}
				}
		}
	}

	static bool TryParseLiteral(string text, out float value, out bool isInt)
	{
		var trimmed = text.Replace("_", "").TrimEnd('f', 'F', 'd', 'D');
		isInt = !text.Contains('.') && !text.Contains('e') && !text.Contains('E')
			&& !text.EndsWith('f') && !text.EndsWith('F') && !text.EndsWith('d') && !text.EndsWith('D');
		return float.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out value);
	}

	static string? RunGit(string workingDir, string args)
	{
		try
		{
			using var p = Process.Start(new ProcessStartInfo("git", args)
			{
				WorkingDirectory = workingDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			if (p == null)
				return null;
			var output = p.StandardOutput.ReadToEnd();
			p.WaitForExit(2000);
			return p.ExitCode == 0 ? output : null;
		}
		catch { return null; }
	}

	// --- ImGui panel ---

	static string search = "";
	static bool showAll;
	public static bool Hovered { get; private set; }

	public static void DrawPanel()
	{
		if (!scannedOnce)
		{
			ScanDiff();
			scannedOnce = true;
		}

		if (!Open)
		{
			Hovered = false;
			return;
		}

		// NoFocusOnAppearing: opening the panel (incl. via the T hotkey) must not steal keyboard focus,
		// which is what would otherwise nav-focus the first item (the search box) and swallow keystrokes.
		if (!ImGui.Begin("Tweaks", ref Open, ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar))
		{
			Hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
			ImGui.End();
			return;
		}
		Hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);

		DrawTitleBarControls();

		var ordered = SortedEntries();

		var primaryCount = CountRows(ordered, static e => e.Watched || e.InDiff);
		var searching = search.Length > 0;
		if (searching || showAll || primaryCount == 0)
		{
			DrawSection(ordered, static e => true, filtered: searching);
		}
		else
		{
			DrawSection(ordered, static e => e.Watched, filtered: false);
			DrawSection(ordered, static e => e.InDiff && !e.Watched, filtered: false);
			if (ImGui.SmallButton("All..."))
				showAll = true;
		}

		ImGui.End();
	}

#if EDITOR
	const string RESET_ALL_TEXT = EditorIcons.Reset + " Reset All";
#else
	const string RESET_ALL_TEXT = "Reset All";
#endif

	static void DrawTitleBarControls()
	{
		var style = ImGui.GetStyle();
		var title =
#if EDITOR
			EditorIcons.Sliders + "  Tweaks";
#else
			"Tweaks";
#endif
		var titleWidth = ImGui.CalcTextSize(title).X;
		var menuWidth = 30f;
		var searchWidth = Math.Clamp(ImGui.GetContentRegionAvail().X - titleWidth - menuWidth - style.ItemSpacing.X * 3, 90f, 240f);
		var headerStart = ImGui.GetCursorPosY();
		ImGui.SetCursorPosY(headerStart + MathF.Max(0, (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f));
		ImGui.TextUnformatted(title);
		ImGui.SameLine();
#if EDITOR
		ImGui.SetCursorPosY(headerStart);
		EditorSearchBar.Draw("##tweaksearch", ref search, 128, "Field name", searchWidth);
#else
		ImGui.SetNextItemWidth(searchWidth);
		ImGui.InputTextWithHint("##tweaksearch", "Field name", ref search, 128);
#endif
		ImGui.SameLine();
		ImGui.SetCursorPosY(headerStart);
		if (ImGui.Button("...##tweakmenu", new Vector2(menuWidth, 0)))
			ImGui.OpenPopup("##tweakmenu");
		if (ImGui.BeginPopup("##tweakmenu"))
		{
			if (ImGui.MenuItem("Rescan diff"))
				ScanDiff();
			if (ImGui.MenuItem(RESET_ALL_TEXT))
				ResetAll();
			ImGui.EndPopup();
		}
		ImGui.Separator();
	}

	static void ResetAll()
	{
		foreach (var e in entries.Values)
		{
			e.HasOverride = false;
			e.WritePending = false;
			e.PendingExpr = "";
		}
	}

	static List<Entry> SortedEntries()
	{
		if (!sortedEntriesDirty)
			return sortedEntries;
		sortedEntries.Clear();
		sortedEntries.AddRange(entries.Values);
		sortedEntries.Sort(static (a, b) =>
		{
			var f = string.CompareOrdinal(a.ShortFile, b.ShortFile);
			return f != 0 ? f : a.Line.CompareTo(b.Line);
		});
		sortedEntriesDirty = false;
		return sortedEntries;
	}

	static int CountRows(List<Entry> ordered, Func<Entry, bool> include)
	{
		var count = 0;
		foreach (var e in ordered)
			if (include(e))
				count++;
		return count;
	}

	static void DrawSection(List<Entry> ordered, Func<Entry, bool> include, bool filtered)
	{
		foreach (var e in ordered)
		{
			if (!include(e))
				continue;
			if (filtered && !SearchMatches(e))
				continue;
			DrawRow(e);
		}
	}

	static bool SearchMatches(Entry e)
		=> search.Length == 0 ||
			e.Context.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			e.Expr.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			e.ShortFile.Contains(search, StringComparison.OrdinalIgnoreCase);

	// Layout: [watch] [name column] [slider fills remaining] [write] [reset]. Name is the auto-detected
	// enclosing member (plus the literal to disambiguate multiple tweaks in one member); falls back
	// to file:line. Kept in a fixed-width column so the sliders line up across rows.
	const float NameColumnWidth = 240f;
	const float MinNameColumnWidth = 120f;
	const float MinSliderWidth = 120f;
	const float IconButtonWidth = 24f;
	const float DefaultMarkerAlpha = 0.7f;
	const float DefaultMarkerFloatEpsilon = 0.0001f;

	static void DrawRow(Entry e)
	{
		ImGui.PushID($"{e.File}:{e.Line}");
		var style = ImGui.GetStyle();
		var rowRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;

		if (DrawIconButton(
#if EDITOR
			EditorIcons.Visible
#else
			"*"
#endif
			, e.Watched ? "Unwatch" : "Watch", e.Watched ? 1f : 0.3f))
			e.Watched = !e.Watched;
		ImGui.SameLine();

		var buttonsWidth = IconButtonWidth * 2 + style.ItemSpacing.X * 2;
		var remaining = rowRight - ImGui.GetCursorPosX();
		var nameWidth = Math.Clamp(remaining - MinSliderWidth - buttonsWidth, MinNameColumnWidth, NameColumnWidth);
		var name = e.Context.Length > 0 ? e.Context : $"{e.ShortFile}:{e.Line}";
		ImGui.BeginChild("##name", new Vector2(nameWidth, ImGui.GetFrameHeight()), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
		ImGui.SetCursorPosY(MathF.Max(0, (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f));
		ImGui.TextUnformatted(name);
		var nameHovered = ImGui.IsItemHovered() || ImGui.IsWindowHovered();
		ImGui.EndChild();
		if (nameHovered)
			ImGui.SetTooltip($"{e.ShortFile}:{e.Line}{(e.Seen ? "" : "  (not yet run)")}");
		ImGui.SameLine();

		var sliderWidth = Math.Max(MinSliderWidth, rowRight - ImGui.GetCursorPosX() - buttonsWidth);
		ImGui.SetNextItemWidth(sliderWidth);
		if (e.IsInt)
		{
			var iv = (int)MathF.Round(e.HasOverride ? e.Value : e.Default);
			if (ImGui.SliderInt("##slider", ref iv, (int)e.Min, (int)e.Max))
			{
				e.Value = iv;
				e.HasOverride = true;
			}
		}
		else
		{
			var fv = e.HasOverride ? e.Value : e.Default;
			if (ImGui.SliderFloat("##slider", ref fv, e.Min, e.Max))
			{
				e.Value = fv;
				e.HasOverride = true;
			}
		}
		DrawDefaultMarker(e);

		ImGui.SameLine();
		if (e.HasOverride)
		{
			if (DrawIconButton(
#if EDITOR
				EditorIcons.Pencil
#else
				"W"
#endif
				, "Write value to source"))
				WriteBack(e);
		}
		else
		{
			ImGui.Dummy(new Vector2(IconButtonWidth, ImGui.GetFrameHeight()));
		}
		ImGui.SameLine();
		if (e.HasOverride || e.WritePending)
		{
			if (DrawIconButton(
#if EDITOR
				EditorIcons.Reset
#else
				"="
#endif
				, "Reset"))
			{
				e.HasOverride = false;
				e.WritePending = false;
				e.PendingExpr = "";
			}
		}
		else
		{
			ImGui.Dummy(new Vector2(IconButtonWidth, ImGui.GetFrameHeight()));
		}

		ImGui.PopID();
	}

	static bool DrawIconButton(string label, string tooltip, float textAlpha = 1f)
	{
		ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
		if (textAlpha < 1f)
			ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text] * textAlpha);
		var pressed = ImGui.Button(label, new Vector2(IconButtonWidth, 0));
		if (textAlpha < 1f)
			ImGui.PopStyleColor();
		ImGui.PopStyleColor(3);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(tooltip);
		return pressed;
	}

	static void DrawDefaultMarker(Entry e)
	{
		if (e.Max <= e.Min)
			return;
		var current = e.HasOverride ? e.Value : e.Default;
		if (e.IsInt ? (int)MathF.Round(current) == (int)MathF.Round(e.Default) : MathF.Abs(current - e.Default) <= DefaultMarkerFloatEpsilon)
			return;
		var t = Math.Clamp((e.Default - e.Min) / (e.Max - e.Min), 0, 1);
		var min = ImGui.GetItemRectMin();
		var max = ImGui.GetItemRectMax();
		var x = MathF.Round(min.X + (max.X - min.X) * t);
		ImGui.GetWindowDrawList().AddLine(
			new Vector2(x, min.Y + 3),
			new Vector2(x, max.Y - 3),
			ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Text] * DefaultMarkerAlpha),
			1);
	}

	static void WriteBack(Entry e)
	{
		string[] lines;
		try { lines = File.ReadAllLines(e.File); }
		catch (Exception ex)
		{
			Console.WriteLine($"Tweak write failed: {e.File}: {ex.Message}");
			return;
		}

		if (e.Line < 1 || e.Line > lines.Length)
			return;

		var line = lines[e.Line - 1];
		var match = MarkerRx.Match(line);
		while (match.Success)
		{
			if (match.Groups[1].Value == e.Expr)
				break;
			match = match.NextMatch();
		}
		if (!match.Success)
		{
			Console.WriteLine($"Tweak write skipped: {e.ShortFile}:{e.Line} no longer contains {e.Expr}.T(");
			return;
		}

		var value = FormatValue(e);
		lines[e.Line - 1] = line[..match.Groups[1].Index] + value + line[(match.Groups[1].Index + match.Groups[1].Length)..];
		try { File.WriteAllLines(e.File, lines); }
		catch (Exception ex)
		{
			Console.WriteLine($"Tweak write failed: {e.File}: {ex.Message}");
			return;
		}

		e.Expr = value;
		e.Default = e.IsInt ? (int)MathF.Round(e.Value) : e.Value;
		e.HasOverride = false;
		e.WritePending = true;
		e.PendingExpr = value;
		e.InDiff = true;
	}

	static string FormatValue(Entry e)
	{
		if (e.IsInt)
			return ((int)MathF.Round(e.Value)).ToString(CultureInfo.InvariantCulture);

		var suffix = e.Expr.EndsWith('f') || e.Expr.EndsWith('F') ? "f" : "";
		return e.Value.ToString("G9", CultureInfo.InvariantCulture) + suffix;
	}
}
#endif
