# VoiSona Song rendering on macOS

OpenUtau can use an installed VoiSona Song Audio Unit to sing notes and lyrics without opening the VoiSona editor for each render. This integration uses a separate native host process and the plugin's public Audio Unit state and rendering interfaces. It is an experimental adapter to VoiSona's serialized song state, not a vendor-supported synthesis API.

## Setup

1. Install VoiSona Song with its Audio Unit plugin and install the desired Chis-A voices using VoiSona.
2. In OpenUtau, choose **Tools → VoiSona → Install as singers**. This registers the provider without copying voice models.
3. Choose **Sign in / Manage voices** if the plugin needs account sign-in or a voice download. Close that window when finished. Account information stays in VoiSona.
4. Choose **Refresh voices**, then select **Chis-A (VoiSona Japanese)** or **Chis-A (VoiSona English / Cross-Lingual)** on an OpenUtau track. The default phonemizer passes lyrics to VoiSona, which supplies pronunciation and timing. For Korean lyrics with the Japanese voice, select **KO to JA**. It sends converted kana to VoiSona while preserving the Hangul in the project, including Korean sound variation and note pronunciation hints. For sung clarity, Hangul h-initial syllables retain their onset and form a sound-variation boundary; explicit hints still take precedence. Stop codas /k t p/ use Japanese small tsu (っ) as a vowel-free closure approximation, losing their distinct places of articulation. Sonorant codas retain the kana approximations ん, む, and る. Japanese vowels and these coda approximations do not make the Japanese voice a native Korean voice.

Both Chis-A versions appear independently when installed. The Japanese entry uses Japanese lyrics; the English / Cross-Lingual entry uses English lyrics in this initial adapter. Additional cross-lingual note language selection and other VoiSona voices are not exposed yet. Voice installation and licensing remain managed by VoiSona. Singer editing and publishing commands cannot modify or redistribute VoiSona model folders.

## Preserved musical data

USTx and USTx31 use the same renderer. Notes retain their exact sounding pitch, including 31-TET positions, note tuning, pitch points, vibrato and pitch deviation curves. Pitch is resampled from OpenUtau's tick-based curve into VoiSona's 5 ms absolute LogF0 curve. Note onset and duration follow the project tempo map; the adapter expresses those times at a fixed native tempo, so VoiSona's host tempo sync stays off.

OpenUtau applies dynamics to the completed audio. The renderer uses one phonemized lyric per note. UTAU phonemizers that emit multiple sample aliases per note are rejected; use DEFAULT or KO to JA. Unconverted Hangul is rejected before synthesis, rather than accepting the Japanese engine's near-silent output. Native voice controls are described below. The renderer does not advertise sample-level phoneme timing or rendered-pitch extraction. Consecutive `+` and `+~` notes extend the preceding syllable while retaining the pitch curve; their different reattack behavior is not reproduced. An extension without a preceding lyric or across a rest is rejected. Overlapping notes and individual native render jobs longer than ten minutes are rejected with an error.

Long phrases are divided into chunks targeting 12 seconds of owned audio, at adjacent fresh-lyric boundaries. A syllable and its `+` chain stay together, so the target is not a hard maximum. Short rests stay inside the existing phrase and are never artificial cut points. Each chunk synthesizes a complete neighboring syllable on either side for context, with 500 ms native padding, at 44.1 kHz. OpenUtau applies the existing pitch and dynamics processing to that context, then retains only the chunk's owned timeline interval plus a complementary 20 ms crossfade at each internal seam. Original phrase head/tail padding and timeline rests remain intact. Playback and export use the same chunk audio and placement.

This reduces the work needed before every track has initial audio. Playback still waits for all tracks needed at the current position; it never starts an incomplete mix. Playback prioritizes chunks at the playhead, and unfocused background preparation warms chunks in timeline order across tracks. Playback and background preparation admit up to two independent native jobs at a time, in priority order, and publish each completed chunk immediately. Mixdown and stem export admit up to four jobs across tracks to overlap plugin initialization. Both limits are reduced to half the logical processor count (at least one); a process-wide four-job ceiling bounds overlapping passes. Identical cache entries are locked during rendering and writes. Cancellation or a failed job cancels and reaps the remaining jobs in that pass. A long sustained syllable or a phrase made entirely of separated notes can exceed the target. Extra plugin launches and repeated context can increase total cold-render work, and independent synthesis can change pronunciation/timbre at chunk boundaries despite the context and crossfade; the audio is not promised to equal a single whole-phrase render.

