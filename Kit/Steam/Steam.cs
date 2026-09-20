#if STEAM
using Steamworks;
#endif
using Foster.Framework;

namespace Kit;

/// Thin wrapper over Steamworks.NET. Steam is always optional: every call is a no-op when it isn't
/// running (the usual dev case), when the player owns the game elsewhere, or on platforms built
/// without STEAM defined (browser, and other games sharing Kit).
public static class Steam
{
	public static bool Running { get; private set; }

	/// Invoked when Steam has delivered the user's stats and achievements, which is asynchronous
	/// and happens some frames after Init. Achievements wires itself up to this.
	public static Action? StatsReceived;

#if STEAM
	static Callback<UserStatsReceived_t>? statsCallback;

	public static void Init(uint appId)
	{
		if (Running)
			return;

		if (SteamAPI.InitEx(out var error) is var result && result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
		{
			Log.Info($"Steam unavailable ({result}): {error}");
			return;
		}

		// A mismatch means steam_appid.txt lost a fight with an installed copy of another game.
		if (SteamUtils.GetAppID().m_AppId != appId)
			Log.Warning($"Steam returned app {SteamUtils.GetAppID().m_AppId}, expected {appId}: achievements will go to the wrong app.");

		Running = true;
		statsCallback = Callback<UserStatsReceived_t>.Create(_ => StatsReceived?.Invoke());
		Log.Info($"Steam ready: {SteamFriends.GetPersonaName()}, app {SteamUtils.GetAppID().m_AppId}{(SteamUtils.IsSteamRunningOnSteamDeck() ? ", on a Steam Deck" : "")}");
	}

	public static void Update()
	{
		if (Running)
			SteamAPI.RunCallbacks();
	}

	public static void Shutdown()
	{
		if (!Running)
			return;
		Running = false;
		statsCallback?.Dispose();
		statsCallback = null;
		SteamAPI.Shutdown();
	}

	/// False while Steam still hasn't delivered stats, so the caller should ask again later
	/// rather than treat the achievement as un-earned.
	public static bool TryGetAchievement(string id, out bool unlocked)
	{
		unlocked = false;
		return Running && SteamUserStats.GetAchievement(id, out unlocked);
	}

	public static void SetAchievement(string id)
	{
		if (!Running || !SteamUserStats.SetAchievement(id))
			return;
		SteamUserStats.StoreStats();
		Log.Info($"Steam achievement unlocked: {id}");
	}

	/// Dev only: hands an achievement back so it can be earned again.
	public static void ClearAchievement(string id)
	{
		if (Running && SteamUserStats.ClearAchievement(id))
			SteamUserStats.StoreStats();
	}
#else
	public static void Init(uint appId) { }
	public static void Update() { }
	public static void Shutdown() { }
	public static bool TryGetAchievement(string id, out bool unlocked) { unlocked = false; return false; }
	public static void SetAchievement(string id) { }
	public static void ClearAchievement(string id) { }
#endif
}
