using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class NotePreviewTest {
        [AvaloniaTheory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void DrawingStopsTheOriginalPreview(bool edo31, bool drag) {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var project = Core.Format.Ustx.Create();
            project.Is31Edo = edo31;
            var part = new UVoicePart { trackNo = 0, Duration = 1920 };
            project.parts.Add(part);
            var previous = DocManager.Inst.TakeProjectForTest(project);
            var previousSink = DocManager.Inst.CommandSink;
            var previousOutput = PlaybackManager.Inst.AudioOutput;
            bool previousPlay = Preferences.Default.PlayTone;
            bool previousFold = Preferences.Default.FoldDiatonic31;
            try {
                DocManager.Inst.CommandSink = _ => { };
                PlaybackManager.Inst.AudioOutput = new OpenUtau.Audio.DummyAudioOutput();
                PlaybackManager.Inst.EndAllTones();
                Preferences.Default.PlayTone = true;
                Preferences.Default.FoldDiatonic31 = false;
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                var editor = new PianoRoll(vm);
                vm.NotesViewModel.OnNext(new LoadPartNotification(part, project, 0), false);
                var notes = vm.NotesViewModel;
                notes.TrackHeight = 18;
                int step = edo31 ? 160 : 62;
                var point = notes.TickToneToCenterPoint(0, notes.GridToTone(step));
                var stateType = typeof(PianoRoll).Assembly.GetType("OpenUtau.App.Views.NoteDrawEditState")!;
                var state = Activator.CreateInstance(stateType, editor, vm, editor, true)!;
                var pointer = new Pointer(1, PointerType.Mouse, true);
                void Invoke(string method, Point p) => stateType.GetMethod(method)!.Invoke(state, new object[] { pointer, p });
                float[] ReadAudio() {
                    var samples = new float[8820];
                    PlaybackManager.Inst.toneGenerator.Mix(0, samples, 0, samples.Length);
                    return samples;
                }
                Invoke("Begin", point);
                Assert.Contains(ReadAudio(), sample => Math.Abs(sample) > 0.01f);
                if (drag) {
                    point = notes.TickToneToCenterPoint(120, notes.GridToTone(step + 1));
                    Invoke("Update", point);
                    ReadAudio();
                }
                Invoke("End", point);
                ReadAudio(); // Let the 25 ms release finish.
                Assert.All(ReadAudio(), sample => Assert.Equal(0f, sample));
            } finally {
                PlaybackManager.Inst.EndAllTones();
                PlaybackManager.Inst.AudioOutput = previousOutput;
                Preferences.Default.PlayTone = previousPlay;
                Preferences.Default.FoldDiatonic31 = previousFold;
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
            }
        }
    }
}