## Native voice parameters

Open **Track Settings** on a VoiSona track for all five native global parameters:

| Control | Native range | Effect |
| --- | --- | --- |
| TUNE | -1 to 1 | Pitch accuracy; enabled with VoiSona-generated pitch |
| VIA | 0 to 10 | Native vibrato amplitude multiplier; 0 disables native vibrato |
| VIF | 0 to 2 | Native vibrato frequency multiplier |
| ALP | -1 to 1 | Age: lower is more childlike, higher is more mature |
| HUS | -10 to 10 | Huskiness: higher is huskier |

Settings belong to the track, persist in USTx/USTx31, support undo, and invalidate both in-memory audio and native render caches. Closing the dialog without OK discards its edits. These ranges were checked against the installed VoiSona editor; the [vendor parameter manual](https://manual.voisona.com/en/song/pc/2b6e9bc7efb1807bb8dacbf63048ba39) describes their meaning.

By default OpenUtau supplies exact pitch, including 31-TET, pitch curves and note vibrato, and disables native vibrato. Raising VIA adds native vibrato. **Use VoiSona-generated pitch** instead lets the native engine generate pitch and makes TUNE effective; it replaces OpenUtau pitch curves and note vibrato and uses rounded 12-TET note anchors. It is explicitly opt-in and does not change stored notes. Keep it off for exact 31-TET tuning.

For local age/huskiness edits, open **Expressions → Get suggestions**, accept, and choose **ALP** or **HUS** in a piano-roll expression lane. Integer lane values are hundredths of native units: ALP -100…100, HUS -1000…1000. Curves are sent as native Alpha/Husky data on the same 5 ms timeline as pitch, including tempo changes and chunk context. An untouched/zero lane leaves native generated values alone; a nonzero lane supplies a curve across that phrase. Native global values remain active alongside these curves.

Volume remains available through **DYN**, applied to the synthesized audio; detailed pitch and vibrato remain available through OpenUtau's piano-roll controls. Native phoneme timing and native volume/vibrato curve editors are not bridged. Both currently supported Chis-A variants provide only the Normal singing style, so there are no style proportions to blend. Other voices and style discovery remain outside this adapter's current singer support.

## Startup, cancellation and failures

Rendering uses a fresh plugin instance for each uncached chunk. Bounded parallel jobs overlap startup waits; instances are not pooled and export retains the same chunking, context, and cache as playback. It starts the audio graph before loading musical state and pumps the native event loop with stopped transport while VoiSona prepares. The plugin's `PluginState` parameter is not treated as a readiness signal.

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

### Bounded concurrency measurement (2026-09-16)

With VoiSona AU 1.18.0.5 on an 8-logical-processor, 16 GiB Mac, a matched fixture of four distinct Japanese phrases (6.5 seconds of rendered audio each) was run in the order serial, four workers, two workers, four workers, serial. Each pass removed its own OpenUtau audio cache and launched fresh helpers; OS/model file caches were not flushed. Timings included helper startup, readiness checks, capture, teardown, and FLAC cache writing:

| Workers | Elapsed seconds |
| --- | --- |
| 1 | 48.10, 44.71 |
| 2 (playback limit) | 26.80 |
| 4 (export limit) | 15.98, 17.34 |

The mean serial/four-worker ratio was 2.79 on this short fixture. This is not a whole-song speedup guarantee or a comparison with native VoiSona multitrack export. All outputs were finite, nonzero and the expected length; cache rereads matched exactly, including simultaneous identical-state requests. No independent pronunciation/timbre audition was performed. The opt-in `VoiSonaThroughputTest` retains timing JSON under `OPENUTAU_VOISONA_ARTIFACTS` and separately exercises playback, mixdown and stem-export routing.
