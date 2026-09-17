# Writing in 31-TET

Choose **New → 31-TET** from the Start page or **File → New**, use the direct
**File → New 31-TET project** shortcut, or open a `.ustx31` file.
The New dialog also offers **12-TET** and **Cancel**. Ordinary `.ustx`
projects keep their existing keyboard, key control, and pitch editing behavior.

The native keyboard has 31 equal steps per octave. Draw and drag notes by one
step; octave transposition moves 31 steps. Colors identify intervals from the selected
tonic: the tonic is always blue, regardless of its note name. All 31 pitches use one
continuous OKLCH hue circle at equal lightness (0.82) and chroma (0.075), with 360/31
degrees between steps. Major and minor show subsets of that same palette. Printed
scale degrees (1, 2, ♭3, 3, 4, 5, ♭6, 6, H7, ♭7, 7), step offsets for intervening pitches
(+1 means one 31-TET step above the tonic), and a double underline at the tonic provide cues
independent of color. Intervening chromatic step labels use smaller, dark gray type
with at least 5:1 contrast; scale-degree labels and note names remain black. The tonic
degree and note name are bold. Adjacent colors are deliberately gradual; use labels
for exact identity. Black keyboard text has at least 11:1 contrast against every row
color. The roll uses subdued tints in either theme, with a permanent stronger tint and thin
contrasting lower boundary on tonic rows. This remains visible at minimum vertical zoom,
independently of selection and playback. Perfect fourth and fifth rows have fainter
permanent highlights in every scale view, while the tonic remains strongest. When rows are too short for ordinary text,
large bold 1, 4, and 5 labels identify the tonic, perfect fourth, and perfect fifth.
They occupy separate columns so they remain legible even on adjacent folded rows.

The **Tonic** menu beside the Major/Minor checkboxes selects the reference for colors, scale degrees, and note names.
It is an application preference, persists across launches, and defaults to **D** when unset. Changing it
does not mark the project modified. No preferred-key information is saved in the
project. This is a spelling center, not a major/minor key declaration.

The independent **Major** and **Minor** checkboxes at the start of the piano-roll toolbar
collapse empty non-scale rows. Minor means natural minor. Both can be enabled together
for their combined collection, rooted at the preferred spelling key. Both modes also
include **H7**, the harmonic seventh: 25 steps above the tonic, the 31-TET approximation
to 7:4 (about 967.7 cents). It is distinct from the natural minor seventh at 26 steps.
H7 uses the same full-size black interval label as the scale degrees and retains its
color in the full chromatic view. Each mode shows eight pitches per octave; together
they show eleven. Existing pitches anywhere in the project remain visible, including
out-of-scale notes. Rows revealed during editing stay available until the view is
expanded and collapsed again, so deleting a note does not shift the grid beneath
the pointer. Turn both off to restore all 31 steps per octave.
Tab to either checkbox and press Space
to toggle it; after a mouse click, Space continues to control playback. Existing Diatonic
preferences migrate to both checkboxes.

This application preference persists across launches and is available only for
31-TET documents. It does not change notes, tuning, undo history, or project files.
Pitch curves retain their exact pitch values and follow the folded display.

Names come from a chain of 31 consecutive fifths: fifteen below the preferred key
through fifteen above it. A fifth is 18 steps; a sharp raises a letter by two
steps. Flats and sharps are distinct where appropriate. Octave numbers follow the
written letter, including spellings that cross C. Double sharps use 𝄪; repeated flats and combined symbols represent more remote
alterations.

**File → Convert a copy between 12-TET and 31-TET** creates a separate unsaved
document. Conversion to 31-TET rounds note targets to the nearest step; conversion
to USTx approximates targets with integer cents. Save the copy under a new name.
Convert whole projects before importing tracks or pasting between tuning systems.

See [Project formats](project-format.md) for exact pitch reference, conversion
rules, file compatibility, and format versioning.

Legacy UTAU plugins require conversion to an ordinary USTx copy first.

## Local macOS packaging

Build the self-contained Apple Silicon bundle, then complete its standard icon
and file associations (the bundler omits the document-type XML):

```sh
dotnet restore OpenUtau/OpenUtau.csproj -r osx-arm64
dotnet msbuild OpenUtau/OpenUtau.csproj -t:BundleApp -p:Configuration=Release -p:RuntimeIdentifier=osx-arm64 -p:UseAppHost=true -p:SelfContained=true -p:OutputPath=../bin/osx-arm64/
uv run python .github/scripts/finalize-macos-bundle.py bin/osx-arm64/publish/OpenUtau.app
codesign --force --deep --sign - bin/osx-arm64/publish/OpenUtau.app
```

The resulting bundle can be installed in `~/Applications`. Verify the copied
bundle with `codesign --verify --deep --strict` and launch that installed copy.
The optional Liquid Glass icon is not required for the standard icon to work.

## Fork version

The local fork identifies itself as `0.1.570-tet31.24 [31-TET]`, based on the
upstream 0.1.570 development line. The application title uses the informational
version, preserving the fork suffix without the assembly's trailing zero. macOS
uses numeric bundle version `0.1.570.24` and short version `0.1.570`. These build
versions are independent of the native project format revision.

## Image-scaling regression check

Avatar and portrait scaling must keep the owning bitmap alive until the native
operation completes. A temporary bitmap can be finalized during the operation,
releasing the Skia image and crashing the application. `BitmapLoaderTest` forces
collection at the scaling boundary and checks the native handle before calling
Skia, so this failure produces an assertion rather than a process crash.

Run this check with optimized code and tiered compilation disabled to exercise
the shortened object lifetimes that exposed the failure:

```sh
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 dotnet test OpenUtau.Test/OpenUtau.Test.csproj -p:Optimize=true --filter 'FullyQualifiedName~BitmapLoaderTest'
```

## VoiSona Song

The macOS fork can register both installed Chis-A variants as singers and render
through the VoiSona Song Audio Unit without a per-render editor window. Native
31-TET pitches and OpenUtau pitch curves are preserved. See [VoiSona setup and
limitations](voisona.md).

## Note-edit part boundaries

The piano roll keeps its browsing extent separately from the saved voice part.
Drawing, moving, or resizing notes can cross the right edge. The part grows only
when the edit group commits, to the first beat boundary containing the farthest
note end. A drag out and back does not enlarge the part; moving notes earlier or
deleting them does not shrink it. Undo and redo include any automatic growth in
the same step as the notes. Explicit part resizing remains available.

Syllable-based phonemizers, including English X-SAMPA, initialize their dictionary
on the existing phonemizer worker before processing the first notes. Initialization
failures remain visible as phonemizer errors and can retry on the next validation;
they no longer leave a background dictionary load permanently pending.
