using System.Numerics;
using System.Globalization;
using System.Diagnostics;
using Foster.Framework;
using System.Text.Json;
namespace Myth;

#if !MYTHSHIP && !BROWSER
public partial class Manager : App
{

	readonly List<MoviePlayerSample> moviePlayerSamples = [];
	bool movieTracksPlayer;
	long movieTrackingStartTimestamp;
	double movieNextSampleTime;
	Vector2 movieCropCenter;
	bool movieCropCenterInitialized;
	int movieTrackingFps;
	Point2 movieOutputSize;

	// Records the game window to video via ScreenCaptureKit (see native/MythCapture).
	void ToggleMovieRecording(bool trackPlayer)
	{
		if (!MythCapture.Available)
		{
			DisplayMessage("Window capture: macOS only");
			return;
		}

		try
		{
			if (MythCapture.IsRecording)
				StopMovieRecording(reveal: true);
			else
			{
				var dir = MovieOutputDir();
				Directory.CreateDirectory(dir);
				var room = SanitizeForFilename(game?.CurrentRoom?.Id);
				var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
				var ext = "mov";
				var name = string.IsNullOrEmpty(room) ? $"myth-{stamp}.{ext}" : $"myth-{room}-{stamp}.{ext}";
				var path = Path.Combine(dir, name);
				var ok = MythCapture.Start(path);
				if (!ok)
				{
					DisplayMessage($"Rec failed: {MythCapture.LastError()}");
					return;
				}

				movieTracksPlayer = trackPlayer;
				moviePlayerSamples.Clear();
				if (trackPlayer)
				{
					movieTrackingStartTimestamp = Stopwatch.GetTimestamp();
					movieNextSampleTime = 0;
					movieCropCenterInitialized = false;
					movieTrackingFps = Math.Max(1, MythCapture.EffectiveFps);
					movieOutputSize = MythCapture.OutputSize;
					moviePlayerSamples.EnsureCapacity(movieTrackingFps * 60);
				}
				DisplayMessage(trackPlayer ? "● REC + player crop" : "● REC");
			}
		}
		catch (DllNotFoundException)
		{
			DisplayMessage("libMythCapture.dylib missing — run native/MythCapture/build.sh");
		}
		catch (EntryPointNotFoundException)
		{
			DisplayMessage("libMythCapture.dylib is stale — rebuild Myth");
		}
	}

	void StopMovieRecording(bool reveal)
	{
		var trackedPlayer = movieTracksPlayer;
		movieTracksPlayer = false;
		var ok = MythCapture.Stop();
		if (!ok || MythCapture.OutputPath is not { } saved)
		{
			DisplayMessage($"Rec stop failed: {MythCapture.LastError()}");
			return;
		}

		if (trackedPlayer)
		{
			try
			{
				var cropped = PlayerCropPath(saved);
				WriteMoviePlayerSidecar(saved, cropped);
				DisplayMessage(LaunchPlayerCrop(saved, cropped)
					? $"Saved → {saved}; cropping player video…"
					: $"Saved → {saved} + sidecar; ffmpeg not found");
			}
			catch (Exception e)
			{
				DisplayMessage($"Saved → {saved}; player crop failed: {e.Message}");
			}
		}
		else
			DisplayMessage($"Saved → {saved}");

		if (reveal)
			RevealInFinder(saved);
	}

