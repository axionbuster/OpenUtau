using System;
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
            return Write(new YamlMappingNode {
                { "format", FormatId },
                { "format_revision", "1" },
                { "base_schema", new YamlMappingNode { { "format", "ustx" }, { "version", Ustx.kUstxVersion.ToString() } } },
                { "features", new YamlSequenceNode(new YamlScalarNode(Feature)) },
                { "project", payload },
            });
        }
        public static UProject Deserialize(string text) {
            var root = Parse(text);
            bool native = root.Children.ContainsKey(new YamlScalarNode("format"));
            if (native) {
                if (Scalar(root, "format") != FormatId || Scalar(root, "format_revision") != "1") {
                    throw new FileFormatException("Unsupported project format or revision.");
                }
                if (!root.Children.TryGetValue(new YamlScalarNode("features"), out var featuresNode) ||
                    featuresNode is not YamlSequenceNode features || features.Children.Count != 1 ||
                    features.Children[0] is not YamlScalarNode feature || feature.Value != Feature) {
                    throw new FileFormatException("Unsupported or missing project features.");
                }
                if (!root.Children.TryGetValue(new YamlScalarNode("base_schema"), out var schemaNode) ||
                    schemaNode is not YamlMappingNode schema || Scalar(schema, "format") != "ustx" ||
                    Scalar(schema, "version") != Ustx.kUstxVersion.ToString()) {
                    throw new FileFormatException("Unsupported base project schema.");
                }
                if (!root.Children.TryGetValue(new YamlScalarNode("project"), out var payload) || payload is not YamlMappingNode projectMap) {
                    throw new FileFormatException("Missing project payload.");
                }
                projectMap.Add("ustx_version", Ustx.kUstxVersion.ToString());
                text = Write(projectMap);
            }
            var result = Yaml.DefaultDeserializer.Deserialize<UProject>(text);
            if (result.ustxVersion == null || result.ustxVersion > Ustx.kUstxVersion) {
                throw new FileFormatException("Missing or unsupported USTx version.");
            }
            result.Is31Edo = native;
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
                double target = note.AdjustedTone;
                if (to31) {
                    note.tone31 = Edo31.NearestStep(target);
                    note.SetGridTone(note.tone31.Value);
                    note.tuning = 0;
                } else {
                    note.tone31 = null;
                    note.tone = (int)Math.Round(target);
                    note.tuning = (int)Math.Round((target - note.tone) * 100);
                }
            }
            copy.Is31Edo = to31;
            copy.FilePath = string.Empty;
            copy.Saved = false;
            copy.AfterLoad();
            return copy;
        }
    }
}
