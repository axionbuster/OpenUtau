# Project formats

The fork supports ordinary `.ustx` documents and native `.ustx31` documents.
The native format is revision 2. It uses fixed 31-TET steps with a project-level
absolute pitch reference.

## Ordinary USTx

Ordinary documents retain the upstream schema and semitone editing grid, including
existing per-note cent offsets. Saving does not add fork identity, features,
temperament steps, or preferred-key fields. Opening a document never converts it.

## Native header

```yaml
format: axion.openutau
format_revision: 2
base_schema:
  format: ustx
  version: '0.9'
features:
- edo31-v1
- pitch-reference-v1
project:
  pitch_reference:
    mode: a4-frequency
    frequency: 440.0
  # Existing project structure, with the differences below.
```

`format` identifies the dialect. `format_revision` versions this dialect
independently of upstream. `base_schema` identifies the underlying upstream
structure. Every entry in `features` is required for safe editing; revision 2
accepts exactly `edo31-v1` and `pitch-reference-v1`. Unsupported identities,
revisions, base schemas, features, and invalid note steps or pitch references are
rejected before project initialization. Revision 1 files remain readable and
retain their historical C-anchored tuning when loaded.

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

Preferred key and display spellings remain separate from the sounding reference
and are not serialized in either format. The current spelling is derived from
exact steps and the application preference.
Project-level keys and key changes for native documents are deferred.

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
and verifies that switching back to USTx restores the ordinary editor. To also save keyboard PNGs, set
`OPENUTAU_QA_OUTPUT` to an output directory when running that test. Real voicebank
synthesis and listening are separate verification steps.

```sh
dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter 'FullyQualifiedName~Edo31'
```