	void RecordMoviePlayerSample()
	{
		if (game is not { } liveGame ||
			liveGame.GetFirst<Player>() is not { } player ||
			Window is not { } window)
			return;

		var elapsed = Stopwatch.GetElapsedTime(movieTrackingStartTimestamp).TotalSeconds;
		if (elapsed < movieNextSampleTime)
			return;
		movieNextSampleTime = (Math.Floor(elapsed * movieTrackingFps) + 1) / movieTrackingFps;

		var viewport = (RectInt)(GameViewRect * Scale);
		if (viewport.Width <= 0 || viewport.Height <= 0 || window.WidthInPixels <= 0 || window.HeightInPixels <= 0)
			return;

		var present = liveGame.PresentRect(viewport);
		var presentScale = liveGame.PresentScale(present, Game.Width, Game.Height);
		var useRemainder = true;
		var continuous = (Vector2)player.Position + (useRemainder ? player.SubPixelRemainder : Vector2.Zero);
		var viewPosition = (continuous - liveGame.RenderCamera) * liveGame.ZoomLevel;
		var windowPosition =
			(Vector2)present.Center +
			(viewPosition - new Vector2(Game.Width, Game.Height) * 0.5f) * presentScale;
		var normalized = windowPosition / new Vector2(window.WidthInPixels, window.HeightInPixels);

		var dt = moviePlayerSamples.Count > 0 ? elapsed - moviePlayerSamples[^1].Time : 0;
		if (!movieCropCenterInitialized)
		{
			movieCropCenter = normalized;
			movieCropCenterInitialized = true;
		}
		else
			movieCropCenter += (normalized - movieCropCenter) * (1 - MathF.Exp(-(float)dt * MoviePlayerCropEase));

		var cropHalf = 0.5f / MoviePlayerCropZoom;
		movieCropCenter = Vector2.Clamp(movieCropCenter, Vector2.One * cropHalf, Vector2.One * (1 - cropHalf));
		moviePlayerSamples.Add(new(
			elapsed,
			normalized,
			viewPosition,
			movieCropCenter,
			(int)player.Facing,
			player.Velocity));
	}

	void WriteMoviePlayerSidecar(string videoPath, string croppedPath)
	{
		var sidecarPath = Path.ChangeExtension(videoPath, ".json");
		using var stream = File.Create(sidecarPath);
		using var json = new Utf8JsonWriter(stream, new() { Indented = true });
		json.WriteStartObject();
		json.WriteNumber("version", 1);
		json.WriteString("video", Path.GetFileName(videoPath));
		json.WriteNumber("fps", movieTrackingFps);
		json.WriteStartObject("videoSize");
		json.WriteNumber("w", movieOutputSize.X);
		json.WriteNumber("h", movieOutputSize.Y);
		json.WriteEndObject();
		json.WriteStartObject("view");
		json.WriteNumber("w", Game.Width);
		json.WriteNumber("h", Game.Height);
		json.WriteEndObject();
		json.WriteString("coordinates", "normalized captured content, top-left origin");
		json.WriteStartObject("crop");
		json.WriteString("video", Path.GetFileName(croppedPath));
		json.WriteNumber("zoom", MoviePlayerCropZoom);
		json.WriteNumber("ease", MoviePlayerCropEase);
		json.WriteEndObject();
		json.WriteStartArray("samples");
		foreach (var sample in moviePlayerSamples)
		{
			json.WriteStartObject();
			json.WriteNumber("t", sample.Time);
			json.WriteNumber("x", sample.ScreenPosition.X);
			json.WriteNumber("y", sample.ScreenPosition.Y);
			json.WriteNumber("viewX", sample.ViewPosition.X);
			json.WriteNumber("viewY", sample.ViewPosition.Y);
			json.WriteNumber("cropX", sample.CropPosition.X);
			json.WriteNumber("cropY", sample.CropPosition.Y);
			json.WriteNumber("facing", sample.Facing);
			json.WriteNumber("vx", sample.Velocity.X);
			json.WriteNumber("vy", sample.Velocity.Y);
			json.WriteEndObject();
		}
		json.WriteEndArray();
		json.WriteEndObject();
	}

	bool LaunchPlayerCrop(string videoPath, string croppedPath)
	{
		if (moviePlayerSamples.Count <= 0 || movieOutputSize.X < 2 || movieOutputSize.Y < 2)
			throw new InvalidOperationException("no player samples");

		var ffmpeg = FfmpegPath();
		if (ffmpeg == null)
			return false;

		var sourceEncoding = ProbeVideoEncoding(ffmpeg, videoPath);

		var cropWidth = Even((int)(movieOutputSize.X / MoviePlayerCropZoom));
		var cropHeight = Even((int)(movieOutputSize.Y / MoviePlayerCropZoom));
		var commandsPath = Path.Combine(Path.GetTempPath(), $"myth-crop-{Guid.NewGuid():N}.txt");
		using (var commands = new StreamWriter(commandsPath))
		{
			WriteCropCommand(commands, 0, moviePlayerSamples[0].CropPosition, cropWidth, cropHeight);
			foreach (var sample in moviePlayerSamples)
				WriteCropCommand(commands, sample.Time, sample.CropPosition, cropWidth, cropHeight);
		}

		var filterPath = commandsPath.Replace("\\", "\\\\").Replace("'", "\\'");
		var filter =
			$"sendcmd=filename='{filterPath}'," +
			$"crop=w={cropWidth}:h={cropHeight}:x=0:y=0:exact=1," +
			$"scale={movieOutputSize.X}:{movieOutputSize.Y}:flags=neighbor";
		var info = new ProcessStartInfo(ffmpeg)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};

