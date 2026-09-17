# Browser Music Encoding

## Goal

Roughly halve the browser's biggest download by encoding music as Vorbis on the FMOD Mobile platform, which the browser build loads. Not urgent: no room shipped to the browser plays music yet.

## Why

The FMOD project encodes per platform (`Assets/Sources/Sounds/FMOD/Metadata/EncodingSetting`):

| Platform | Format | Music.bank |
|---|---|---|
| Desktop | Vorbis, quality 37 | 8.9 MB |
| Mobile (browser) | FADPCM, 24 kHz | 17.5 MB |

FADPCM is cheap to decode, which suits short, frequently fired SFX. It compresses much less than Vorbis, though, and the browser downloads and holds whole banks in memory (no streaming from disk), so long music is where it costs the most. The Mobile banks are ~87% of the browser download.

## Plan

1. In FMOD Studio, override the encoding for the music assets only (per-asset or per-folder platform override) to Vorbis on Mobile. Keep SFX on FADPCM.
2. Rebuild banks and compare `Assets/Sounds/FMOD/Build/Mobile/Music.bank` before and after. `--stage-content browser` prints the staged sizes.
3. Check decode cost in wasm on a phone: FMOD's HTML5 build decodes Vorbis in software, so watch for audio stutter or frame hitches when music starts or changes area.
4. If Vorbis is too heavy on phones, try a lower Vorbis quality or a lower sample rate for music before giving up on it.
