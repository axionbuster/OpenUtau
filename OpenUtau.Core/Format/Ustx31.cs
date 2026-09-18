using System;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using YamlDotNet.RepresentationModel;

namespace OpenUtau.Core.Format {
    /// <summary>Native envelope with independent identity and schema revision.</summary>
    public static class Ustx31 {
        public const string FormatId = "axion.openutau";
        public const string Feature = "edo31-v1";
        public const string PitchReferenceFeature = "pitch-reference-v1";
        public const string CurrentRevision = "2";
        static YamlMappingNode Parse(string text) {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode map) {
                throw new FileFormatException("Expected one project mapping.");
            }
            return map;
        }
        static string Write(YamlMappingNode map) {
            var writer = new StringWriter();
            new YamlStream(new YamlDocument(map)).Save(writer, false);
            return writer.ToString();
        }
        static string Scalar(YamlMappingNode map, string key) =>
            map.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
                ? scalar.Value ?? "" : throw new FileFormatException($"Missing {key}.");

        static void CheckFeatures(YamlMappingNode root, params string[] expected) {
            if (!root.Children.TryGetValue(new YamlScalarNode("features"), out var featuresNode) ||
                featuresNode is not YamlSequenceNode features || features.Children.Count != expected.Length) {
                throw new FileFormatException("Unsupported or missing project features.");
            }
            for (int i = 0; i < expected.Length; i++) {
                if (features.Children[i] is not YamlScalarNode feature || feature.Value != expected[i]) {
                    throw new FileFormatException("Unsupported or missing project features.");
                }
            }
        }

        static YamlMappingNode SerializePitchReference(Edo31PitchReference reference) {
            reference = reference.ValidatedCopy();
            var map = new YamlMappingNode();
            if (reference.Mode == Edo31PitchReferenceMode.A4Frequency) {
                map.Add("mode", "a4-frequency");
                map.Add("frequency", reference.A4Frequency.ToString("R", CultureInfo.InvariantCulture));
            } else {
                map.Add("mode", "12-tet-note");
                map.Add("pitch_class", reference.TwelveTetPitchClass.ToString(CultureInfo.InvariantCulture));
            }
            return map;
        }