		info.ArgumentList.Add("-y");
		info.ArgumentList.Add("-i");
		info.ArgumentList.Add(videoPath);
		info.ArgumentList.Add("-vf");
		info.ArgumentList.Add(filter);
		info.ArgumentList.Add("-map");
		info.ArgumentList.Add("0:v:0");
		info.ArgumentList.Add("-map");
		info.ArgumentList.Add("0:a?");
		info.ArgumentList.Add("-map");
		info.ArgumentList.Add("0:a?");
		AddMatchingVideoEncoder(info, sourceEncoding);
		info.ArgumentList.Add("-c:a");
		info.ArgumentList.Add("copy");
		info.ArgumentList.Add("-movflags");
		info.ArgumentList.Add("+faststart");
		info.ArgumentList.Add(croppedPath);

		var process = new Process { StartInfo = info, EnableRaisingEvents = true };
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data != null)
				Console.WriteLine($"ffmpeg: {e.Data}");
		};
		process.Exited += (_, _) =>
		{
			try
			{
				if (process.ExitCode == 0)
				{
					Console.WriteLine($"Player crop saved → {croppedPath}");
					RevealInFinder(croppedPath);
				}
				else
					Console.WriteLine($"Player crop failed (ffmpeg exit {process.ExitCode})");
			}
			finally
			{
				process.Dispose();
				try { File.Delete(commandsPath); } catch { }
			}
		};
		try
		{
			if (process.Start())
			{
				process.BeginErrorReadLine();
				return true;
			}
		}
		catch
		{
			process.Dispose();
			try { File.Delete(commandsPath); } catch { }
			throw;
		}

		process.Dispose();
		try { File.Delete(commandsPath); } catch { }
		return false;
	}

	void WriteCropCommand(
		StreamWriter writer,
		double time,
		Vector2 center,
		int cropWidth,
		int cropHeight)
	{
		var maxX = movieOutputSize.X - cropWidth;
		var maxY = movieOutputSize.Y - cropHeight;

		var rawX = center.X * movieOutputSize.X - cropWidth * 0.5f;
		var rawY = center.Y * movieOutputSize.Y - cropHeight * 0.5f;

		var x = Math.Clamp((int)MathF.Round(rawX), 0, maxX);
		var y = Math.Clamp((int)MathF.Round(rawY), 0, maxY);

		// Safe for both 4:2:2 ProRes and potential 4:2:0 H.264/HEVC inputs.
		x &= ~1;
		y &= ~1;

		writer.Write(time.ToString("0.######", CultureInfo.InvariantCulture));
		writer.Write(" crop x ");
		writer.Write(x.ToString(CultureInfo.InvariantCulture));
		writer.Write(", crop y ");
		writer.Write(y.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(";");
	}

	static float MoviePlayerCropZoom => 2f;
	static float MoviePlayerCropEase => 8f;
	static int Even(int value) => Math.Max(2, value - value % 2);
	static string PlayerCropPath(string videoPath)
		=> Path.Combine(
			Path.GetDirectoryName(videoPath) ?? "",
			$"{Path.GetFileNameWithoutExtension(videoPath)}-player{Path.GetExtension(videoPath)}");
	static string? FfmpegPath()
		=> File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg"
			: File.Exists("/usr/local/bin/ffmpeg") ? "/usr/local/bin/ffmpeg"
			: null;

	static string MovieOutputDir()
	{
		var movies = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
		if (string.IsNullOrEmpty(movies))
			movies = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		return Path.Combine(movies, "Myth");
	}

	// Room ids can contain path separators / spaces; make one filename-safe.
	static string SanitizeForFilename(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return "";
		var invalid = Path.GetInvalidFileNameChars();
		var chars = name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
		return new string(chars);
	}

	// Reveal (and select) the finished recording in a Finder window.
	static void RevealInFinder(string path)
	{
		try
		{
			var info = new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false };
			info.ArgumentList.Add("-R");
			info.ArgumentList.Add(path);
			System.Diagnostics.Process.Start(info);
		}
		catch { /* best-effort: recording already saved */ }
	}

	readonly record struct MoviePlayerSample(
		double Time,
		Vector2 ScreenPosition,
		Vector2 ViewPosition,
		Vector2 CropPosition,
		int Facing,
		Vector2 Velocity);

	private sealed record VideoEncoding(string CodecName, string? Profile, string? PixelFormat);

	private static VideoEncoding ProbeVideoEncoding(string ffmpegPath, string videoPath) {
		var directory = Path.GetDirectoryName(ffmpegPath)
			?? throw new InvalidOperationException("FFmpeg path has no directory");

		var ffprobePath = Path.Combine(
			directory,
			OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

		if (!File.Exists(ffprobePath))
			throw new FileNotFoundException(
				"ffprobe must be installed alongside ffmpeg",
				ffprobePath);

		var info = new ProcessStartInfo(ffprobePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		info.ArgumentList.Add("-v");
		info.ArgumentList.Add("error");

		info.ArgumentList.Add("-select_streams");
		info.ArgumentList.Add("v:0");

		info.ArgumentList.Add("-show_entries");
		info.ArgumentList.Add("stream=codec_name,profile,pix_fmt");

		info.ArgumentList.Add("-of");
		info.ArgumentList.Add("json");

		info.ArgumentList.Add(videoPath);

		using var process = Process.Start(info)
			?? throw new InvalidOperationException("Could not start ffprobe");

		var json = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();

		process.WaitForExit();

		if (process.ExitCode != 0)
			throw new InvalidOperationException(
				$"ffprobe failed with exit code {process.ExitCode}: {stderr}");

		using var document = JsonDocument.Parse(json);

		if (!document.RootElement.TryGetProperty("streams", out var streams) ||
		    streams.GetArrayLength() == 0)
		{
			throw new InvalidOperationException(
				"No video stream was found in the input file");
		}

		var stream = streams[0];

		var codecName = stream.GetProperty("codec_name").GetString()
			?? throw new InvalidOperationException(
				"ffprobe did not report a video codec");

		var profile = stream.TryGetProperty("profile", out var profileElement)
			? profileElement.GetString()
			: null;

		var pixelFormat = stream.TryGetProperty("pix_fmt", out var pixelFormatElement)
			? pixelFormatElement.GetString()
			: null;

		return new VideoEncoding(codecName, profile, pixelFormat);
	}

	private static string GetProResProfile(string? sourceProfile)
	{
		var profile = sourceProfile?.Trim().ToLowerInvariant() ?? "";

		if (profile.Contains("xq"))
			return "4444xq";

		if (profile.Contains("4444"))
			return "4444";

		if (profile.Contains("hq"))
			return "hq";

		if (profile.Contains("lt"))
			return "lt";

		if (profile.Contains("proxy"))
			return "proxy";

		if (profile.Contains("standard"))
			return "standard";

		throw new NotSupportedException(
			$"Unknown ProRes profile: {sourceProfile ?? "(missing)"}");
	}

	private static void AddMatchingVideoEncoder(
		ProcessStartInfo info,
		VideoEncoding source)
	{
		void Add(string option, string value)
		{
			info.ArgumentList.Add(option);
			info.ArgumentList.Add(value);
		}

		switch (source.CodecName.ToLowerInvariant())
		{
			case "prores":
			{
				var profile = GetProResProfile(source.Profile);

				Add("-c:v", "prores_videotoolbox");
				Add(
					"-profile:v",
					profile == "4444xq" ? "xq" : profile);

				// 10-bit, bi-planar 4:2:2 input expected by VideoToolbox.
				Add("-pix_fmt", "p210le");

				break;
			}

			case "h264":
				Add("-c:v", "libx264");
				Add("-preset", "medium");
				Add("-crf", "12");
				Add("-pix_fmt", "yuv420p");
				break;

			case "hevc":
			case "h265":
				Add("-c:v", "libx265");
				Add("-preset", "medium");
				Add("-crf", "12");

				// Preserve 10-bit HEVC when the source was 10-bit.
				Add(
					"-pix_fmt",
					source.PixelFormat?.Contains("10", StringComparison.Ordinal) == true
						? "yuv420p10le"
						: "yuv420p");

				break;

			default:
				throw new NotSupportedException(
					$"Cannot preserve unsupported source codec " +
					$"'{source.CodecName}'");
		}
	}

}
#endif
