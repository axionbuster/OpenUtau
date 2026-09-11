using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class Edo31EditorTest {
        [AvaloniaFact]
        public void NativeKeyboardEditsAndRespellsWithoutDocumentChanges() {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var project = Core.Format.Ustx.Create();
            project.Is31Edo = true;
            var part = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 };
            project.parts.Add(part);
            foreach (int step in new[] { 155, 160, 165, 168, 173, 178, 183, 186 }) {
                part.notes.Add(project.CreateGridNote(step, (step - 155) * 40, 120));
            }
            var previous = DocManager.Inst.TakeProjectForTest(project);
            var previousSink = DocManager.Inst.CommandSink;
            DocManager.Inst.CommandSink = _ => { };
            int oldKey = Preferences.Default.PreferredKey31Fifths;
            bool oldFold = Preferences.Default.FoldDiatonic31;
            string prefsPath = PathManager.Inst.PrefsFilePath;
            byte[]? prefs = File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null;
            Window? window = null;
            try {
                Preferences.Default.PreferredKey31Fifths = 2;
                Preferences.Default.FoldDiatonic31 = false;
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                var editor = new PianoRoll(vm);
                window = new Window { Width = 1100, Height = 900, Content = editor };
                window.Show();
                vm.NotesViewModel.OnNext(new LoadPartNotification(part, project, 0), false);
                vm.NotesViewModel.TrackHeight = 18;
                vm.NotesViewModel.TrackOffset = 341 - 1 - 193;
                Dispatcher.UIThread.RunJobs();
                var notes = vm.NotesViewModel;
                Assert.Equal(341, notes.TrackCount);
                Assert.Equal(31, notes.Keys.Count);
                Assert.Equal("Spelling: D", notes.KeyText);
                Assert.True(editor.FindControl<MenuItem>("SpellingMenu").IsVisible);
                for (int step = 155; step <= 186; step++) {
                    var point = notes.TickToneToPoint(0, notes.GridToTone(step));
                    var center = point.WithY(point.Y + notes.TrackHeight / 2);
                    Assert.Equal(step, notes.PointToTone(center));
                    Assert.InRange(Math.Abs(notes.PointToToneDouble(center) - notes.GridToTone(step)), 0, 1e-9);
                }
                project.BeforeSave();
                string before = Core.Format.Ustx31.Serialize(project);
                project.AfterSave();
                bool savedBefore = DocManager.Inst.ChangesSaved;
                notes.SetKeyCommand.Execute(0).Subscribe();
                Assert.Equal("Spelling: C", notes.KeyText);
                Assert.Equal(savedBefore, DocManager.Inst.ChangesSaved);
                project.BeforeSave();
                Assert.Equal(before, Core.Format.Ustx31.Serialize(project));
                project.AfterSave();
                notes.SetKeyCommand.Execute(2).Subscribe();
                Capture(window, "keyboard-31edo.png");
                Assert.Contains("[31-TET]", new MainWindowViewModel().AppVersion);
                Assert.Contains("0.1.570-tet31.", new MainWindowViewModel().AppVersion);
                var toggle = editor.FindControl<CheckBox>("DiatonicToggle");
                Assert.True(toggle.IsVisible);
                editor.Focus();
                var togglePoint = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
                window.MouseDown(togglePoint, MouseButton.Left);
                window.MouseUp(togglePoint, MouseButton.Left);
                Assert.True(toggle.IsChecked);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.True(toggle.IsChecked);
                Dispatcher.UIThread.RunJobs();
                Assert.True(notes.FoldDiatonic31);
                Assert.True(Preferences.Default.FoldDiatonic31);
                Assert.Equal(110, notes.DisplayTrackCount);
                // D major and D natural minor, with both forms of degrees 3, 6, and 7.
                foreach (int step in new[] { 160, 165, 168, 170, 173, 178, 181, 183, 186, 188 }) {
                    Assert.Contains(step, notes.DisplayRows!);
                }
                Assert.Contains(155, notes.DisplayRows!); // C in D natural minor.
                Assert.Contains(186, notes.DisplayRows!); // C in D natural minor.
                Assert.Equal(341, notes.TrackCount);
                Assert.DoesNotContain(156, notes.DisplayRows!);
                Assert.Contains(165, notes.DisplayRows!); // Existing E remains visible.
                foreach (int step in notes.DisplayRows!) {
                    var top = notes.TickToneToPoint(0, notes.GridToTone(step));
                    var center = top.WithY(top.Y + notes.TrackHeight / 2);
                    Assert.Equal(step, notes.PointToTone(center));
                    Assert.InRange(Math.Abs(notes.PointToToneDouble(center) - notes.GridToTone(step)), 0, 1e-9);
                    Assert.Equal(center, notes.TickToneToCenterPoint(0, notes.GridToTone(step)));
                }
                foreach (var note in part.notes) {
                    var top = notes.TickToneToPoint(note.position + 60, note.AdjustedTone);
                    var hitTest = typeof(NotesViewModel).GetField("HitTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(notes)!;
                    foreach (double y in new[] { top.Y + 1, top.Y + notes.TrackHeight - 1 }) {
                        var hit = hitTest.GetType().GetMethod("HitTestNote")!.Invoke(hitTest, new object[] { top.WithY(y) })!;
                        Assert.True((bool)hit.GetType().GetField("hitBody")!.GetValue(hit)!);
                    }
                }
                Assert.Equal(savedBefore, DocManager.Inst.ChangesSaved);
                project.BeforeSave();
                Assert.Equal(before, Core.Format.Ustx31.Serialize(project));
                project.AfterSave();
                notes.TrackOffset = notes.DisplayTrackCount - 1 - notes.StepToDisplayRow(193);
                Capture(window, "keyboard-31edo-diatonic.png");
                notes.SetKeyCommand.Execute(0).Subscribe();
                Assert.Contains(155, notes.DisplayRows!);
                Assert.DoesNotContain(157, notes.DisplayRows!);
                foreach (int step in new[] { 155, 160, 163, 165, 168, 173, 176, 178, 181, 183 }) {
                    Assert.Contains(step, notes.DisplayRows!);
                }
                notes.FoldDiatonic31 = false;
                Assert.Null(notes.DisplayRows);
                notes.FoldDiatonic31 = true;

                var ordinary = Core.Format.Ustx31.ConvertCopy(project, false);
                DocManager.Inst.TakeProjectForTest(ordinary);
                notes.OnNext(new LoadProjectNotification(ordinary), false);
                notes.OnNext(new LoadPartNotification(ordinary.parts[0], ordinary, 0), false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(132, notes.TrackCount);
                Assert.Null(notes.DisplayRows);
                Assert.False(toggle.IsVisible);
                Assert.Equal(12, notes.Keys.Count);
                Assert.DoesNotContain("Spelling", notes.KeyText);
                Assert.False(editor.FindControl<MenuItem>("SpellingMenu").IsVisible);
                Capture(window, "keyboard-12edo.png");
            } finally {
                window?.Close();
                ThreadGuard.SetUiThread(null);
                Preferences.Default.PreferredKey31Fifths = oldKey;
                Preferences.Default.FoldDiatonic31 = oldFold;
                if (prefs != null) { File.WriteAllBytes(prefsPath, prefs); }
                else if (File.Exists(prefsPath)) { File.Delete(prefsPath); }
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
            }
        }
        static void Capture(Window window, string name) {
            string? output = Environment.GetEnvironmentVariable("OPENUTAU_QA_OUTPUT");
            if (string.IsNullOrEmpty(output)) { return; }
            Directory.CreateDirectory(output);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name));
        }
    }
}
