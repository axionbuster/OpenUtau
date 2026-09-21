using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;
using SharpCompress;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UTempo {
        public int position;
        public double bpm;

        public UTempo() { }
        public UTempo(int position, double bpm) {
            this.position = position;
            this.bpm = bpm;
        }
        public override string ToString() => $"{bpm}@{position}";
    }

    public class UTimeSignature {
        public int barPosition;
        public int beatPerBar;
        public int beatUnit;

        public UTimeSignature() { }
        public UTimeSignature(int barPosition, int beatPerBar, int beatUnit) {
            this.barPosition = barPosition;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
        }
        public override string ToString() => $"{beatPerBar}/{beatUnit}@bar{barPosition}";
    }

    public class UProject {
        [YamlIgnore] public bool Is31Edo { get; set; }
        [YamlIgnore] public string NativeExtension => Is31Edo ? ".ustx31" : ".ustx";
        [YamlIgnore] public Edo31PitchReference PitchReference31 { get; set; } = Edo31PitchReference.Default;
        public double ToneToFrequency(double tone) => Is31Edo
            ? PitchReference31.ToneToFrequency(tone)
            : MusicMath.ToneToFreq(tone);
        public double FrequencyToTone(double frequency) => Is31Edo
            ? PitchReference31.FrequencyToTone(frequency)
            : MusicMath.FreqToTone(frequency);
        public string name = "New Project";
        public string comment = string.Empty;
        public string outputDir = "Vocal";
        public string cacheDir = "UCache";
        [YamlMember(SerializeAs = typeof(string))]
        public Version ustxVersion;
        [YamlIgnore] public int resolution => 480;

        [Obsolete("Since ustx v0.6")] public double bpm = 120;
        [Obsolete("Since ustx v0.6")] public int beatPerBar = 4;
        [Obsolete("Since ustx v0.6")] public int beatUnit = 4;

        public Dictionary<string, UExpressionDescriptor> expressions = new Dictionary<string, UExpressionDescriptor>();
        public string[] expSelectors = new string[] { Format.Ustx.DYN, Format.Ustx.PITD, Format.Ustx.CLR, Format.Ustx.ENG, Format.Ustx.VEL, Format.Ustx.VOL, Format.Ustx.ATK, Format.Ustx.DEC, Format.Ustx.GEN, Format.Ustx.BRE };
        public int expPrimary = 0;
        public int expSecondary = 1;
        public int key = 0;//Music key of the project, 0 = C, 1 = C#, 2 = D, ..., 11 = B
        // Null only for older files, whose original 12-TET key remains the fallback.
        public List<UKeySignature>? keySignatures;
        public UKeySignature KeyAt(int tick) => keySignatures?.LastOrDefault(change => change.position <= Math.Max(0, tick))
            ?? new UKeySignature { key = Is31Edo ? 2 : Math.Clamp(key, 0, 11) };
        public IEnumerable<UKeySignature> KeyTimeline() => keySignatures ?? new List<UKeySignature> { KeyAt(0) };
        public List<UTimeSignature> timeSignatures;
        public List<UTempo> tempos;
        public List<UTrack> tracks;
        [YamlIgnore] public List<UPart> parts;
        [YamlIgnore] public bool SoloTrackExist { get => tracks.Any(t => t.Solo); }

        /// <summary>
        /// Transient field used for serialization.
        /// </summary>
        public List<UVoicePart>? voiceParts;
        /// <summary>
        /// Transient field used for serialization.
        /// </summary>
        public List<UWavePart>? waveParts;

        [YamlIgnore] public string FilePath { get; set; } = string.Empty;
        [YamlIgnore] public bool Saved { get; set; } = false;
        [YamlIgnore] public int EndTick => parts.Count == 0 ? 0 : parts.Max(p => p.End);

        [YamlIgnore] public readonly TimeAxis timeAxis = new TimeAxis();

        public UProject() {
            timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };
            tempos = new List<UTempo> { new UTempo(0, 120) };
            tracks = new List<UTrack>() {
                UTrack.CreateChordsTrack(),
                new UTrack("Track1") { TrackNo = 1 },
            };
            parts = new List<UPart> { CreateChordPart(0) };
            timeAxis.BuildSegments(this);
        }

        public void RegisterExpression(UExpressionDescriptor descriptor) {
            if (!expressions.ContainsKey(descriptor.abbr)) {
                expressions.Add(descriptor.abbr, descriptor);
            }
        }

        public void MargeExpression(string oldAbbr, string newAbbr) {
            if (parts != null && parts.Count > 0) {
                foreach (UVoicePart p in parts.Where(p => p is UVoicePart).OfType<UVoicePart>()) {
                    foreach (var n in p.notes) {
                        ConvertNoteExp(n, tracks[p.trackNo]);
                    }
                }
            } else if (voiceParts != null && voiceParts.Count > 0) {
                foreach (var p in voiceParts) {
                    foreach (var n in p.notes) {
                        ConvertNoteExp(n, tracks[p.trackNo]);
                    }
                }
            }
            expressions.Remove(oldAbbr);

            void ConvertNoteExp(UNote note, UTrack track) {
                if (note.phonemeExpressions.Any(e => e.abbr == oldAbbr)) {
                    var toRemove = new List<UExpression>();
                    foreach (var oldExp in note.phonemeExpressions.Where(e => e.abbr == oldAbbr)) {
                        if (!note.phonemeExpressions.Any(newExp => newExp.abbr == newAbbr && newExp.index == oldExp.index)) {
                            // When there is only old exp, convert it to new exp
                            oldExp.abbr = newAbbr;
                            if (track.TryGetExpDescriptor(this, newAbbr, out var descriptor)) {
                                oldExp.descriptor = descriptor;
                            }
                        } else {
                            // When both old and new exp exist, remove the old one
                            toRemove.Add(oldExp);
                        }
                    }
                    toRemove.ForEach(exp => note.phonemeExpressions.Remove(exp));
                }
            }
        }

        public UNote CreateNote() {
            UNote note = UNote.Create();
            int start = NotePresets.Default.DefaultPortamento.PortamentoStart;
            int length = NotePresets.Default.DefaultPortamento.PortamentoLength;
            var shape = NotePresets.Default.DefaultPitchShape;
            note.pitch.AddPoint(new PitchPoint(start, 0, shape));
            note.pitch.AddPoint(new PitchPoint(start + length, 0, shape));
            return note;
        }

        public UNote CreateGridNote(int gridTone, int posTick, int durTick) {
            var note = CreateNote(0, posTick, durTick);
            if (Is31Edo) { note.tone31 = gridTone; }
            note.SetGridTone(gridTone);
            return note;
        }

        public UNote CreateNote(int noteNum, int posTick, int durTick) {
            var note = CreateNote();
            note.tone = noteNum;
            note.position = posTick;
            note.duration = durTick;
            return note;
        }

        public void BeforeSave() {
            foreach (var track in tracks) {
                track.BeforeSave();
            }
            foreach (var part in parts) {
                part.BeforeSave(this, tracks[part.trackNo]);
            }
            voiceParts = parts
                .Where(part => part is UVoicePart)
                .OfType<UVoicePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToList();
            waveParts = parts
                .Where(part => part is UWavePart)
                .OfType<UWavePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToList();
        }

        public UProject CloneAsTemplate() {
            var project = new UProject() {
                ustxVersion = ustxVersion,
                Is31Edo = Is31Edo,
                PitchReference31 = PitchReference31.ValidatedCopy(),
            };
            foreach (var kv in expressions) {
                project.expressions.Add(kv.Key, kv.Value.Clone());
            }
            return project;
        }

        public void AfterSave() {
            voiceParts = null;
            waveParts = null;
        }

        public void AfterLoad() {
            foreach (var track in tracks) {
                track.AfterLoad(this);
            }
            if (voiceParts != null) {
                parts.AddRange(voiceParts);
                voiceParts = null;
            }
            if (waveParts != null) {
                parts.AddRange(waveParts);
                waveParts = null;
            }
            EnsureChordsTrack();
            foreach (var part in parts) {
                part.AfterLoad(this, tracks[part.trackNo]);
            }
        }

        static UVoicePart CreateChordPart(int trackNo) => new UVoicePart {
            name = "Chords",
            trackNo = trackNo,
            position = 0,
            duration = 1,
            isChordPart = true,
        };

        [YamlIgnore] public UTrack ChordsTrack => tracks.First(track => track.IsChordsTrack);
        [YamlIgnore] public UVoicePart ChordsPart => parts.OfType<UVoicePart>().First(part => part.IsChordPart);

        public void EnsureChordsTrack() {
            tracks ??= new List<UTrack>();
            parts ??= new List<UPart>();
            var owners = parts.ToDictionary(
                part => part,
                part => part.trackNo >= 0 && part.trackNo < tracks.Count ? tracks[part.trackNo] : null);
            var roleTracks = tracks.Where(track => track.IsChordsTrack).ToList();
            var chordTrack = roleTracks.FirstOrDefault() ?? UTrack.CreateChordsTrack();
            var chordParts = parts.OfType<UVoicePart>()
                .Where(part => part.IsChordPart || (part.trackNo >= 0 && part.trackNo < tracks.Count && tracks[part.trackNo].IsChordsTrack))
                .ToList();
            var destination = chordParts.FirstOrDefault() ?? CreateChordPart(0);
            var recoveryTrack = tracks.FirstOrDefault(track => !track.IsChordsTrack);
            foreach (var malformed in chordParts.Where(part => part.notes.Count > 0 || part.curves.Count > 0)) {
                if (recoveryTrack == null) {
                    recoveryTrack = new UTrack("Recovered vocals");
                    tracks.Add(recoveryTrack);
                }
                var recovered = new UVoicePart {
                    name = malformed.name == "Chords" ? "Recovered vocals" : malformed.name,
                    comment = malformed.comment,
                    trackNo = tracks.IndexOf(recoveryTrack),
                    position = malformed.position,
                    duration = malformed.duration,
                    notes = malformed.notes,
                    curves = malformed.curves,
                };
                malformed.notes = new SortedSet<UNote>();
                malformed.curves = new List<UCurve>();
                parts.Add(recovered);
                owners[recovered] = recoveryTrack;
            }
            var helpers = parts.OfType<UVoicePart>()
                .SelectMany(part => part.chordHelpers.Select((helper, index) => (part, helper, index)))
                .OrderBy(item => item.part.position + item.helper.position)
                .ThenBy(item => item.part.trackNo)
                .ThenBy(item => item.index)
                .ToList();
            destination.chordHelpers.Clear();
            foreach (var item in helpers) {
                item.helper.position += item.part.position;
                destination.chordHelpers.Add(item.helper);
                if (!ReferenceEquals(item.part, destination)) {
                    item.part.chordHelpers.Remove(item.helper);
                }
            }
            var regions = parts.OfType<UVoicePart>()
                .SelectMany(part => part.chordRegions.Select((region, index) => (part, region, index)))
                .OrderBy(item => item.part.position + item.region.position)
                .ThenBy(item => item.part.trackNo)
                .ThenBy(item => item.index)
                .ToList();
            destination.chordRegions.Clear();
            foreach (var item in regions) {
                item.region.position += item.part.position;
                destination.chordRegions.Add(item.region);
                if (!ReferenceEquals(item.part, destination)) {
                    item.part.chordRegions.Remove(item.region);
                }
            }
            foreach (var extra in chordParts.Where(part => !ReferenceEquals(part, destination))) {
                parts.Remove(extra);
            }
            foreach (var extra in roleTracks.Where(track => !ReferenceEquals(track, chordTrack))) {
                int removed = tracks.IndexOf(extra);
                tracks.RemoveAt(removed);
                foreach (var part in parts.Where(part => part.trackNo > removed)) part.trackNo--;
            }
            tracks.Remove(chordTrack);
            tracks.Insert(0, chordTrack);
            for (int i = 0; i < tracks.Count; i++) tracks[i].TrackNo = i;
            foreach (var part in parts.Where(part => !ReferenceEquals(part, destination))) {
                if (owners.TryGetValue(part, out var owner) && owner != null && tracks.Contains(owner)) {
                    part.trackNo = tracks.IndexOf(owner);
                }
            }
            destination.isChordPart = true;
            chordTrack.TrackName = "Chords";
            if (chordTrack.TrackColor == "Ivory") chordTrack.TrackColor = "Automatic (theme)";
            destination.name = "Chords";
            destination.position = 0;
            destination.trackNo = 0;
            destination.notes.Clear();
            destination.curves.Clear();
            destination.duration = Math.Max(
                destination.chordHelpers.Select(helper => helper.End).DefaultIfEmpty(1).Max(),
                destination.chordRegions.Select(region => region.End).DefaultIfEmpty(1).Max());
            if (!parts.Contains(destination)) parts.Add(destination);
        }

        public void Validate(ValidateOptions options) {
            if (!options.SkipTiming) {
                timeSignatures.Sort((lhs, rhs) => lhs.barPosition.CompareTo(rhs.barPosition));
                tempos.Sort((lhs, rhs) => lhs.position.CompareTo(rhs.position));
                timeAxis.BuildSegments(this);
            }
            if (options.Part == null) {
                foreach (var track in tracks) {
                    track.Validate(options, this);
                }
            }
            foreach (var part in parts) {
                if (options.Part == null || options.Part == part) {
                    part.Validate(options, this, tracks[part.trackNo]);
                }
            }
        }

        public void ValidateFull() {
            Validate(new ValidateOptions());
        }
    }
}
