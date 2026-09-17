using System.Numerics;
using Foster.Framework;

namespace Myth;

// Player-facing video options. The game always renders at 320x180; these only shape the final
// composite: how much of the window the frame fills, whether it snaps to whole multiples, and the
// resolution the CRT runs at before a linear stretch to the screen. Compositing at a whole multiple
// keeps every game pixel the same width at fractional scales, and keeps a 6K display from running
// the CRT over 20M pixels.
public sealed class VideoSettings
{
	// Whole multiples of 320x180, which happen to be the familiar names; 0 is auto.
	static ReadOnlySpan<int> ResolutionScales => [0, 4, 6, 8, 12];
	static ReadOnlySpan<int> ScreenSizes => [100, 90, 80, 70];
	// Auto's cap: past this the CRT's scanlines and RGB split stop getting any better.
	static int AutoMaxScale => 8;

	public int ResolutionScale;
	public int ScreenSizePercent = 100;
	public bool PixelPerfect;

	// Whole literals, so the menu doesn't allocate a label every frame.
	public string ResolutionMenuLabel => ResolutionScale switch
	{
		0 => "resolution: auto",
		4 => "resolution: 720p",
		6 => "resolution: 1080p",
		8 => "resolution: 1440p",
		12 => "resolution: 4k",
		_ => $"resolution: {Game.Height * ResolutionScale}p",
	};

	public string ScreenSizeMenuLabel => ScreenSizePercent switch
	{
		100 => "screen size: 100%",
		90 => "screen size: 90%",
		80 => "screen size: 80%",
		70 => "screen size: 70%",
		_ => $"screen size: {ScreenSizePercent}%",
	};

	// Scale that fits a frame into the viewport, before gameplay zoom and the browser side-crop.
	public float FitScale(Vector2 viewport, Vector2 frame)
	{
		var fit = MathF.Min(viewport.X / frame.X, viewport.Y / frame.Y) * ScreenSizePercent / 100f;
		return PixelPerfect && fit >= 1 ? MathF.Floor(fit) : fit;
	}

	// Whole multiple the CRT composites at; the composite is then stretched by fit / this.
	public int CompositeScale(float fit)
		=> Math.Clamp((int)fit, 1, ResolutionScale == 0 ? AutoMaxScale : ResolutionScale);

	// Skips resolutions bigger than the display (unknown displays report zero and allow all).
	public void CycleResolution(Point2 displayPixels)
	{
		var index = ResolutionScales.IndexOf(ResolutionScale);
		do index = (index + 1) % ResolutionScales.Length;
		while (ResolutionScales[index] != 0 && displayPixels.X > 0 &&
			(Game.Width * ResolutionScales[index] > displayPixels.X || Game.Height * ResolutionScales[index] > displayPixels.Y));
		ResolutionScale = ResolutionScales[index];
	}

	public void CycleScreenSize()
		=> ScreenSizePercent = ScreenSizes[(ScreenSizes.IndexOf(ScreenSizePercent) + 1) % ScreenSizes.Length];

	readonly record struct PresentInputs(Point2 WindowPoints, Point2 WindowPixels, float ContentScale, bool Fullscreen,
		Point2 DisplayPoints, Point2 DisplayPixels, int ResolutionScale, int ScreenSizePercent, bool PixelPerfect, RectInt Viewport);

	PresentInputs lastLogged;

	// One verbose line whenever anything feeding the composite changes, to check the DPI and fit
	// assumptions on each machine.
	public void LogIfChanged(Window window, RectInt viewport)
	{
		var inputs = new PresentInputs(window.Size, window.SizeInPixels, window.ContentScale.X, window.Fullscreen,
			window.DisplaySize, window.DisplaySizeInPixels, ResolutionScale, ScreenSizePercent, PixelPerfect, viewport);
		if (inputs == lastLogged)
			return;
		lastLogged = inputs;

		var frame = new Vector2(Game.Width, Game.Height);
		var fit = FitScale(viewport.Size, frame);
		var composite = CompositeScale(fit);
		var shown = frame * fit;
		var letterbox = ((Vector2)viewport.Size - shown) / 2;
		Console.WriteLine($"[Video] window {inputs.WindowPoints.X}x{inputs.WindowPoints.Y}pt = {inputs.WindowPixels.X}x{inputs.WindowPixels.Y}px"
			+ $" (content scale {inputs.ContentScale:0.##}, {(inputs.Fullscreen ? "fullscreen" : "windowed")})"
			+ $" | display {inputs.DisplayPoints.X}x{inputs.DisplayPoints.Y}pt = {inputs.DisplayPixels.X}x{inputs.DisplayPixels.Y}px"
			+ $" | {ResolutionMenuLabel}, {ScreenSizeMenuLabel}, pixel perfect {(PixelPerfect ? "on" : "off")}"
			+ $" | viewport {viewport.Width}x{viewport.Height} fit {fit:0.###}x = {shown.X:0}x{shown.Y:0}, letterbox {letterbox.X:0}x{letterbox.Y:0}"
			+ $" | composite {composite}x = {Game.Width * composite}x{Game.Height * composite}, stretch {fit / composite:0.###}x");
	}
}
