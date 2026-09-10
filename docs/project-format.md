# Project formats

The fork supports ordinary `.ustx` documents and native `.ustx31` documents.
The native format is revision 1. Its initial feature is fixed 31-TET.

## Ordinary USTx

Ordinary documents retain the upstream schema and semitone editing grid, including
existing per-note cent offsets. Saving does not add fork identity, features,
temperament steps, or preferred-key fields. Opening a document never converts it.

## Native header

```yaml
format: axion.openutau
format_revision: 1
base_schema:
  format: ustx
  version: '0.9'
features:
- edo31-v1
project:
  # Existing project structure, with the differences below.
```

`format` identifies the dialect. `format_revision` versions this dialect
independently of upstream. `base_schema` identifies the underlying upstream
structure. Every entry in `features` is required for safe editing; revision 1
accepts exactly `edo31-v1`. Unsupported identities, revisions, base schemas,
features, and invalid note steps are rejected before project initialization.

The native payload does not contain `ustx_version` or `key`. The distinct header
avoids advertising the file as ordinary USTx to the upstream content detector.
The extension is a user-facing distinction, not the sole format discriminator.

## Note pitch

Native notes store `tone31`, an integer number of 31-TET steps above C-1.
The editor covers eleven octaves: steps 0 through 340. C4 is step 155.
The target MIDI-coordinate pitch is `tone31 * 12 / 31 + tuning / 100`.
Frequency is `440 * 2^((pitch - 69) / 12)`. This anchors C-1 to the ordinary
MIDI reference; the 31-TET A derived from it is not exactly 440 Hz.

`tone` remains the nearest integer semitone used for singer/sample selection;
it is derived from `tone31` in native documents. `tuning` remains an additional
integer-cent offset. Pitch curves and vibrato retain their existing units.
Ordinary notes omit `tone31`. Mixed note representations in one project are invalid.

Preferred key and display spellings are not serialized in either format. The
current spelling is derived from exact steps and the application preference.
Project-level keys and key changes for native documents are deferred.

## Save, recovery, and conversion

Save, Save As, autosave, crash backup, and templates retain document mode.
Recovery uses that mode's extension to recover the original path. Changing a
filename extension is not conversion. Track import and clipboard paste reject
mixed tuning modes before changing the destination; convert a copy first.

Conversion creates an independent, unsaved document with no destination path:

- To 31-TET: round each sounding note target to the nearest 31-TET step and clear
  its per-note cent offset. Relative pitch curves and vibrato remain unchanged.
- To USTx: derive the nearest semitone and integer-cent offset. Per-note target
  rounding is at most half a cent. Exact temperament identity is lost.

The source document is not rewritten. Relative pitch expression is preserved,
but conversion does not promise identical renderer output or vocal articulation.

## Verification

`Edo31Test` covers spelling across the supported key window and pitch range,
native and ordinary serialization, unsupported-file rejection, detection and
loading, conversion, undo, cloning, and mixed-mode import rejection.
`UstxYamlTest` retains the upstream note serialization expectations.

The headless `Edo31EditorTest` renders the real piano-roll control, checks row
coordinates and spelling preference behavior, and verifies that switching back
to USTx restores the ordinary editor. To also save keyboard PNGs, set
`OPENUTAU_QA_OUTPUT` to an output directory when running that test. Real voicebank
synthesis and listening are separate verification steps.

```sh
dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter 'FullyQualifiedName~Edo31'
```
