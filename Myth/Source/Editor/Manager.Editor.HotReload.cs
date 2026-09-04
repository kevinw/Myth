#if EDITOR && DEBUG && !IOS
using Foster.Framework;
using ImGuiNET;
using System.Numerics;

namespace Myth;

// Development hot reload: one FileSystemWatcher routes each asset kind to its own debounced reload,
// so a burst of writes (or a save that lands as several events) reloads once.
// Watcher callbacks arrive off the main thread, hence the locks; the reload itself runs from
// Update via the matching Update*Reload.
public partial class Manager
{
	const float AssetReloadDelay = 0.25f;

	FileSystemWatcher? assetWatcher;
	readonly Lock assetReloadLock = new();
	bool assetReloadRequested;
	float assetReloadTimer;
	readonly Lock particleReloadLock = new();
	bool particleReloadRequested;
	float particleReloadTimer;
	readonly Lock shaderReloadLock = new();
	bool shaderReloadRequested;
	float shaderReloadTimer;
	readonly Lock audioBankReloadLock = new();
	bool audioBankReloadRequested;
	float audioBankReloadTimer;
	readonly Lock rigReloadLock = new();
	bool rigReloadRequested;
	float rigReloadTimer;
	readonly Lock roomReloadLock = new();
	readonly HashSet<string> changedRoomPaths = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> deletedRoomPaths = new(StringComparer.OrdinalIgnoreCase);
	bool roomReloadRequested;
	float roomReloadTimer;
	Room? roomSaveConflict;
	string? roomSaveConflictPath;
	bool roomSaveConflictPopupRequested;

	void StartAssetWatcher()
	{
		assetWatcher = new FileSystemWatcher(Path.GetFullPath(Assets.AssetsPath))
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};
		assetWatcher.Filters.Add("*.ase");
		assetWatcher.Filters.Add("*.aseprite");
		assetWatcher.Filters.Add("*.json");
		assetWatcher.Filters.Add("*.hlsl");
		assetWatcher.Filters.Add("*.bank");
		assetWatcher.Filters.Add("*.ozzpack");

