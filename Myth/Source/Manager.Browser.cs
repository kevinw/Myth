#if BROWSER
using Foster.Framework;

namespace Myth;

public partial class Manager
{
	// On iOS, backgrounding the browser tab triggers an audio-session interruption
	// that permanently kills FMOD's WebAudio output (the worklet can't be revived and
	// the output device can't be re-inited in place). The JS bridge flags it; here we
	// rebuild the output (a fresh AudioContext) and re-establish background music the
	// same way a bank hot-reload does. Runs every frame; cheap until the flag is set.
	private void UpdateAudioInterruptionRecovery()
	{
		if (!Audio.ConsumeAudioInterruption())
			return;

		try
		{
			var musicState = game?.CaptureMusicReloadState();
			Audio.Restart();
			if (musicState is { } state)
				game?.RestoreMusicReloadState(state);
			Console.WriteLine("Rebuilt FMOD audio output after interruption.");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to rebuild FMOD audio after interruption: {e}");
		}
	}

	private void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			selectedRoomId = byId.Id;
			cell = byId.Cell;
			return;
		}

		if (Assets.GetRoom(cell) is { } byCell)
		{
			selectedRoomId = byCell.Id;
			cell = byCell.Cell;
			return;
		}

		var first = Assets.GetRoom(Point2.Zero) ?? Assets.RoomsById.Values.FirstOrDefault();
		selectedRoomId = first?.Id;
		cell = first?.Cell ?? Point2.Zero;
	}
}
#endif
