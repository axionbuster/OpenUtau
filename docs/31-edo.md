# Writing in 31-EDO

Choose **File → New 31-EDO project**, or open a `.ustx31` file. Ordinary `.ustx`
projects keep their existing keyboard, key control, and pitch editing behavior.

The native keyboard has 31 equal steps per octave. Draw and drag notes by one
step; octave transposition moves 31 steps. The five fixed keyboard colors identify
double flats, flats, naturals, sharps, and double sharps in the D-centered reference
spelling. Changing displayed names does not change these colors or sounding pitches.

The **Spelling** menu selects the preferred key for note names. It is an application
preference, persists across launches, and defaults to **D** when unset. Changing it
does not mark the project modified. No preferred-key information is saved in the
project. This is a spelling center, not a major/minor key declaration.

Names come from a chain of 31 consecutive fifths: fifteen below the preferred key
through fifteen above it. A fifth is 18 steps; a sharp raises a letter by two
steps. Flats and sharps are distinct where appropriate. Octave numbers follow the
written letter, including spellings that cross C. Double sharps use 𝄪; repeated flats and combined symbols represent more remote
alterations.

**File → Convert a copy between 12-EDO and 31-EDO** creates a separate unsaved
document. Conversion to 31-EDO rounds note targets to the nearest step; conversion
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
