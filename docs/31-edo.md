# Writing in 31-TET

Choose **New → 31-TET** from the Start page or **File → New**, use the direct
**File → New 31-TET project** shortcut, or open a `.ustx31` file.
The New dialog also offers **12-TET** and **Cancel**. Ordinary `.ustx`
projects keep their existing keyboard, key control, and pitch editing behavior.

New 31-TET projects use a native A4 = 440 Hz reference: A4 is step 178, and every
adjacent step differs by the exact ratio `2^(1/31)`. Use **Project → 31-TET
reference pitch…** to enter another absolute A4 frequency or synchronize the nearest
31-TET step with a chosen 12-TET pitch class under standard A4 = 440 Hz. The choice
is saved in the project and participates in undo and redo. It changes sounding pitch,
not note spelling. Older revision-1 `.ustx31` files retain their historical C anchor.

The native keyboard has 31 equal steps per octave. Draw and drag notes by one
step; octave transposition moves 31 steps. Colors identify intervals from the selected
tonic: the tonic is always blue, regardless of its note name. All 31 pitches use one
continuous OKLCH hue circle at equal lightness (0.82) and chroma (0.075), with 360/31
degrees between steps. Major and minor show subsets of that same palette. Printed
septimal-meantone scale degrees (1, 2, ♭3, 3, 4, 5, ♭6, 6, ♭7, 7
for the combined Major/Minor collection), and a double underline at the tonic provide cues
independent of color. Chromatic scale-degree labels use smaller, dark gray type
with at least 5:1 contrast; scale-degree labels and note names remain black. The tonic
interval and note name are bold. Adjacent colors are deliberately gradual; use labels
for exact identity. Hover a keyboard row to see its distance from the tonic in 31-TET steps.
Black keyboard text has at least 11:1 contrast against every row
color. The roll uses subdued tints in either theme, with a permanent stronger tint and thin
contrasting lower boundary on tonic rows. This remains visible at minimum vertical zoom,
independently of selection and playback. Perfect fourth and fifth rows have fainter
permanent highlights in every scale view, while the tonic remains strongest. When rows are too short for ordinary text,
large bold 1, 4, and 5 labels identify the tonic, perfect fourth, and perfect fifth.
They occupy separate columns so they remain legible even on adjacent folded rows.
Drag the right edge of the piano keyboard to resize its full column from 72 to 320 pixels.
The chosen width persists across launches and applies to both 31-TET and 12-TET projects.

The **Key: D Major + Minor** button opens a panel holding a **Tonic** picker and a
single **Mode** picker, which replace the independent checkboxes. Modes are **Major**,
**Minor**, **Major + minor**, and **Chromatic**. Changes apply immediately to the
section named below the pickers, are saved with the project, and can be undone.

To modulate, position the playhead, open the panel, and choose **New section at
bar _n_, beat _n_** before choosing its tonic and mode. That button names the playhead
position and is disabled where a section already starts. **Remove this section**
extends the preceding section; the initial entry cannot be removed.
The keyboard follows the playhead. Background regions show their own key/mode labels,
colors, and scale emphasis. Selected-note and chord names use the key at their positions.
These changes affect notation, never sounding pitch.

The selected modes collapse empty non-scale rows. Major and natural minor each show
seven pitches per octave; together they show ten. The 25-step approximation to 7:4
(about 967.7 cents) is spelled **♯6**, the septimal-meantone augmented sixth, rather than
as a harmonic or minor seventh. It is not part of the major or natural-minor collections,
so folded views hide it unless an existing note reveals that row. The adjacent 24-, 25-,
and 26-step intervals are labeled **♭♭7**, **♯6**, and **♭7** respectively. All other rows
likewise use fifth-based septimal-meantone scale degrees with Arabic numerals and accidentals;
raw 31-TET step counts appear on hover. While either scale view is active, note names outside the selected scale
collection are italicized to make them less prominent. The folded grid includes the union
of scales used throughout the project, keeping note positions stable when playback crosses
a key change. A chromatic section expands the whole grid. Existing pitches anywhere in the project remain visible, including
out-of-scale notes. Rows revealed during editing stay available until the view is
expanded and collapsed again, so deleting a note does not shift the grid beneath
the pointer. Choose Chromatic to restore all 31 steps per octave.
The pickers support keyboard navigation. Closing the panel returns focus to the
piano roll so Space controls playback.

Key and mode are also available in 12-TET documents; their grid remains unfolded.
Pitch curves retain their exact pitch values and follow the folded display.
Older native projects default to D with both scales enabled because their previous
app-specific choices were never saved. See the format documentation for migration.

Drawing a note or pressing a piano-roll key previews its exact grid frequency with
a band-limited harmonic tone rather than a pure sine. The preview includes the
fundamental and up to four successively quieter harmonics; partials at or above
Nyquist are omitted so high notes do not acquire aliased pitches.

Names come from a chain of 31 consecutive fifths: fifteen below the preferred key
through fifteen above it. A fifth is 18 steps; a sharp raises a letter by two
steps. Flats and sharps are distinct where appropriate. Octave numbers follow the
written letter, including spellings that cross C. Double and more remote accidentals
use repeated ordinary UI glyphs (for example, a double flat is **♭♭** and five
flats are **♭♭♭♭♭**). Piano-roll labels use slight negative tracking for compactness.

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

The local fork identifies itself as `0.1.570-tet31.35 [31-TET]`, based on the
upstream 0.1.570 development line. The application title uses the informational
version, preserving the fork suffix without the assembly's trailing zero. macOS
uses numeric bundle version `0.1.570.35` and short version `0.1.570`. These build
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