		assetWatcher.Changed += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Created += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Deleted += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Renamed += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.EnableRaisingEvents = true;
		Assets.RoomSaveConflict += HandleRoomSaveConflict;
		Assets.RoomFileDeleted += NoteDeletedRoomFile;
	}

	void RouteAssetChange(string path)
	{
		var extension = Path.GetExtension(path);
		if (extension.Equals(".ase", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".aseprite", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileName(path).Equals("inbetweens.json", StringComparison.OrdinalIgnoreCase) ||
			path.EndsWith(".kit.json", StringComparison.OrdinalIgnoreCase))
		{
			RequestAssetReload();
			return;
		}

		var relative = Path.GetRelativePath(Assets.AssetsPath, path);
		if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && Assets.IsCurrentWorldRoomPath(path))
			RequestRoomReload(path);
		else if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, "Particles"))
			RequestParticleEffectReload();
		else if (extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, "Shaders"))
			RequestShaderReload();
		else if (AudioEnabled && extension.Equals(".bank", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, Path.Join("Sounds", "FMOD")))
			RequestAudioBankReload();
		else if (extension.Equals(".ozzpack", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, "Rigs"))
			RequestRigReload();
	}

	void RequestRigReload()
	{
		lock (rigReloadLock)
		{
			rigReloadRequested = true;
			rigReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateRigReload()
	{
		var reload = false;
		lock (rigReloadLock)
		{
			if (rigReloadRequested && (rigReloadTimer -= Time.Delta) <= 0)
			{
				rigReloadRequested = false;
				reload = true;
			}
		}
		if (!reload || game == null)
			return;
		StopRigAnimationPreview();
		game.ReloadRoom(reloadRigs: true);
		Game.NotifyHotReload("Reloaded rig pack");
	}

	static bool IsAssetDirectory(string relative, string directory)
		=> relative.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

	void RequestAssetReload()
	{
		lock (assetReloadLock)
		{
			assetReloadRequested = true;
			assetReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateAssetReload()
	{
		var reload = false;

		lock (assetReloadLock)
		{
			if (assetReloadRequested)
			{
				assetReloadTimer -= Time.Delta;
				if (assetReloadTimer <= 0)
				{
					assetReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			ReloadAssets(false);
	}

	void RequestRoomReload(string path)
	{
		lock (roomReloadLock)
		{
			changedRoomPaths.Add(Path.GetFullPath(path));
			roomReloadRequested = true;
			roomReloadTimer = AssetReloadDelay;
		}
	}

	void HandleRoomSaveConflict(Room room, string path)
	{
		roomSaveConflict = room;
		roomSaveConflictPath = Path.GetFullPath(path);
		roomSaveConflictPopupRequested = true;
		RequestRoomReload(path);
	}

	void NoteDeletedRoomFile(string path)
	{
		lock (roomReloadLock)
			deletedRoomPaths.Add(Path.GetFullPath(path));
	}

	bool ConsumeDeletedRoomFile(string path)
	{
		lock (roomReloadLock)
		{
			if (!deletedRoomPaths.Remove(path))
				return false;
			return !File.Exists(path);
		}
	}

	void UpdateRoomReload()
	{
		string[] paths;
		lock (roomReloadLock)
		{
			if (!roomReloadRequested || playing || editorGameDirty || roomSaveConflict != null)
				return;
			roomReloadTimer -= Time.Delta;
			if (roomReloadTimer > 0)
				return;
			paths = [.. changedRoomPaths];
			changedRoomPaths.Clear();
			roomReloadRequested = false;
		}

		string? externalPath = null;
		foreach (var path in paths)
			if (!ConsumeDeletedRoomFile(path) && !Assets.RoomFileMatchesLoadedRevision(path))
			{
				externalPath = path;
				break;
			}
		if (externalPath == null)
			return;

		ReloadExternallyChangedRooms(externalPath);
	}

	void ReloadExternallyChangedRooms(string changedPath)
	{
		var selectedActorId = CurrentRoom is { } oldRoom && selectedRoomActorIndex >= 0 && selectedRoomActorIndex < oldRoom.Actors.Count
			? ActorSpecs.GetActorId(oldRoom.Actors[selectedRoomActorIndex])
			: null;
		var oldRooms = new Dictionary<(int Sublayer, Point2 Cell), Room>(Assets.Rooms);
		var oldRoomsById = new Dictionary<string, Room>(Assets.RoomsById, StringComparer.OrdinalIgnoreCase);

		try
		{
			Assets.ReloadRooms();
			Assets.RebuildRoomGeometry();
		}
		catch (Exception e)
		{
			Assets.Rooms.Clear();
			foreach (var entry in oldRooms)
				Assets.Rooms.Add(entry.Key, entry.Value);
			Assets.RoomsById.Clear();
			foreach (var entry in oldRoomsById)
				Assets.RoomsById.Add(entry.Key, entry.Value);
			Console.Error.WriteLine($"Room hot reload failed for '{changedPath}': {e.Message}");
			return;
		}

		undo.Clear();
		ClearEditorDragState();
		CancelTilePaint();
		editorSelectionRect = null;
		ClearRoomPreviewCache();
		ResolveSelectedRoom();
		selectedRoomActorIndex = -1;
		if (selectedActorId != null && CurrentRoom is { } room)
			for (var i = 0; i < room.Actors.Count; i++)
				if (string.Equals(ActorSpecs.GetActorId(room.Actors[i]), selectedActorId, StringComparison.OrdinalIgnoreCase))
				{
					selectedRoomActorIndex = i;
					break;
				}
		SyncEditorGame();
		Console.WriteLine($"Room hot reload: {Path.GetFileName(changedPath)}");
	}

	void DrawRoomSaveConflictPopup()
	{
		if (roomSaveConflictPopupRequested)
		{
			roomSaveConflictPopupRequested = false;
			ImGui.OpenPopup("Room changed on disk");
		}

		var open = true;
		if (!BeginPaddedPopupModal("Room changed on disk", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;
		if (!open)
			roomSaveConflictPopupRequested = true;

		ImGui.TextUnformatted($"'{roomSaveConflict?.Id}' was changed outside the editor.");
		ImGui.TextUnformatted("Which version do you want to keep?");
		if (ImGui.Button("Reload from disk", new Vector2(130, 0)))
		{
			var path = roomSaveConflictPath!;
			roomSaveConflict = null;
			roomSaveConflictPath = null;
			ReloadExternallyChangedRooms(path);
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Keep editor version", new Vector2(145, 0)))
		{
			var room = roomSaveConflict!;
			roomSaveConflict = null;
			roomSaveConflictPath = null;
			room.Save(overwriteExternal: true);
			ImGui.CloseCurrentPopup();
		}
		EndPaddedPopup();
	}

	// Particle effect JSONs get their own watcher: the general asset watcher
	// only reacts to art files, and a full ReloadAssets would be overkill for a
	// particle tweak. Reload is deferred while a tuning-window autosave is
	// pending so an external edit can't clobber an in-progress slider drag.
	void RequestParticleEffectReload()
	{
		lock (particleReloadLock)
		{
			particleReloadRequested = true;
			particleReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateParticleEffectReload()
	{
		var reload = false;
		lock (particleReloadLock)
		{
			if (particleReloadRequested && !particleEditor.AutosavePending)
			{
				particleReloadTimer -= Time.Delta;
				if (particleReloadTimer <= 0)
				{
					particleReloadRequested = false;
					reload = true;
				}
			}
		}
		if (reload)
			ParticleEffectAssets.Load();
	}

	void RequestShaderReload()
	{
		lock (shaderReloadLock)
		{
			shaderReloadRequested = true;
			shaderReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateShaderReload()
	{
		var reload = false;

		lock (shaderReloadLock)
		{
			if (shaderReloadRequested)
			{
				shaderReloadTimer -= Time.Delta;
				if (shaderReloadTimer <= 0)
				{
					shaderReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			CompileAndReloadShaders();
	}

	void RequestAudioBankReload()
	{
		lock (audioBankReloadLock)
		{
			audioBankReloadRequested = true;
			audioBankReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateAudioBankReload()
	{
		var reload = false;

		lock (audioBankReloadLock)
		{
			if (audioBankReloadRequested)
			{
				audioBankReloadTimer -= Time.Delta;
				if (audioBankReloadTimer <= 0)
				{
					audioBankReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			ReloadAudioBanks();
	}

	void DisposeAssetWatchers()
	{
		Assets.RoomSaveConflict -= HandleRoomSaveConflict;
		Assets.RoomFileDeleted -= NoteDeletedRoomFile;
		assetWatcher?.Dispose();
		assetWatcher = null;
	}
}
#endif
