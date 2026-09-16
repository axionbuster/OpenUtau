using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.VoiSona {
    public sealed class VoiSonaSinger : USinger {
        public string VoiceFileName { get; }
        public string Language { get; }
        public int NoteLanguage => Language == "ja_JP" ? 1 : 2;
        public override string Id => "voisona:" + Path.GetFileNameWithoutExtension(VoiceFileName);
        public override string Name => Language == "ja_JP" ? "Chis-A (VoiSona Japanese)" : "Chis-A (VoiSona English / Cross-Lingual)";
        public override USingerType SingerType => USingerType.VoiSona;
        public override string Location { get; }
        public override string BasePath => Location;
        public override string Version { get; }
        public override string Author => "Techno-Speech";
        public override string Web => "https://voisona.com/";
        public override string OtherInfo => "VoiSona Song Audio Unit. Use Tools → VoiSona → Sign in / Manage voices for setup. Lyrics use " + (NoteLanguage == 1 ? "Japanese." : "English.");
        public override string DefaultPhonemizer => "OpenUtau.Core.DefaultPhonemizer";
        public override IList<USubbank> Subbanks { get; } = new List<USubbank>();
        public override IList<string> Errors { get; } = new List<string>();
        public VoiSonaSinger(string language, string version, string location) {
            Language = language; Version = version; Location = location;
            VoiceFileName = $"nitech-jp_{language}_f008_svss.tsnvoice";
            found = loaded = true;
        }
        public override bool TryGetOto(string phoneme, out UOto oto) {
            oto = UOto.OfDummy(phoneme); return true;
        }
    }

    public static class VoiSonaSingerLoader {
        public static string RegistrationPath => Path.Combine(PathManager.Inst.SingersInstallPath, "VoiSona", "provider.json");
        public static string VoiceRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Techno-Speech", "VoiSona", "voices", "Singer");
        public static string? PluginPath => new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Audio/Plug-Ins/Components/VoiSona Song.component"),
            "/Library/Audio/Plug-Ins/Components/VoiSona Song.component",
        }.FirstOrDefault(Directory.Exists);
        public static IEnumerable<USinger> FindAllSingers() => OperatingSystem.IsMacOS() && File.Exists(RegistrationPath)
            ? Discover(VoiceRoot) : Enumerable.Empty<USinger>();
        public static IReadOnlyList<VoiSonaSinger> Discover(string root) {
            var result = new List<VoiSonaSinger>();
            foreach (string language in new[] { "ja_JP", "en_US" }) {
                string voice = $"nitech-jp_{language}_f008_svss";
                string directory = Path.Combine(root, voice);
                if (!Directory.Exists(directory)) continue;
                // Only filenames and version folders are read. Voice models and account files are never opened.
                var version = Directory.EnumerateDirectories(directory).Where(d => File.Exists(Path.Combine(d, voice + ".tsnvoice")))
                    .OrderByDescending(d => ParseVersion(Path.GetFileName(d))).ThenByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
                if (version != null) result.Add(new VoiSonaSinger(language, Path.GetFileName(version), version));
            }
            return result;
        }
        static Version ParseVersion(string version) => Version.TryParse(version.Split(' ')[0], out var result) ? result : new Version(0, 0);
        public static void Register() {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("VoiSona rendering requires macOS.");
            if (PluginPath == null) throw new InvalidOperationException("Install VoiSona Song, including its Audio Unit plugin, before registering it in OpenUtau.");
            Directory.CreateDirectory(Path.GetDirectoryName(RegistrationPath)!);
            File.WriteAllText(RegistrationPath, "{\"provider\":\"VoiSona Song\",\"version\":1}\n");
        }
    }
}
