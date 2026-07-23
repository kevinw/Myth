#if EDITOR
using System.Numerics;
using Foster.Framework;

namespace Myth;

// Every room in the editor world draws as a rendered preview of itself. The current room renders
// live into activeRoomPreviewTarget; every other room renders once into a cached Target and is
// reused until something invalidates it. Both are keyed off a per-room version counter, so the
// only thing the rest of the editor needs to say is "this room changed" via MarkRoomPreviewDirty
// or InvalidateRoomPreview.
//
// Targets in use by the current frame's batcher can't be disposed immediately, so disposals are
// deferred to the top of the next frame (DisposeDeferredRoomPreviews).
public partial class Manager
{
	const float DefaultRoomPreviewResolutionScale = 0.75f;

	readonly Dictionary<string, EditorRoomPreview> roomPreviewCache = [];
	readonly Dictionary<string, int> roomPreviewVersions = [];
	readonly Queue<string> roomPreviewBuildQueue = [];
	readonly HashSet<string> queuedRoomPreviewIds = [];
	readonly List<IDisposable> deferredRoomPreviewDisposals = [];
	int nextRoomPreviewVersion = 1;
	Target? activeRoomPreviewTarget;
	string? activeRoomPreviewId;
	// Cache state for the active room preview: it is only re-rendered when one of these
	// changed, instead of every frame (the full lighting/visibility render was the editor's
	// dominant cost). See GetActiveRoomPreview.
	int activeRoomPreviewVersion = -1;
	bool activeRoomPreviewShowForeground;
	bool activeRoomPreviewShowBackground;
	bool activeRoomPreviewAnimating;
	float roomPreviewResolutionScale = DefaultRoomPreviewResolutionScale;

	Texture? GetEditorRoomTexture(Room room)
		=> room == CurrentRoom && game != null ? GetActiveRoomPreview(room) : GetRoomPreview(room);

