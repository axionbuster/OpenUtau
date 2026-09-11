# Writing in 31-TET

Choose **New → 31-TET** from the Start page or **File → New**, use the direct
**File → New 31-TET project** shortcut, or open a `.ustx31` file.
The New dialog also offers **12-TET** and **Cancel**. Ordinary `.ustx`
projects keep their existing keyboard, key control, and pitch editing behavior.

The native keyboard has 31 equal steps per octave. Draw and drag notes by one
step; octave transposition moves 31 steps. The five fixed keyboard colors identify
double flats, flats, naturals, sharps, and double sharps in the D-centered reference
spelling. Changing displayed names does not change these colors or sounding pitches.

The **Spelling** menu selects the preferred key for note names. It is an application
preference, persists across launches, and defaults to **D** when unset. Changing it
does not mark the project modified. No preferred-key information is saved in the
project. This is a spelling center, not a major/minor key declaration.

The **Diatonic** checkbox at the start of the piano-roll toolbar collapses empty
non-scale rows. It uses the combined major and natural minor collections rooted at the same
preferred spelling key (ten pitches per octave). Existing pitches anywhere in the project remain visible, including
out-of-scale notes. Rows revealed during editing stay available until the view is
expanded and collapsed again, so deleting a note does not shift the grid beneath
the pointer. Uncheck it to restore all 31 steps per octave.

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

The local fork identifies itself as `0.1.570-tet31.8 [31-TET]`, based on the
upstream 0.1.570 development line. The application title uses the informational
version, preserving the fork suffix without the assembly's trailing zero. macOS
uses numeric bundle version `0.1.570.6` and short version `0.1.570`. These build
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
