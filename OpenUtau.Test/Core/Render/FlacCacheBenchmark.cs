using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using NAudio.Wave;
using OpenUtau.Core.Format;
using Xunit;
namespace OpenUtau.Core {
 public class FlacCacheBenchmark {
  [Fact(Skip = "Manual benchmark: set OPENUTAU_CACHE_BENCHMARK and remove Skip locally.")] public void MeasureRealCache() {
   string root = Path.Combine(Path.GetTempPath(), "openutau-flac-benchmark");
   Directory.CreateDirectory(root);
   var rows = new List<object>();
   foreach (string path in Directory.GetFiles(Environment.GetEnvironmentVariable("OPENUTAU_CACHE_BENCHMARK")!, "*.wav").OrderBy(p=>p)) {
    if (!Path.GetFileName(path).StartsWith("voisona-") && !Path.GetFileName(path).StartsWith("wdl-")) continue;
    var sw = Stopwatch.StartNew();
    using var reader = Wave.OpenFile(path);
    bool voisona = Path.GetFileName(path).StartsWith("voisona-");
    var provider = reader.ToSampleProvider();
    float[] samples = Wave.GetSamples(reader.WaveFormat.Channels == 1 ? provider : provider.ToMono(voisona ? .5f : 1f, voisona ? .5f : 0f));
    double wavRead = sw.Elapsed.TotalMilliseconds;
    string flac = Path.Combine(root, Path.GetFileNameWithoutExtension(path)+".flac");
    sw.Restart();
    if (voisona) Wave.WriteMonoCache(flac, samples, 24);
    else { string copy = flac + ".wav"; File.Copy(path, copy, true); Wave.ConvertCacheFile(copy, flac); }
    double write = sw.Elapsed.TotalMilliseconds;
    sw.Restart(); var decoded = Wave.ReadMonoCache(flac); double read = sw.Elapsed.TotalMilliseconds;
    Assert.Equal(samples.Length, decoded.Length);
    double error = samples.Zip(decoded).Max(p => Math.Abs(p.First-p.Second));
    Assert.True(error <= (voisona ? 1.0/8388608 : 0), $"{path}: {error}");
    rows.Add(new { file=Path.GetFileName(path), frames=samples.Length, wavBytes=new FileInfo(path).Length, flacBytes=new FileInfo(flac).Length, wavReadMs=wavRead, flacWriteMs=write, flacReadMs=read, maxError=error });
   }
   File.WriteAllText(Path.Combine(root,"results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
   Assert.NotEmpty(rows);
  }
 }
}
