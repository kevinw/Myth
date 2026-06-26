using Foster.Framework;
namespace Myth;

internal struct PlayerRewinder(Game game) {
	RingBuffer<(double, Point2)> playerPositions = new(1000);
	internal float Rewinding = 0;
	double RewindFromTime = 0;
	float RewindSpeedup;

	void StopRewind() {
		Rewinding = 0;
		RewindFromTime = 0;
		RewindSpeedup = 0;
	}

	internal void UpdateDebugPlayerRewind() {
		var player = game.GetFirst<Player>();
		if (player == null) return;
		var now = game.Time.Seconds;
		var timeDelta = game.Time.Delta;

		if (game.Controls.Rewind.Value != 0 && playerPositions.Count > 0) {
			Rewinding += timeDelta * RewindSpeedup;
			RewindSpeedup += timeDelta;
			if (RewindFromTime == 0) {
				RewindFromTime = now;
			}
		} else {
			StopRewind();
		}

		if (Rewinding > 0) {
			// Rewinding: go backwards until we're out of positions.
			if (playerPositions.Count == 0) {
				StopRewind();
			} else {
				var targetTime = RewindFromTime - Rewinding;
				var (lastTime, lastPos) = playerPositions[playerPositions.Count - 1];
				if (targetTime <= lastTime) {
					player.Position = lastPos;
					playerPositions.RemoveLast();
				}
			}
			return;
		}

		// Not rewinding: maybe add new player positions to the ring buffer.
		var currentPos = player.Position;

		if (playerPositions.Count > 0) {
			var (lastTime, lastPos) = playerPositions[playerPositions.Count - 1];
			if (Point2.ManhattanDist(lastPos, currentPos) < 6) {
				return;
			}
		}

		playerPositions.Add((now, currentPos));
	}

}
