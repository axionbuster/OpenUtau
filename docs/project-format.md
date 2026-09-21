# Project formats

The fork supports ordinary `.ustx` documents and native `.ustx31` documents.
The native format is revision 3. It uses fixed 31-TET steps with a project-level
absolute pitch reference and a timeline of key/mode changes.

## Ordinary USTx

Ordinary documents retain the semitone editing grid and per-note cent offsets.
This fork adds `key_signatures` to ordinary documents; the legacy `key` field mirrors
the initial tonic. Saving does not add fork identity, feature headers, or temperament
steps. Opening a document never converts its tuning. Upstream builds may ignore or
reject the added timeline; use this fork to preserve key/mode changes.

## Native header

```yaml
format: axion.openutau
format_revision: 3
base_schema:
  format: ustx
  version: '0.9'
features:
- edo31-v1
- pitch-reference-v1
- key-signatures-v1
project:
  pitch_reference:
    mode: a4-frequency
    frequency: 440.0
  # Existing project structure, with the differences below.
```

`format` identifies the dialect. `format_revision` versions this dialect
independently of upstream. `base_schema` identifies the underlying upstream
structure. Every entry in `features` is required for safe editing; revision 3
accepts exactly `edo31-v1`, `pitch-reference-v1`, and `key-signatures-v1`. Unsupported identities,
revisions, base schemas, features, and invalid note steps or pitch references are
rejected before project initialization. Revision 1 files remain readable and
retain their historical C-anchored tuning when loaded. Revision 2 remains readable
and retains its stored pitch reference. Earlier builds reject revision 3 rather than
silently discarding its key timeline.

The native payload does not contain `ustx_version` or `key`. The distinct header
avoids advertising the file as ordinary USTx to the upstream content detector.
The extension is a user-facing distinction, not the sole format discriminator.

## Note pitch

Native notes store `tone31`, an integer number of 31-TET steps above C-1.
The editor covers eleven octaves: steps 0 through 340. C4 is step 155.
The resulting native pitch coordinate is `tone31 * 12 / 31 + tuning / 100`;
it uses semitones as a convenient logarithmic unit, but is not an absolute MIDI
note number. With the default reference, step 178 is A4 and frequency is
`a4_frequency * 2^((native_pitch - 178 * 12 / 31) / 12)`, so A4 is exactly
440 Hz while every adjacent 31-TET step retains the ratio `2^(1/31)`.

The alternate reference mode synchronizes one 31-TET pitch class with a selected
12-TET pitch class under standard A4 = 440 Hz. Its nearest 31-TET step is used as
the native anchor; for example, selecting C makes native C4 exactly the 12-TET C4
frequency. The project stores either the absolute A4 frequency or the synchronized
12-TET pitch class:

```yaml
pitch_reference:
  mode: 12-tet-note
  pitch_class: 0 # C
```

Render phrases carry this native reference. Renderers that accept frequency receive
frequency calculated directly from it. Only adapters for external interfaces that
require 12-TET MIDI-coordinate values translate at that interface boundary.

`tone` remains the nearest integer semitone used for singer/sample selection;
it is derived from `tone31` in native documents. `tuning` remains an additional
integer-cent offset. Pitch curves and vibrato retain their existing units.
Ordinary notes omit `tone31`. Mixed note representations in one project are invalid.

## Key and mode timeline

Both formats store `key_signatures`, ordered by absolute project tick:

```yaml
key_signatures:
- position: 0
  key: 0
  major: true
  minor: false
- position: 1920
  key: 2
  major: false
  minor: true
```

In `.ustx31`, `key` is the fifths index (-15 through 15); in `.ustx`, it is the
12-TET pitch class (0 through 11). The example is C major followed by D natural minor
in either system. Both flags enable the major/minor union; neither means chromatic.
The first entry must be at zero; later positions must be strictly increasing.
Missing, duplicate, unordered, negative, or out-of-range entries are rejected.
A key change applies at its exact tick, through the next change or the project end.

Older ordinary files use their saved `key`; older native files use D with both
Major and Minor enabled, the previous factory defaults. Historical app preferences
were not in those files and cannot be recovered from them. Loading does not consult
current app preferences, so the same file has the same interpretation on every machine.
The next save records the fallback explicitly. New documents use the same defaults
(C for 12-TET, D for 31-TET; both scales enabled).

Key/mode commands participate in dirty state and undo/redo. They have no audio pipeline
impact and do not transpose notes or change the pitch reference. Conversion maps each
tonic to the destination system while preserving positions and mode flags. Moving or
splitting a part leaves the project-wide key timeline in place; importing parts uses
the destination project's timeline.

## Save, recovery, and conversion

Save, Save As, autosave, crash backup, and templates retain document mode.
Recovery uses that mode's extension to recover the original path. Changing a
filename extension is not conversion. Track import and clipboard paste reject
mixed tuning modes or unequal 31-TET references before changing the destination;
convert a copy first.

Conversion creates an independent, unsaved document with no destination path:

- To 31-TET: use the destination's default A4 = 440 Hz reference, round each
  sounding note target to its nearest 31-TET step, and clear its per-note cent
  offset. Relative pitch curves and vibrato remain unchanged.
- To USTx: derive the nearest semitone and integer-cent offset. Per-note target
  rounding is at most half a cent. Exact temperament identity is lost.

Both conversions calculate through the source and destination pitch references,
so the note's absolute frequency is the conversion target. The source document is
not rewritten. Relative pitch expression is preserved, but conversion does not
promise identical renderer output or vocal articulation.

## Verification

`Edo31Test` covers spelling across the supported key window and pitch range,
native and ordinary serialization, reference validation and revision-1 migration,
detection and loading, frequency-preserving conversion, undo, cloning, and
mixed-mode or unequal-reference import rejection.
`UstxYamlTest` retains the upstream note serialization expectations.

The headless `Edo31EditorTest` renders the real piano-roll control, checks row
coordinates and spelling preference behavior, verifies the pitch-reference dialog,
and verifies that switching back to USTx restores the ordinary editor. Its Skia
test host reuses the production font-manager settings, including the macOS
Helvetica Neue and Apple Symbols fallbacks used by scale-degree accidentals. Each
capture is rendered and size-checked even when no output directory is requested.
Set `OPENUTAU_QA_OUTPUT` to an output directory to also save the keyboard PNGs.
This path uses Avalonia's headless platform rather than a native macOS window, so
it remains the appropriate renderer when the interactive session is unavailable
or locked. The automated test verifies that window-independent code path; an actual
lock-screen run remains an environment-level check. Real voicebank synthesis and
listening are separate verification steps.

```sh
dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter 'FullyQualifiedName~Edo31'
```
