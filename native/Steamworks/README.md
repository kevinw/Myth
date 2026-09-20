# Steamworks.NET

Vendored from the [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) **2025.164.1**
release, which wraps Steamworks SDK **1.64** — newer than anything on NuGet (2024.8.0 = SDK 1.60).

`src/` is the upstream `com.rlabrecque.steamworks.net/Runtime` tree (Unity `.meta`/`.asmdef` files
dropped) and `Steamworks.NET.csproj` builds it. The release's **prebuilt** assemblies are not usable
here: they target .NET Framework 4.8 and their IL is stamped AMD64, so a .NET 10 arm64 process
refuses to load them ("assembly architecture is not compatible"). Building from source also keeps
the IL trimmable and NativeAOT-friendly.

`STEAMWORKS_WIN` vs `STEAMWORKS_LIN_OSX` picks the P/Invoke calling convention and the native
library name, so it has to agree with whichever `steam_api` binary sits next to the executable.
The csproj derives it from `RuntimeIdentifier`, falling back to the host OS.

| File | Notes |
| --- | --- |
| `osx-linux/libsteam_api.dylib` | universal (x86_64 + arm64) |
| `osx-linux/libsteam_api.so` | x86_64 (covers Steam Deck) |
| `windows/steam_api64.dll` | x64 |

To update: download `Steamworks.NET-Standalone_<ver>.zip` for the `steam_api` binaries and the
matching source tarball for `src/`, then re-run a desktop build.

`steam_appid.txt` is written into the build output by `MythWriteSteamAppId` in Myth.csproj from
`app.json`'s `steamAppId`, so it is not stored here. It exists only so an un-installed dev build can
identify itself; a real Steam release must not ship it.

Steamworks.NET is MIT (`LICENSE.txt`); the `steam_api` binaries are Valve redistributables covered
by the Steamworks SDK Access Agreement.