	Texture GetActiveRoomPreview(Room room)
	{
		using var perf = PerfStats.Time(PerfDetail.ActiveRoomPreview);
		var bounds = room.WorldBounds;
		var recreated = false;
		if (activeRoomPreviewTarget == null ||
			activeRoomPreviewId != room.Id ||
			activeRoomPreviewTarget.Width != bounds.Width ||
			activeRoomPreviewTarget.Height != bounds.Height)
		{
			DisposeRoomPreview(activeRoomPreviewTarget, defer: false);
			activeRoomPreviewTarget = new Target(GraphicsDevice, bounds.Width, bounds.Height, name: $"ActiveRoomPreview/{room.Id}");
			activeRoomPreviewId = room.Id;
			recreated = true;
		}

		// Re-render only when the image could have changed: a room edit (version bump, the
		// same signal cached previews use, set by MarkRoomPreviewDirty/InvalidateRoomPreview),
		// a layer-visibility toggle, the target was recreated (room/size change), or a
		// particle emitter is animating live. Otherwise reuse last frame's render â€” the full
		// lighting/visibility pass was the editor's dominant per-frame cost.
		var version = GetRoomPreviewVersion(room);
		var needsRender = game != null && (recreated
			|| activeRoomPreviewVersion != version
			|| activeRoomPreviewShowForeground != editorShowForegroundLayer
			|| activeRoomPreviewShowBackground != editorShowBackgroundLayer
			|| activeRoomPreviewAnimating);

		if (needsRender)
		{
			game!.ShowForegroundLayer = editorShowForegroundLayer;
			game.ShowBackgroundLayer = editorShowBackgroundLayer;
			game.RenderWorldToTarget(activeRoomPreviewTarget, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
			game.ShowForegroundLayer = true;
			game.ShowBackgroundLayer = true;
			activeRoomPreviewVersion = version;
			activeRoomPreviewShowForeground = editorShowForegroundLayer;
			activeRoomPreviewShowBackground = editorShowBackgroundLayer;
		}
		return activeRoomPreviewTarget.Attachments[0];
	}

	Texture? GetRoomPreview(Room room)
	{
		EditorRoomPreview? entry;
		var version = GetRoomPreviewVersion(room);
		using (PerfStats.Time(PerfDetail.RoomPreviewCacheCheck))
		{
			if (roomPreviewCache.TryGetValue(room.Id, out entry) &&
				IsRoomPreviewCurrent(room, entry, version))
				return entry.Texture;
		}

		EnqueueRoomPreview(room);
		if (entry != null)
			return entry.Texture;
		return null;
	}

	void BuildQueuedRoomPreview()
	{
		while (roomPreviewBuildQueue.Count > 0)
		{
			var roomId = roomPreviewBuildQueue.Dequeue();
			queuedRoomPreviewIds.Remove(roomId);
			if (!Assets.RoomsById.TryGetValue(roomId, out var room))
				continue;

			var version = GetRoomPreviewVersion(room);
			if (roomPreviewCache.TryGetValue(room.Id, out var cached) && IsRoomPreviewCurrent(room, cached, version))
				continue;

			BuildRoomPreview(room, version);
			return;
		}
	}

	bool IsRoomPreviewCurrent(Room room, EditorRoomPreview entry, int version)
	{
		var previewSize = GetRoomPreviewPixelSize(room);
		return
			entry.Version == version &&
			entry.ShowForegroundLayer == editorShowForegroundLayer &&
			entry.ShowBackgroundLayer == editorShowBackgroundLayer &&
			entry.Target.Width == previewSize.X &&
			entry.Target.Height == previewSize.Y;
	}

	void EnqueueRoomPreview(Room room)
	{
		if (queuedRoomPreviewIds.Add(room.Id))
			roomPreviewBuildQueue.Enqueue(room.Id);
	}

	void BuildRoomPreview(Room room, int version)
	{
		using var perf = PerfStats.Time(PerfDetail.RoomPreviewBuild);
		if (roomPreviewCache.Remove(room.Id, out var entry))
			DisposeRoomPreview(entry, defer: false);

		var previewSize = GetRoomPreviewPixelSize(room);
		var target = new Target(GraphicsDevice, previewSize.X, previewSize.Y, name: $"RoomPreview/{room.Id}");
		using var previewGame = new Game(this, room, audioEventsEnabled: false, spawnBud: false, enableParticles: false, loadGoals: true);
		previewGame.ShowForegroundLayer = editorShowForegroundLayer;
		previewGame.ShowBackgroundLayer = editorShowBackgroundLayer;
		previewGame.RenderWorldToTarget(target, new Rect(room.WorldBounds.X, room.WorldBounds.Y, room.WorldBounds.Width, room.WorldBounds.Height));
		roomPreviewCache[room.Id] = new EditorRoomPreview(version, editorShowForegroundLayer, editorShowBackgroundLayer, target);
	}

	Point2 GetRoomPreviewPixelSize(Room room)
	{
		var bounds = room.WorldBounds;
		var scale = Math.Clamp(roomPreviewResolutionScale, 0.05f, 1.0f);
		return new Point2(
			Math.Max(1, (int)MathF.Ceiling(bounds.Width * scale)),
			Math.Max(1, (int)MathF.Ceiling(bounds.Height * scale)));
	}

	void ClearRoomPreviewCache(bool deferDisposal = true)
	{
		foreach (var preview in roomPreviewCache.Values)
			DisposeRoomPreview(preview, deferDisposal);
		roomPreviewCache.Clear();
		roomPreviewBuildQueue.Clear();
		queuedRoomPreviewIds.Clear();
		roomPreviewVersions.Clear();
		nextRoomPreviewVersion = 1;
		DisposeRoomPreview(activeRoomPreviewTarget, deferDisposal);
		activeRoomPreviewTarget = null;
		activeRoomPreviewId = null;
	}

	int GetRoomPreviewVersion(Room room)
	{
		if (!roomPreviewVersions.TryGetValue(room.Id, out var version))
		{
			version = nextRoomPreviewVersion++;
			roomPreviewVersions[room.Id] = version;
		}
		return version;
	}

	void MarkRoomPreviewDirty(Room room, bool deferDisposal = true)
	{
		roomPreviewVersions[room.Id] = nextRoomPreviewVersion++;
		if (roomPreviewCache.Remove(room.Id, out var preview))
			DisposeRoomPreview(preview, deferDisposal);
	}

	void InvalidateRoomPreview(Room room, bool deferDisposal = true, bool bumpVersion = true)
	{
		if (bumpVersion)
			roomPreviewVersions[room.Id] = nextRoomPreviewVersion++;

		if (roomPreviewCache.Remove(room.Id, out var preview))
			DisposeRoomPreview(preview, deferDisposal);

		if (activeRoomPreviewId == room.Id)
		{
			DisposeRoomPreview(activeRoomPreviewTarget, deferDisposal);
			activeRoomPreviewTarget = null;
			activeRoomPreviewId = null;
		}
	}

	void DisposeRoomPreview(IDisposable? disposable, bool defer)
	{
		if (disposable == null)
			return;

		if (defer)
			deferredRoomPreviewDisposals.Add(disposable);
		else
			disposable.Dispose();
	}

	void DisposeDeferredRoomPreviews()
	{
		foreach (var disposable in deferredRoomPreviewDisposals)
			disposable.Dispose();
		deferredRoomPreviewDisposals.Clear();
	}

	sealed class EditorRoomPreview(int version, bool showForegroundLayer, bool showBackgroundLayer, Target target) : IDisposable
	{
		public readonly int Version = version;
		public readonly bool ShowForegroundLayer = showForegroundLayer;
		public readonly bool ShowBackgroundLayer = showBackgroundLayer;
		public readonly Target Target = target;
		public Texture Texture => Target.Attachments[0];

		public void Dispose()
			=> Target.Dispose();
	}
}
#endif
