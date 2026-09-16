# VoiSona Song rendering on macOS

OpenUtau can use an installed VoiSona Song Audio Unit to sing notes and lyrics without opening the VoiSona editor for each render. This integration uses a separate native host process and the plugin's public Audio Unit state and rendering interfaces. It is an experimental adapter to VoiSona's serialized song state, not a vendor-supported synthesis API.

## Setup

1. Install VoiSona Song with its Audio Unit plugin and install the desired Chis-A voices using VoiSona.
2. In OpenUtau, choose **Tools → VoiSona → Install as singers**. This registers the provider without copying voice models.
3. Choose **Sign in / Manage voices** if the plugin needs account sign-in or a voice download. Close that window when finished. Account information stays in VoiSona.
4. Choose **Refresh voices**, then select **Chis-A (VoiSona Japanese)** or **Chis-A (VoiSona English / Cross-Lingual)** on an OpenUtau track. The default phonemizer passes lyrics to VoiSona, which supplies pronunciation and timing. For Korean lyrics with the Japanese voice, select **KO to JA**. It sends converted kana to VoiSona while preserving the Hangul in the project, including Korean sound variation and note pronunciation hints. Japanese vowels and added coda vowels approximate Korean sounds; this does not make the Japanese voice a native Korean voice.

Both Chis-A versions appear independently when installed. The Japanese entry uses Japanese lyrics; the English / Cross-Lingual entry uses English lyrics in this initial adapter. Additional cross-lingual note language selection and other VoiSona voices are not exposed yet. Voice installation and licensing remain managed by VoiSona. Singer editing and publishing commands cannot modify or redistribute VoiSona model folders.

## Preserved musical data

USTx and USTx31 use the same renderer. Notes retain their exact sounding pitch, including 31-TET positions, note tuning, pitch points, vibrato and pitch deviation curves. Pitch is resampled from OpenUtau's tick-based curve into VoiSona's 5 ms absolute LogF0 curve. Note onset and duration follow the project tempo map; the adapter expresses those times at a fixed native tempo, so VoiSona's host tempo sync stays off.

OpenUtau applies dynamics to the completed audio. The renderer uses one phonemized lyric per note. UTAU phonemizers that emit multiple sample aliases per note are rejected; use DEFAULT or KO to JA. Unconverted Hangul is rejected before synthesis, rather than accepting the Japanese engine's near-silent output. The renderer does not advertise support for other voice expressions, sample-level phoneme timing, rendered-pitch extraction, or VoiSona's expressive controls. Consecutive `+` and `+~` notes extend the preceding syllable while retaining the pitch curve; their different reattack behavior is not reproduced. An extension without a preceding lyric or across a rest is rejected. Overlapping notes and phrases longer than ten minutes are rejected with an error.

The helper renders the entire phrase with 500 ms before and after it, at 44.1 kHz, then OpenUtau places it at the original timeline position. Rests between phrases remain in the timeline. VoiSona may pronounce and time syllables differently from another engine.

## Startup, cancellation and failures

Rendering uses a fresh plugin instance for each uncached phrase and is serialized across VoiSona tracks. It starts the audio graph before loading musical state and pumps the native event loop with stopped transport while VoiSona prepares. The plugin's `PluginState` parameter is not treated as a readiness signal.

A phrase is accepted only after consecutive complete captures agree and every note window contains nonzero audio. The helper retries up to eight captures and has a bounded deadline of the greater of 150 seconds or twice the audio duration plus 30 seconds. OpenUtau enforces the same process deadline with an additional ten seconds for cleanup. These checks reduce silent and partially prepared output; they cannot establish linguistic correctness. A silent voice, unavailable license, expired sign-in, or a slow initialization produces an actionable failure instead of a silent cache entry.

Cancellation requests normal plugin cleanup first. If it does not exit within three seconds, OpenUtau terminates that job's process tree. Each helper enters an empty private working directory before loading VoiSona, preventing teardown from scanning the GUI process’s inherited directory. Independent native guards terminate a child if its parent exits, if the overall deadline expires, or if teardown stalls for five seconds. After a complete WAV and atomic success manifest have been written, a teardown stall preserves that successful result; failures and cancellation retain a failure exit code. Temporary files and unsuccessful captures are removed. Successful captures are validated again before entering the cache. Keys include generated state, duration, voice version and file metadata, plugin version/binary metadata, helper contents and adapter schema. Dynamics are applied after loading the cache. Changing any musical input represented in the state causes a new render.

## Build and verification

On macOS, the Core project compiles `native/voisona/main.swift` with the Xcode command-line tools and copies `openutau-voisona-host` through build and publish outputs. The architecture follows `osx-arm64` or `osx-x64`, falling back to the build process architecture. Other platforms do not build or discover this renderer.

Run focused tests:

```sh
dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter FullyQualifiedName~VoiSona
```

Real-engine tests require both Chis-A voices installed and the plugin signed in:

```sh
OPENUTAU_TEST_VOISONA=1 dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter FullyQualifiedName~VoiSona
```

Set `OPENUTAU_VOISONA_ARTIFACTS` to a directory to retain generated USTx31 projects, rendered WAVs and native requests for independent inspection. These tests create their own fixtures and do not modify source songs. VoiSona updates can change its state format or synthesis behavior; repeat the installed-engine checks after an update.
