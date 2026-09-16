using Foster.Framework;

namespace Myth;

public partial class Manager
{
	enum StartupAttributionPhase
	{
		None,
		Logos,
		TitleFade,
	}

	static float AttributionFadeInDuration => 0.5f;
	static float AttributionHoldDuration => 2.5f;
	static float AttributionFadeOutDuration => 0.5f;
	static float AttributionTitleFadeDuration => 0.6f;
	static float AttributionLogoGap => 16f;

	StartupAttributionPhase startupAttributionPhase;
	float startupAttributionTime;
	float startupAttributionFadeOutFrom = 1;
	Texture? fosterAttributionTexture;
	Texture? fmodAttributionTexture;

	bool TryBeginStartupAttribution()
	{
		const string fosterPath = "Attribution/foster.png";
		const string fmodPath = "Attribution/fmod.png";
		try
		{
			if (!Assets.Content.FileExists(fosterPath) || !Assets.Content.FileExists(fmodPath))
			{
				Console.Error.WriteLine($"Attribution skipped: missing '{fosterPath}' or '{fmodPath}'.");
				return false;
			}

			fosterAttributionTexture = Assets.LoadTexture(GraphicsDevice, Assets.Content, fosterPath);
			fmodAttributionTexture = Assets.LoadTexture(GraphicsDevice, Assets.Content, fmodPath);
			startupAttributionTime = 0;
			startupAttributionFadeOutFrom = 1;
			startupAttributionPhase = StartupAttributionPhase.Logos;
			return true;
		}
		catch (Exception e)
		{
			DisposeStartupAttribution();
			Console.Error.WriteLine($"Attribution skipped: {e.Message}");
			return false;
		}
	}

	bool UpdateStartupAttribution()
	{
		if (startupAttributionPhase == StartupAttributionPhase.None)
			return false;

		startupAttributionTime += Time.Delta;
		if (startupAttributionPhase == StartupAttributionPhase.Logos)
		{
			var fadeOutStart = AttributionFadeInDuration + AttributionHoldDuration;
			if (startupAttributionTime < fadeOutStart && StartupAttributionSkipPressed())
			{
				startupAttributionFadeOutFrom = StartupAttributionAlpha();
				startupAttributionTime = fadeOutStart;
			}

			var duration = fadeOutStart + AttributionFadeOutDuration;
			if (startupAttributionTime < duration)
				return true;

			DisposeStartupAttribution();
			startupAttributionTime = 0;
			startupAttributionPhase = StartupAttributionPhase.TitleFade;
			ShowTitleMenu();
			return false;
		}

		if (startupAttributionTime >= AttributionTitleFadeDuration)
			startupAttributionPhase = StartupAttributionPhase.None;
		return false;
	}

	bool StartupAttributionSkipPressed()
	{
		if (Input.Keyboard.FirstPressed() != null)
			return true;
		foreach (var controller in Input.Controllers)
			if (controller.Connected)
				for (var button = 0; button < ControllerState.MaxButtons; button++)
					if (controller.Pressed(button))
						return true;
		return false;
	}

	float StartupAttributionAlpha()
	{
		var fadeOutStart = AttributionFadeInDuration + AttributionHoldDuration;
		if (startupAttributionTime < AttributionFadeInDuration)
			return Ease.Cube.InOut(startupAttributionTime / AttributionFadeInDuration);
		if (startupAttributionTime < fadeOutStart)
			return 1;
		return startupAttributionFadeOutFrom *
			(1 - Ease.Cube.InOut((startupAttributionTime - fadeOutStart) / AttributionFadeOutDuration));
	}

	void DrawStartupAttribution()
	{
		if (fosterAttributionTexture == null || fmodAttributionTexture == null)
			return;

		var color = Color.White * StartupAttributionAlpha();
		var height = fosterAttributionTexture.Height + AttributionLogoGap + fmodAttributionTexture.Height;
		var top = MenuBounds.Center.Y - height * 0.5f;

		batcher.PushMatrix(MenuMatrix);
		batcher.ImageJustified(new(fosterAttributionTexture),
			new(MenuBounds.Center.X, top + fosterAttributionTexture.Height * 0.5f), new(0.5f), color);
		batcher.ImageJustified(new(fmodAttributionTexture),
			new(MenuBounds.Center.X, top + fosterAttributionTexture.Height + AttributionLogoGap + fmodAttributionTexture.Height * 0.5f), new(0.5f), color);
		batcher.PopMatrix();
	}

	bool DrawStartupTitleFade()
	{
		if (startupAttributionPhase != StartupAttributionPhase.TitleFade)
			return false;

		var alpha = 1 - Ease.Cube.Out(Math.Clamp(startupAttributionTime / AttributionTitleFadeDuration, 0, 1));
		batcher.Rect(MenuViewport, Color.Black * alpha);
		return true;
	}

	void DisposeStartupAttribution()
	{
		fosterAttributionTexture?.Dispose();
		fosterAttributionTexture = null;
		fmodAttributionTexture?.Dispose();
		fmodAttributionTexture = null;
	}
}
