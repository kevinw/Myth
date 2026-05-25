FMOD iOS libraries
==================

This folder contains the iOS subset copied from `~/src/FMOD Programmers API`.

- Core headers: `api/core/inc`
- Studio headers: `api/studio/inc`
- Device libraries: `libfmod_iphoneos.a`, `libfmodstudio_iphoneos.a`
- Simulator libraries: `libfmod_iphonesimulator.a`, `libfmodstudio_iphonesimulator.a`
- Logging variants are included with the `L` suffix for later debug wiring.

FMOD's iOS platform notes list `AudioToolbox.framework` and
`AVFoundation.framework` as required dependencies; the TinyLink iOS project links
those alongside the device static libraries.