        static Edo31PitchReference DeserializePitchReference(YamlMappingNode project) {
            if (!project.Children.TryGetValue(new YamlScalarNode("pitch_reference"), out var node) ||
                node is not YamlMappingNode map) {
                throw new FileFormatException("Missing 31-TET pitch reference.");
            }
            var mode = Scalar(map, "mode");
            Edo31PitchReference reference;
            if (mode == "a4-frequency") {
                if (!double.TryParse(Scalar(map, "frequency"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var frequency)) {
                    throw new FileFormatException("Invalid A4 reference frequency.");
                }
                reference = new Edo31PitchReference {
                    Mode = Edo31PitchReferenceMode.A4Frequency,
                    A4Frequency = frequency,
                };
            } else if (mode == "12-tet-note") {
                if (!int.TryParse(Scalar(map, "pitch_class"), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var pitchClass)) {
                    throw new FileFormatException("Invalid 12-TET reference note.");
                }
                reference = new Edo31PitchReference {
                    Mode = Edo31PitchReferenceMode.TwelveTetNote,
                    TwelveTetPitchClass = pitchClass,
                };
            } else {
                throw new FileFormatException("Unsupported 31-TET pitch reference mode.");
            }
            try {
                return reference.ValidatedCopy();
            } catch (ArgumentOutOfRangeException e) {
                throw new FileFormatException("Invalid 31-TET pitch reference.", e);
            }
        }

        public static void CheckSavePath(string path, UProject project) {
            if (!string.Equals(Path.GetExtension(path), project.NativeExtension, StringComparison.OrdinalIgnoreCase)) {
                throw new FileFormatException($"Save this document as {project.NativeExtension}; use conversion to save another format.");
            }
        }
        public static string Serialize(UProject project) {
            var notes = (project.voiceParts ?? project.parts.OfType<UVoicePart>().ToList()).SelectMany(p => p.notes);
            if (notes.Any(n => n.tone31.HasValue != project.Is31Edo || n.tone31 < 0 || n.tone31 >= Edo31.MaxStep)) {
                throw new FileFormatException("Note tuning does not match the project format.");
            }
            string yaml = Yaml.DefaultSerializer.Serialize(project);
            if (!project.Is31Edo) { return yaml; }
            var payload = Parse(yaml);
            payload.Children.Remove(new YamlScalarNode("ustx_version"));
            payload.Children.Remove(new YamlScalarNode("key"));
            payload.Add("pitch_reference", SerializePitchReference(project.PitchReference31));
            return Write(new YamlMappingNode {
                { "format", FormatId },
                { "format_revision", CurrentRevision },
                { "base_schema", new YamlMappingNode { { "format", "ustx" }, { "version", Ustx.kUstxVersion.ToString() } } },
                { "features", new YamlSequenceNode(new YamlScalarNode(Feature), new YamlScalarNode(PitchReferenceFeature)) },
                { "project", payload },
            });
        }
        public static UProject Deserialize(string text) {
            var root = Parse(text);
            bool native = root.Children.ContainsKey(new YamlScalarNode("format"));
            var pitchReference = Edo31PitchReference.Default;
            if (native) {
                if (Scalar(root, "format") != FormatId) {
                    throw new FileFormatException("Unsupported project format or revision.");
                }
                string revision = Scalar(root, "format_revision");
                if (!root.Children.TryGetValue(new YamlScalarNode("base_schema"), out var schemaNode) ||
                    schemaNode is not YamlMappingNode schema || Scalar(schema, "format") != "ustx" ||
                    Scalar(schema, "version") != Ustx.kUstxVersion.ToString()) {
                    throw new FileFormatException("Unsupported base project schema.");
                }
                if (!root.Children.TryGetValue(new YamlScalarNode("project"), out var payload) || payload is not YamlMappingNode projectMap) {
                    throw new FileFormatException("Missing project payload.");
                }
                if (revision == "1") {
                    CheckFeatures(root, Feature);
                    // Revision 1 fixed C to the ordinary MIDI reference.
                    pitchReference = Edo31PitchReference.LegacyC;
                } else if (revision == CurrentRevision) {
                    CheckFeatures(root, Feature, PitchReferenceFeature);
                    pitchReference = DeserializePitchReference(projectMap);
                    projectMap.Children.Remove(new YamlScalarNode("pitch_reference"));
                } else {
                    throw new FileFormatException("Unsupported project format or revision.");
                }
                projectMap.Add("ustx_version", Ustx.kUstxVersion.ToString());
                text = Write(projectMap);
            }
            var result = Yaml.DefaultDeserializer.Deserialize<UProject>(text);
            if (result.ustxVersion == null || result.ustxVersion > Ustx.kUstxVersion) {
                throw new FileFormatException("Missing or unsupported USTx version.");
            }
            result.Is31Edo = native;
            result.PitchReference31 = pitchReference;
            foreach (var note in (result.voiceParts ?? new()).SelectMany(p => p.notes)) {
                if (note.tone31.HasValue != native || (native && (note.tone31 < 0 || note.tone31 >= Edo31.MaxStep))) {
                    throw new FileFormatException("Invalid note tuning for this project format.");
                }
                if (native) { note.SetGridTone(note.tone31!.Value); }
            }
            return result;
        }
        /// <summary>Make an independent conversion; leave the source and its path untouched.</summary>
        public static UProject ConvertCopy(UProject source, bool to31) {
            var previousVersion = source.ustxVersion;
            source.ustxVersion = Ustx.kUstxVersion;
            source.BeforeSave();
            UProject copy;
            try { copy = Deserialize(Serialize(source)); }
            finally { source.AfterSave(); source.ustxVersion = previousVersion; }
            foreach (var note in (copy.voiceParts ?? new()).SelectMany(p => p.notes)) {
                double frequency = copy.ToneToFrequency(note.PreciseAdjustedTone);
                if (to31) {
                    var reference = Edo31PitchReference.Default;
                    double target = reference.FrequencyToTone(frequency);
                    note.tone31 = Edo31.NearestStep(target);
                    note.SetGridTone(note.tone31.Value);
                    note.tuning = 0;
                } else {
                    double target = MusicMath.FreqToTone(frequency);
                    note.tone31 = null;
                    note.tone = (int)Math.Round(target);
                    note.tuning = (int)Math.Round((target - note.tone) * 100);
                }
            }
            copy.Is31Edo = to31;
            copy.PitchReference31 = Edo31PitchReference.Default;
            copy.FilePath = string.Empty;
            copy.Saved = false;
            copy.AfterLoad();
            return copy;
        }
    }
}
