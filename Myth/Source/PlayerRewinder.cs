using Foster.Framework;
namespace Myth;

internal struct PlayerRewinder(Game game)
{
	readonly RingBuffer<(double, Point2)> playerPositions = new(1000);
	readonly internal bool Rewinding => rewindingHeld != 0;
	float rewindingHeld = 0;
	double RewindFromTime = 0;
	float RewindSpeedup;

	void StopRewind()
	{
		rewindingHeld = 0;
		RewindFromTime = 0;
		RewindSpeedup = 0;
	}

	internal void UpdateDebugPlayerRewind()
	{
		var player = game.GetFirst<Player>();
		if (player == null) return;
		var now = game.Time.Seconds;
		var timeDelta = game.Time.Delta;

		if (game.Controls.Rewind.Value != 0 && playerPositions.Count > 0)
		{
			rewindingHeld += timeDelta * RewindSpeedup;
			RewindSpeedup += timeDelta;
			if (RewindFromTime == 0)
			{
				RewindFromTime = now;
			}
		}
		else
		{
			StopRewind();
		}

		if (rewindingHeld > 0)
		{
			// rewindingHeld: go backwards until we're out of positions.
			if (playerPositions.Count == 0)
			{
				StopRewind();
			}
			else
			{
				var targetTime = RewindFromTime - rewindingHeld;
				var (lastTime, lastPos) = playerPositions[^1];
				if (targetTime <= lastTime)
				{
					player.Position = lastPos;
					playerPositions.RemoveLast();
				}
			}
			return;
		}

		// Not rewinding: maybe add new player positions to the ring buffer.
		var currentPos = player.Position;

		if (playerPositions.Count > 0)
		{
			var (_, lastPos) = playerPositions[^1];
			if (Point2.ManhattanDist(lastPos, currentPos) < 6)
			{
				return;
			}
		}

		playerPositions.Add((now, currentPos));
	}

}
