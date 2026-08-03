#if EDITOR && DEBUG && !IOS
using Foster.Framework;

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

		assetWatcher.Changed += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Created += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Deleted += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.Renamed += (_, e) => RouteAssetChange(e.FullPath);
		assetWatcher.EnableRaisingEvents = true;
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
		if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, "Particles"))
			RequestParticleEffectReload();
		else if (extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, "Shaders"))
			RequestShaderReload();
		else if (AudioEnabled && extension.Equals(".bank", StringComparison.OrdinalIgnoreCase) && IsAssetDirectory(relative, Path.Join("Sounds", "FMOD")))
			RequestAudioBankReload();
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
		assetWatcher?.Dispose();
		assetWatcher = null;
	}
}
#endif
