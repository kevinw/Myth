using Foster.Framework;

namespace TinyLink;

public partial class Game
{
	static float PlayerHitSnapshotHoldTime => 0.12f;
	const string PlayerHitSnapshotPath = "snapshot:/player_hit";

	private SoundInstance? playerHitSnapshot;
	private float playerHitSnapshotTimer;

	public void TriggerPlayerHitSnapshot()
	{
		if (!AudioEventsEnabled || !Audio.Initialized)
			return;

		playerHitSnapshotTimer = PlayerHitSnapshotHoldTime;
		if (playerHitSnapshot != null)
			return;

		try
		{
			playerHitSnapshot = Audio.CreateEvent(PlayerHitSnapshotPath);
			playerHitSnapshot.Start();
		}
		catch (Exception e)
		{
			playerHitSnapshot = null;
			playerHitSnapshotTimer = 0;
			if (audioFailures.Add(PlayerHitSnapshotPath))
				Console.Error.WriteLine($"Failed to start FMOD snapshot '{PlayerHitSnapshotPath}': {e}");
		}
	}

	private void UpdatePlayerHitSnapshot()
	{
		if (playerHitSnapshot == null)
			return;

		playerHitSnapshotTimer -= Time.Delta;
		if (playerHitSnapshotTimer > 0)
			return;

		StopPlayerHitSnapshot(false);
	}

	private void StopPlayerHitSnapshot(bool immediate)
	{
		if (playerHitSnapshot == null)
			return;

		try
		{
			playerHitSnapshot.Stop(immediate);
			playerHitSnapshot.Release();
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{PlayerHitSnapshotPath}:stop"))
				Console.Error.WriteLine($"Failed to stop FMOD snapshot '{PlayerHitSnapshotPath}': {e}");
		}
		finally
		{
			playerHitSnapshot = null;
			playerHitSnapshotTimer = 0;
		}
	}
}
