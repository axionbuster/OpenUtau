# Audio cache and export

Rendered phrase caches and the built-in classic concatenator's resampler caches use
FLAC. Existing WAV caches migrate when reused, avoiding another synthesis pass;
a completed FLAC replaces its WAV only after encoding succeeds. External
resamplers/wavtools and the native VoiSona host still exchange WAV files where
required by their interfaces. External wavtools retain their intermediate WAVs.

The previous 16-bit PCM cache precision is unchanged; migrating a 16-bit WAV is
sample-exact. VoiSona previously stored stereo float WAV but playback uses their
mono average. It now caches that average as 24-bit PCM FLAC. This introduces at
most half a 24-bit quantization step for samples within [-1, 1]; out-of-range
samples are clipped. VoiSona reads back the cache on the initial render so cold
and warm playback use identical samples. Decoded playback buffers remain floats;
this change reduces disk storage, not the resident sample-buffer size.

Mixdown and “Export Tracks To...” offer WAV and FLAC (16-bit PCM), plus M4A
(AAC at 256 kb/s) on macOS. M4A uses `/usr/bin/afconvert`; no Homebrew tools are
required. Export writes a separate temporary file before replacing a destination.
The quick “Export Wav Files” command remains a WAV shortcut.

FLAC encoding and known-length decoding use libFLAC from the pinned native runtime
NuGet packages (Apple Silicon/Intel macOS, Linux x64/arm64, Windows x64/arm64).
Only macOS arm64 was executed in this change. Imported FLAC files without a total
sample count retain the existing managed scanner. The native decoder avoids a
managed-reader failure discovered with actual caches: it omitted a final partial
frame from a valid 286,650-sample FLAC.

## Local measurement, September 16, 2026

Measured on copies of the 130 VoiSona/Worldline WAVs present in the local cache.
Original files were retained during benchmarking. Each output was decoded and
checked for complete sample count and maximum absolute error.

| Cache | Files | WAV bytes | FLAC bytes | Reduction | Maximum sample error |
| --- | ---: | ---: | ---: | ---: | ---: |
| VoiSona | 83 | 1,785,640,368 | 399,019,092 | 77.7% | 0.000000059604645 |
| Worldline | 47 | 39,516,728 | 23,719,461 | 40.0% | 0 (exact PCM) |

VoiSona savings combine stereo-to-mono storage, float-to-24-bit conversion, and
FLAC compression; they are not attributable to FLAC compression alone.
Single-pass median WAV/FLAC read times were 6.55/9.41 ms for VoiSona and
2.23/5.37 ms for Worldline. Median conversion/write times were 18.41 and 12.87 ms
respectively (Worldline includes copying its source fixture). These are local
warm-filesystem observations with non-alternated ordering, not a cold-start or
end-to-end rendering benchmark. They support a storage improvement, not a speedup.

`FlacCacheBenchmark` is an explicitly skipped manual test for reproducing the
measurement on a supplied cache directory. `AudioExportTest` covers exact PCM,
24-bit error, atomic publication, concurrent writes, stereo seeking, final partial
frames, and WAV/FLAC/M4A containers. Native engine integration tests remain opt-in.

## Delivery verification

Installed `0.1.570-tet31.16` in `~/Applications/OpenUtau.app`; the copied Core DLL
matches the published bundle and deep/strict code-sign verification passed.
The installed app launched with the expected version. A separate harness loaded
its installed Core/NAudio assemblies and bundled codecs and exported a one-second
44.1 kHz stereo fixture to WAV, FLAC, and M4A. WAV/FLAC decoded to all 88,200
interleaved samples; independent ffprobe inspection identified the M4A as stereo
44.1 kHz AAC. This verifies installed encoding, not a completed menu-driven export
or a listening audition. Final focused regression: 32 passed, 4 explicitly skipped
native-engine integration tests; the 130-file benchmark also passed separately.
