#if EDITOR && DEBUG && !IOS
using Foster.Framework;

namespace Myth;

// Development hot reload: a FileSystemWatcher per asset kind, each debouncing its reload by
// AssetReloadDelay so a burst of writes (or a save that lands as several events) reloads once.
// Watcher callbacks arrive off the main thread, hence the locks; the reload itself runs from
// Update via the matching Update*Reload.
public partial class Manager
{
	const float AssetReloadDelay = 0.25f;

	FileSystemWatcher? assetWatcher;
	FileSystemWatcher? shaderWatcher;
	FileSystemWatcher? audioBankWatcher;
	FileSystemWatcher? particleWatcher;
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
		assetWatcher.Filters.Add("inbetweens.json");
		assetWatcher.Filters.Add("*.kit.json");

		assetWatcher.Changed += (_, _) => RequestAssetReload();
		assetWatcher.Created += (_, _) => RequestAssetReload();
		assetWatcher.Deleted += (_, _) => RequestAssetReload();
		assetWatcher.Renamed += (_, _) => RequestAssetReload();
		assetWatcher.EnableRaisingEvents = true;
	}

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
	void StartParticleWatcher()
	{
		var particlesPath = Path.GetFullPath(ParticleEffectAssets.FolderPath);
		if (!Directory.Exists(particlesPath))
			return;

		particleWatcher = new FileSystemWatcher(particlesPath, "*.json")
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		particleWatcher.Changed += (_, _) => RequestParticleEffectReload();
		particleWatcher.Created += (_, _) => RequestParticleEffectReload();
		particleWatcher.Deleted += (_, _) => RequestParticleEffectReload();
		particleWatcher.Renamed += (_, _) => RequestParticleEffectReload();
		particleWatcher.EnableRaisingEvents = true;
	}

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

	void StartShaderWatcher()
	{
		var shadersPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Shaders"));
		if (!Directory.Exists(shadersPath))
			return;

		shaderWatcher = new FileSystemWatcher(shadersPath, "*.hlsl")
		{
			IncludeSubdirectories = false,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		shaderWatcher.Changed += (_, _) => RequestShaderReload();
		shaderWatcher.Created += (_, _) => RequestShaderReload();
		shaderWatcher.Deleted += (_, _) => RequestShaderReload();
		shaderWatcher.Renamed += (_, _) => RequestShaderReload();
		shaderWatcher.EnableRaisingEvents = true;
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

	void StartAudioBankWatcher()
	{
		var fmodPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Sounds", "FMOD"));
		if (!Directory.Exists(fmodPath))
			return;

		audioBankWatcher = new FileSystemWatcher(fmodPath, "*.bank")
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		audioBankWatcher.Changed += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Created += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Deleted += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Renamed += (_, _) => RequestAudioBankReload();
		audioBankWatcher.EnableRaisingEvents = true;
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
		shaderWatcher?.Dispose();
		shaderWatcher = null;
		particleWatcher?.Dispose();
		particleWatcher = null;
		audioBankWatcher?.Dispose();
		audioBankWatcher = null;
	}
}
#endif
