# Myth iOS Spike

This folder contains a minimal Foster iOS smoke app. It is intentionally separate
from the full Myth game so the first milestone can isolate .NET iOS, Foster,
SDL, and Metal before asset loading, FMOD, and editor code are involved.

## Build

```bash
dotnet workload restore Platforms/iOS/FosterIosSmoke/FosterIosSmoke.csproj
dotnet build Platforms/iOS/FosterIosSmoke/FosterIosSmoke.csproj -f net10.0-ios -r iossimulator-arm64
dotnet build Platforms/iOS/FosterIosSmoke/FosterIosSmoke.csproj -f net10.0-ios -r ios-arm64 /p:ArchiveOnBuild=false
```

## Run on Device

List devices:

```bash
xcrun devicectl list devices
```

Install and launch the smoke app, replacing the device id as needed:

```bash
xcrun devicectl device install app --device <device-id> Platforms/iOS/FosterIosSmoke/bin/Debug/net10.0-ios/ios-arm64/FosterIosSmoke.app
xcrun devicectl device process launch --device <device-id> com.kev.myth.smoke
```

Check whether it is still running:

```bash
xcrun devicectl device info processes --device <device-id>
```

## SDL3

The project has an optional native reference to:

```text
Platforms/iOS/Native/SDL3.xcframework
```

If that folder exists, MSBuild includes it as a native framework reference. Get
`SDL3.xcframework` from the SDL3 release disk image or build it from the SDL
Xcode project, then copy it to that path.

The smoke app embeds SDL3 from the xcframework and maps Foster's `SDL3` native
imports to the embedded framework at startup.

## Notes

The iOS simulator currently gets through SDL initialization but fails at
`SDL_CreateGPUDevice` with SDL reporting that the simulator does not meet the
hardware requirements for SDL_GPU Metal. Use a physical iOS device for the
graphics smoke test.
