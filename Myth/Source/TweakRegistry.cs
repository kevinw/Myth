#if DEBUG
using System.Diagnostics;
using System.IO;
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
		public string ShortFile => Path.GetFileName(File);
	}

	static readonly Dictionary<(string, int), Entry> entries = new();

	public static bool Open = true;

	// --- resolution (called from Tweak.T on the hot path) ---

	public static float ResolveFloat(string file, int line, string expr, float value, float? min, float? max)
	{
		var e = GetOrAdd(file, line, expr, value, isInt: false, min, max);
		e.IsInt = false;
		e.Seen = true;
		if (!e.HasOverride)
			e.Default = value; // track live source value (hot reload) until the user overrides
		return e.HasOverride ? e.Value : value;
	}

	public static int ResolveInt(string file, int line, string expr, int value, int? min, int? max)
	{
		var e = GetOrAdd(file, line, expr, value, isInt: true, min, max);
		e.IsInt = true;
		e.Seen = true;
		if (!e.HasOverride)
			e.Default = value;
		return e.HasOverride ? (int)MathF.Round(e.Value) : value;
	}

	static Entry GetOrAdd(string file, int line, string expr, float value, bool isInt, float? min, float? max)
	{
		file = Normalize(file);
		var key = (file, line);
		if (!entries.TryGetValue(key, out var e))
		{
			e = new Entry { File = file, Line = line, Expr = expr, IsInt = isInt, Default = value, Value = value };
			e.Context = ResolveContext(file, line);
			ApplyRange(e, value, isInt, min, max);
			entries[key] = e;
		}
		else if (min is { } lo && max is { } hi)
		{
			e.Min = lo; e.Max = hi; e.HasRange = true;
		}
		if (!string.IsNullOrEmpty(expr))
			e.Expr = expr;
		return e;
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
		try { return Path.GetFullPath(path); }
		catch { return path; }
	}

	// A member declaration: `<type> Name(` / `<type> Name =>` / `<type> Name =` / `<type> Name {`.
	static readonly Regex DeclRx = new(
		@"(?:void|bool|byte|int|uint|long|float|double|string|var|const|Vector2|Vector3|Color|Point2|Rect|RectInt|[A-Z][A-Za-z0-9_<>?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|=>|=|\{)",
		RegexOptions.Compiled);

	// Best-effort "name" for an inline literal: scan upward for the nearest enclosing member/local
	// declaration and take its identifier. Falls back to empty (panel then shows file:line instead).
	static string ResolveContext(string file, int line)
	{
		string[] lines;
		try { lines = File.ReadAllLines(file); }
		catch { return ""; }
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
			string[] lines;
			try { lines = File.ReadAllLines(file); }
			catch { continue; }

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

	public static void DrawPanel()
	{
		if (!scannedOnce)
		{
			ScanDiff();
			scannedOnce = true;
		}

		if (!Open)
			return;

		if (!ImGui.Begin("Tweaks", ref Open))
		{
			ImGui.End();
			return;
		}

		ImGui.InputText("##tweaksearch", ref search, 128);
		ImGui.SameLine();
		if (ImGui.SmallButton("Rescan diff"))
			ScanDiff();
		ImGui.SameLine();
		if (ImGui.SmallButton("Reset all"))
			foreach (var e in entries.Values)
				e.HasOverride = false;

		var ordered = new List<Entry>(entries.Values);
		ordered.Sort(static (a, b) =>
		{
			var f = string.CompareOrdinal(a.ShortFile, b.ShortFile);
			return f != 0 ? f : a.Line.CompareTo(b.Line);
		});

		DrawSection("Watched", ordered, static e => e.Watched, filtered: false);
		DrawSection("In git diff", ordered, static e => e.InDiff && !e.Watched, filtered: false);

		ImGui.Separator();
		if (ImGui.CollapsingHeader("All"))
			DrawSection(null, ordered, static e => !e.Watched && !e.InDiff, filtered: true);

		ImGui.End();
	}

	static void DrawSection(string? header, List<Entry> ordered, Func<Entry, bool> include, bool filtered)
	{
		var any = false;
		foreach (var e in ordered)
		{
			if (!include(e))
				continue;
			if (filtered && search.Length > 0
				&& !e.Expr.Contains(search, StringComparison.OrdinalIgnoreCase)
				&& !e.ShortFile.Contains(search, StringComparison.OrdinalIgnoreCase))
				continue;
			if (header != null && !any)
			{
				ImGui.SeparatorText(header);
				any = true;
			}
			DrawRow(e);
		}
	}

	// Layout: [watch] [name column] [slider fills remaining] [reset]. Name is the auto-detected
	// enclosing member (plus the literal to disambiguate multiple tweaks in one member); falls back
	// to file:line. Kept in a fixed-width column so the sliders line up across rows.
	const float NameColumnWidth = 240f;
	const float ResetButtonWidth = 52f;

	static void DrawRow(Entry e)
	{
		ImGui.PushID($"{e.File}:{e.Line}");

		var watched = e.Watched;
		if (ImGui.Checkbox("##watch", ref watched))
			e.Watched = watched;
		ImGui.SameLine();

		var name = e.Context.Length > 0 ? $"{e.Context} · {e.Expr}" : $"{e.ShortFile}:{e.Line} · {e.Expr}";
		var nameStart = ImGui.GetCursorPosX();
		ImGui.TextUnformatted(name);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip($"{e.ShortFile}:{e.Line}{(e.Seen ? "" : "  (not yet run)")}");
		ImGui.SameLine();
		ImGui.SetCursorPosX(nameStart + NameColumnWidth);

		ImGui.SetNextItemWidth(-ResetButtonWidth);
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

		ImGui.SameLine();
		if (ImGui.SmallButton(e.HasOverride ? "reset" : "="))
			e.HasOverride = false;

		ImGui.PopID();
	}
}
#endif
