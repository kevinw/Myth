using System.Diagnostics;
using System.Numerics;
using Foster.Framework;

namespace TinyLink;

static class Inbetweens
{
	public static bool UseRustCli = false;
	public static bool BenchMode = false;
	public static int MaxFrames = int.MaxValue;

	public static (Image[] Frames, float[] Durations)? Generate(
		string name, Image[] frames, Aseprite ase, int count)
	{
		if (frames.Length < 2)
			return null;

		if (BenchMode)
			return GenerateBench(name, frames, ase, count);

		if (UseRustCli)
			return GenerateWithRustCli(name, frames, ase, count);
		return GenerateWithCSharp(name, frames, ase, count);
	}

	/// <summary>
	/// Generates in-between frames between two specific images (a -> b).
	/// Used for animation transitions.
	/// </summary>
	public static (Image[] Frames, float[] Durations) GenerateForPair(Image a, Image b, int count)
	{
		if (count <= 0)
			return ([], []);

		var flow = ComputeFlow(a, b);
		var frames = new Image[count];
		var durations = new float[count];
		for (int i = 0; i < count; i++)
		{
			float t = (i + 1.0f) / (count + 1.0f);
			frames[i] = Warp(a, flow, t);
			durations[i] = 0.019f;
		}
		return (frames, durations);
	}

	private static (Image[] Frames, float[] Durations)? GenerateBench(
		string name, Image[] frames, Aseprite ase, int count)
	{
		// snapshot the frames we actually need
		var bf = MaxFrames < frames.Length
			? frames[..MaxFrames]
			: frames;

		// Run both generators
		var rustResult = GenerateWithRustCli(name, bf, ase, count);
		var csResult   = GenerateWithCSharp(name, bf, ase, count);

		if (rustResult == null || csResult == null)
			return UseRustCli ? rustResult : csResult;

		var (rustFrames, rustDurs) = rustResult.Value;
		var (csFrames,   csDurs)   = csResult.Value;

		int compareCount = Math.Min(rustFrames.Length, csFrames.Length);
		var metrics = new List<BenchMetric>();

		string benchDir = Path.Combine(
			AppContext.BaseDirectory,
			"..", "..", "..", "..", "tmp", "bench");

		try { Directory.CreateDirectory(Path.Combine(benchDir, name, "cs")); } catch (Exception ex) { Console.Error.WriteLine($"[Bench] mkdir error: {ex.Message}"); }
		try { Directory.CreateDirectory(Path.Combine(benchDir, name, "rust")); } catch (Exception ex) { Console.Error.WriteLine($"[Bench] mkdir error: {ex.Message}"); }

		var sw = Stopwatch.StartNew();

		for (int i = 0; i < compareCount; i++)
		{
			// Write PNGs for visual comparison
			try { csFrames[i].WritePng(Path.Combine(benchDir, name, "cs", $"f{i:D4}.png")); } catch (Exception ex) { Console.Error.WriteLine($"[Bench] write cs png error: {ex.Message}"); }
			try { rustFrames[i].WritePng(Path.Combine(benchDir, name, "rust", $"f{i:D4}.png")); } catch (Exception ex) { Console.Error.WriteLine($"[Bench] write rust png error: {ex.Message}"); }

			// Compute per-pixel difference
			var csImg = csFrames[i];
			var rsImg = rustFrames[i];
			int w = Math.Min(csImg.Width, rsImg.Width);
			int h = Math.Min(csImg.Height, rsImg.Height);

			long sumSq = 0;
			int maxDiff = 0;
			int totalPixels = w * h;

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					var ca = csImg[x, y];
					var cb = rsImg[x, y];
					int dr = ca.R - cb.R;
					int dg = ca.G - cb.G;
					int db = ca.B - cb.B;
					int da = ca.A - cb.A;
					int d2 = dr * dr + dg * dg + db * db + da * da;
					sumSq += d2;
					if (d2 > maxDiff) maxDiff = d2;
				}

			float mse = (float)sumSq / totalPixels;
			metrics.Add(new BenchMetric
			{
				Frame = i,
				MSE = mse,
				RMSE = MathF.Sqrt(mse),
				MaxChannelDiff = MathF.Sqrt(maxDiff),
			});
		}

