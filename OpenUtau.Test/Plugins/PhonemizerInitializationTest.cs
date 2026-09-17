using System;
using System.Threading;
using OpenUtau.Api;

using OpenUtau.Core;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    [Collection(RenderSingletonCollection.Name)]
    public class PhonemizerInitializationTest {
        sealed class TestSinger : USinger {
            public TestSinger(bool ready = true) { found = true; loaded = ready; }
            public void FinishLoading() { loaded = true; }
        }
        // Use the real English X-SAMPA configuration and production Testing=false
        // path, with a controlled dictionary loader instead of disk/network timing.
        sealed class Probe : EnXSampaPhonemizer {
            protected override string YamlFileName => "";
            public int Loads;
            public bool FailNext;
            public bool Ready => hasDictionary && !isDictionaryLoading;
            protected override IG2p LoadBaseDictionary() {
                Loads++;
                Thread.Sleep(30);
                if (FailNext) {
                    FailNext = false;
                    throw new InvalidOperationException("dictionary temporarily unavailable");
                }
                return new G2pDictionary.Builder().Build();
            }
        }
        [Fact]
        public void FirstSingerAndSingerSwitchAreReadyBeforeProcessing() {
            var phonemizer = new Probe();
            var singer = new TestSinger();
            phonemizer.SetSinger(singer);
            Assert.True(phonemizer.Ready);
            Assert.Equal(1, phonemizer.Loads);
            phonemizer.SetSinger(singer);
            Assert.Equal(1, phonemizer.Loads);
            phonemizer.SetSinger(new TestSinger());
            Assert.True(phonemizer.Ready);
            Assert.Equal(2, phonemizer.Loads);
        }
        [Fact]
        public void SingerBecomingLoadedRetriesInitializationWithoutReopening() {
            var phonemizer = new Probe();
            var singer = new TestSinger(false);
            phonemizer.SetSinger(singer);
            Assert.False(phonemizer.Ready);
            singer.FinishLoading();
            phonemizer.SetSinger(singer);
            Assert.True(phonemizer.Ready);
            Assert.Equal(1, phonemizer.Loads);
        }
        [Fact]
        public void FailedDictionaryLoadCanRetrySameSinger() {
            var phonemizer = new Probe { FailNext = true };
            var singer = new TestSinger();
            Assert.Throws<InvalidOperationException>(() => phonemizer.SetSinger(singer));
            phonemizer.SetSinger(singer);
            Assert.True(phonemizer.Ready);
            Assert.Equal(2, phonemizer.Loads);
        }
    }
}