		sw.Stop();
		double totalSec = sw.Elapsed.TotalSeconds;

		// Log summary
		var totalMse = metrics.Average(m => m.MSE);
		var totalRmse = MathF.Sqrt(totalMse);
		var maxOverallDiff = metrics.Max(m => m.MaxChannelDiff);
		Console.Error.WriteLine($"[Bench] {name}: {metrics.Count} frames, "
			+ $"RMSE={totalRmse:F2}, max-diff={maxOverallDiff:F0}, "
			+ $"C#={csFrames.Length}f ib, Rust={rustFrames.Length}f ib, "
			+ $"took {totalSec:F2}s");

		return UseRustCli ? rustResult : csResult;
	}

	private struct BenchMetric
	{
		public int Frame;
		public float MSE;
		public float RMSE;
		public float MaxChannelDiff;
	}



	public static void ClearRustCliCache()
	{
		rustCliCache = null;
	}

	private static string? rustCliCache;

	private static string? FindRustCli()
	{
		if (rustCliCache != null)
			return rustCliCache;

		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, "bitmapflow-cli"),
			Path.Combine(AppContext.BaseDirectory, "bitmapflow-cli.exe"),
		};
		foreach (var c in candidates)
			if (File.Exists(c))
				return rustCliCache = c;

		var dir = new DirectoryInfo(Path.Combine(
			AppContext.BaseDirectory,
			"..", "..", "..", "..", "Assets"));
		while (dir != null)
		{
			var cli = Path.Combine(dir.FullName, "bitmapflow-cli", "target", "release", "bitmapflow-cli");
			if (File.Exists(cli))
				return rustCliCache = cli;
			if (File.Exists(cli + ".exe"))
				return rustCliCache = cli + ".exe";
			dir = dir.Parent;
		}
		return null;
	}

	private static (Image[] Frames, float[] Durations)? GenerateWithRustCli(
		string name, Image[] frames, Aseprite ase, int count)
	{
		if (frames.Length < 2)
			return null;

		var cli = FindRustCli();
		if (cli == null)
			return null;

		bool looping = ase.Tags.Any(t => t.Repeat == 0);
		var cliInputs = looping ? frames.Concat(frames.Take(1)).ToArray() : frames;

		var tempDir = Path.Combine(Path.GetTempPath(), "TinyLink-BitmapFlow", name);
		try { Directory.CreateDirectory(tempDir); } catch { return null; }

		try
		{
			var inputPaths = new string[cliInputs.Length];
			for (int i = 0; i < cliInputs.Length; i++)
			{
				inputPaths[i] = Path.Combine(tempDir, $"f_{i:D4}.png");
				cliInputs[i].WritePng(inputPaths[i]);
			}

			var inputArgs = string.Join(" ", inputPaths.Select(p => $"\"{p}\""));
			var args = $"--frames {inputArgs} --output-dir \"{tempDir}\" --inbetweens {count} --prefix ib";

			var psi = new ProcessStartInfo(cli, args)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
			};
			using var process = Process.Start(psi);
			if (process == null)
				return null;
			var stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				Console.Error.WriteLine($"[Assets] bitmapflow-cli failed for {name} (exit {process.ExitCode}): {stderr}");
				return null;
			}

			var ibCount = (cliInputs.Length - 1) * count;
			var ibImages = new Image[ibCount];
			for (int i = 0; i < ibCount; i++)
			{
				var ibPath = Path.Combine(tempDir, $"ib{i:D4}.png");
				if (!File.Exists(ibPath))
					return null;
				ibImages[i] = new Image(ibPath);
			}

			var resultCount = frames.Length + ibCount;
			var result = new Image[resultCount];
			var durations = new float[resultCount];

			int ibIdx = 0;
			int outIdx = 0;
			for (int i = 0; i < frames.Length; i++)
			{
				float subMs = looping || i < frames.Length - 1
					? ase.Frames[i].Duration / (float)(count + 1)
					: ase.Frames[i].Duration;
				float subSec = subMs / 1000f;

				result[outIdx] = frames[i];
				durations[outIdx] = subSec;
				outIdx++;

				if (looping || i < frames.Length - 1)
				{
					for (int j = 0; j < count; j++)
					{
						result[outIdx] = ibImages[ibIdx++];
						durations[outIdx] = subSec;
						outIdx++;
					}
				}
			}

		return (result, durations);
		}
		finally
		{
			try { Directory.Delete(tempDir, true); } catch { }
		}
	}

	// ── C# optical-flow implementation ──────────────────────────────

	private const int BlockRadius = 2;
	private const int CoarseSearchRadius = 0;
	private const int FineSearchRadius = 1;
	private const int PyramidLevels = 1;
	private const int MedianFilterRadius = 1;
	private const float StaticFrameChangeRatio = 0.50f;

	private static (Image[] Frames, float[] Durations)? GenerateWithCSharp(
		string name, Image[] frames, Aseprite ase, int count)
	{
		if (frames.Length < 2)
			return null;

		bool looping = ase.Tags.Any(t => t.Repeat == 0);
		int pairCount = looping ? frames.Length : frames.Length - 1;

		// compute flow for each consecutive pair
		var flows = new Vector2[pairCount][,];
		Parallel.For(0, pairCount, p =>
		{
			int a = p;
			int b = (p + 1) % frames.Length;
			flows[p] = ComputeFlow(frames[a], frames[b]);
		});

		// warp to produce inbetween frames
		var ibCount = pairCount * count;
		var ibImages = new Image[ibCount];
		Parallel.For(0, ibCount, i =>
		{
			int pairIdx = i / count;
			int tIdx = i % count;
			float t = (tIdx + 1.0f) / (count + 1.0f);

			int a = pairIdx;
			ibImages[i] = Warp(frames[a], flows[pairIdx], t);
		});

		// assemble [F0, ib..., F1, ib..., ..., F_last(, ib... if looping)]
		var resultCount = frames.Length + ibCount;
		var result = new Image[resultCount];
		var durations = new float[resultCount];

		int ibIdx = 0;
		int outIdx = 0;
		for (int i = 0; i < frames.Length; i++)
		{
			float subMs = looping || i < frames.Length - 1
				? ase.Frames[i].Duration / (float)(count + 1)
				: ase.Frames[i].Duration;
			float subSec = subMs / 1000f;

			result[outIdx] = frames[i];
			durations[outIdx] = subSec;
			outIdx++;

			if (looping || i < frames.Length - 1)
			{
				for (int j = 0; j < count; j++)
				{
					result[outIdx] = ibImages[ibIdx++];
					durations[outIdx] = subSec;
					outIdx++;
				}
			}
		}

		return (result, durations);
	}

	// ── dense optical flow via block matching on a pyramid ──────────

	private static void SmoothFlow(Vector2[,] flow, int passes = 2, float strength = 0.5f)
	{
		int h = flow.GetLength(0), w = flow.GetLength(1);
		for (int p = 0; p < passes; p++)
		{
			var copy = new Vector2[h, w];
			Array.Copy(flow, copy, flow.Length);

			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					float sx = 0, sy = 0;
					int n = 0;
					for (int dy = -1; dy <= 1; dy++)
						for (int dx = -1; dx <= 1; dx++)
						{
							if (dx == 0 && dy == 0) continue;
							int nx = x + dx, ny = y + dy;
							if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
							sx += copy[ny, nx].X;
							sy += copy[ny, nx].Y;
							n++;
						}

					if (n > 0)
						flow[y, x] = new Vector2(
							copy[y, x].X * (1 - strength) + (sx / n) * strength,
							copy[y, x].Y * (1 - strength) + (sy / n) * strength);
				}
		}
	}

	private static Vector2[,] ComputeFlow(Image src, Image dst)
	{
		if (IsSparsePoseChange(src, dst))
			return new Vector2[src.Height, src.Width];

		var pyrSrc = BuildPyramid(src);
		var pyrDst = BuildPyramid(dst);

		int cw = pyrSrc[^1].GetLength(1);
		int ch = pyrSrc[^1].GetLength(0);
		var flow = new Vector2[ch, cw];

		for (int level = PyramidLevels - 1; level >= 0; level--)
		{
			var colsA = pyrSrc[level];
			var colsB = pyrDst[level];

			if (level < PyramidLevels - 1)
				flow = UpscaleFlow(flow, colsA.GetLength(1), colsA.GetLength(0));

				int radius = level == 0 ? FineSearchRadius : CoarseSearchRadius;
			flow = BlockMatch(colsA, colsB, flow, radius);
			flow = MedianFilterFlow(flow, MedianFilterRadius);
		}

		// zero flow for transparent source pixels so the warp doesn't
		// pull sprite content into blank space
		var src0 = pyrSrc[0];
		for (int y = 0; y < flow.GetLength(0); y++)
			for (int x = 0; x < flow.GetLength(1); x++)
				if (src0[y, x].A < 30)
					flow[y, x] = default;

		return flow;
	}

	private static bool IsSparsePoseChange(Image src, Image dst)
	{
		int w = Math.Min(src.Width, dst.Width);
		int h = Math.Min(src.Height, dst.Height);
		int opaque = 0;
		int changed = 0;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var a = src[x, y];
				var b = dst[x, y];
				bool aOpaque = a.A >= 30;
				bool bOpaque = b.A >= 30;
				if (!aOpaque && !bOpaque)
					continue;

				opaque++;
				if (a.R != b.R || a.G != b.G || a.B != b.B || a.A != b.A)
					changed++;
			}

		return opaque > 0 && changed / (float)opaque <= StaticFrameChangeRatio;
	}

	private static Vector2[,] MedianFilterFlow(Vector2[,] flow, int radius)
	{
		if (radius <= 0)
			return flow;

		int h = flow.GetLength(0), w = flow.GetLength(1);
		var filtered = new Vector2[h, w];
		int maxSamples = (radius * 2 + 1) * (radius * 2 + 1);

		Parallel.For(0, h, y =>
		{
			var xs = new float[maxSamples];
			var ys = new float[maxSamples];

			for (int x = 0; x < w; x++)
			{
				int count = 0;
				for (int dy = -radius; dy <= radius; dy++)
				{
					int ny = y + dy;
					if (ny < 0 || ny >= h)
						continue;

					for (int dx = -radius; dx <= radius; dx++)
					{
						int nx = x + dx;
						if (nx < 0 || nx >= w)
							continue;

						var sample = flow[ny, nx];
						xs[count] = sample.X;
						ys[count] = sample.Y;
						count++;
					}
				}

				Array.Sort(xs, 0, count);
				Array.Sort(ys, 0, count);
				filtered[y, x] = new Vector2(xs[count / 2], ys[count / 2]);
			}
		});

		return filtered;
	}

	private static List<Color[,]> BuildPyramid(Image img)
	{
		var pyramid = new List<Color[,]>();
		Color[,] current = ToColors(img);
		pyramid.Add(current);

		for (int level = 1; level < PyramidLevels; level++)
		{
			int pw = current.GetLength(1);
			int ph = current.GetLength(0);
			int nw = Math.Max(1, pw / 2);
			int nh = Math.Max(1, ph / 2);
			var next = new Color[nh, nw];

			for (int y = 0; y < nh; y++)
			{
				int sy = y * 2;
				for (int x = 0; x < nw; x++)
				{
					int sx = x * 2;
					int sumR = 0, sumG = 0, sumB = 0, sumA = 0, count = 0;
					for (int dy = 0; dy < 2 && sy + dy < ph; dy++)
						for (int dx = 0; dx < 2 && sx + dx < pw; dx++)
						{
							var c = current[sy + dy, sx + dx];
							sumR += c.R; sumG += c.G; sumB += c.B; sumA += c.A;
							count++;
						}
					next[y, x] = new Color(
						(byte)(sumR / count),
						(byte)(sumG / count),
						(byte)(sumB / count),
						(byte)(sumA / count));
				}
			}

			pyramid.Add(next);
			current = next;
		}

		return pyramid;
	}

	private static Color[,] ToColors(Image img)
	{
		int w = img.Width, h = img.Height;
		var cols = new Color[h, w];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img[x, y];
				cols[y, x] = c.A < 30 ? new Color(0, 0, 0, 0) : c;
			}
		return cols;
	}

	private static Vector2[,] BlockMatch(Color[,] src, Color[,] dst, Vector2[,] init, int radius)
	{
		int h = src.GetLength(0), w = src.GetLength(1);
		var flow = new Vector2[h, w];

		Parallel.For(0, h, y =>
		{
			for (int x = 0; x < w; x++)
			{
				float bestCost = float.MaxValue;
				float bestDx = init[y, x].X;
				float bestDy = init[y, x].Y;

				int cx = (int)Math.Round(init[y, x].X);
				int cy = (int)Math.Round(init[y, x].Y);

				for (int dy = cy - radius; dy <= cy + radius; dy++)
					for (int dx = cx - radius; dx <= cx + radius; dx++)
					{
						float cost = SAD_RGB(src, dst, x, y, x + dx, y + dy);

						if (cost < bestCost)
						{
							bestCost = cost;
							bestDx = dx;
							bestDy = dy;
						}
					}

				flow[y, x] = new Vector2(bestDx, bestDy);
			}
		});

		return flow;
	}

	private static float SAD_RGB(Color[,] src, Color[,] dst, int sx, int sy, int dx, int dy)
	{
		int h = src.GetLength(0), w = src.GetLength(1);
		float sum = 0;

		for (int by = -BlockRadius; by <= BlockRadius; by++)
			for (int bx = -BlockRadius; bx <= BlockRadius; bx++)
			{
				int sy2 = sy + by, sx2 = sx + bx;
				int dy2 = dy + by, dx2 = dx + bx;

				Color ca, cb;
				bool aOk = sy2 >= 0 && sy2 < h && sx2 >= 0 && sx2 < w;
				bool bOk = dy2 >= 0 && dy2 < h && dx2 >= 0 && dx2 < w;

				if (!aOk || !bOk)
				{
					sum += 255;
					continue;
				}

				ca = src[sy2, sx2];
				cb = dst[dy2, dx2];

				// both transparent — no cost
				if (ca.A < 30 && cb.A < 30)
					continue;

				// use max channel difference across R, G, B
				byte ra = ca.A < 30 ? (byte)0 : ca.R;
				byte ga = ca.A < 30 ? (byte)0 : ca.G;
				byte ba = ca.A < 30 ? (byte)0 : ca.B;
				byte rb = cb.A < 30 ? (byte)0 : cb.R;
				byte gb = cb.A < 30 ? (byte)0 : cb.G;
				byte bb = cb.A < 30 ? (byte)0 : cb.B;

				int dr = Math.Abs(ra - rb);
				int dg = Math.Abs(ga - gb);
				int db = Math.Abs(ba - bb);

				sum += Math.Max(dr, Math.Max(dg, db));
			}

		return sum;
	}

	private static Vector2[,] UpscaleFlow(Vector2[,] low, int newW, int newH)
	{
		int lh = low.GetLength(0), lw = low.GetLength(1);
		float sx = (float)newW / lw;
		float sy = (float)newH / lh;
		var result = new Vector2[newH, newW];

		for (int y = 0; y < newH; y++)
		{
			float ly = y / sy;
			int iy = Math.Min((int)ly, lh - 1);
			for (int x = 0; x < newW; x++)
			{
				float lx = x / sx;
				int ix = Math.Min((int)lx, lw - 1);
				var f = low[iy, ix];
				result[y, x] = new Vector2(f.X * sx, f.Y * sy);
			}
		}

		return result;
	}

	// ── warp source image along flow field ──────────────────────────

	private static Image Warp(Image source, Vector2[,] flow, float t)
	{
		int w = source.Width, h = source.Height;
		var result = new Image(w, h);

		Parallel.For(0, h, y =>
		{
			for (int x = 0; x < w; x++)
			{
				float fx = flow[y, x].X * t;
				float fy = flow[y, x].Y * t;

				result[x, y] = SampleNearest(source, x - fx, y - fy);
			}
		});

		return result;
	}

	private static Color SampleNearest(Image img, float x, float y)
	{
		int w = img.Width, h = img.Height;
		int ix = (int)Math.Round(Math.Clamp(x, 0, w - 1));
		int iy = (int)Math.Round(Math.Clamp(y, 0, h - 1));
		return img[ix, iy];
	}
}
