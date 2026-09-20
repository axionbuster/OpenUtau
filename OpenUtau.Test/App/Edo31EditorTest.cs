using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
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
            foreach (int step in new[] { 155, 160, 163, 165, 167, 168, 173, 178, 183, 184, 186 }) {
                part.notes.Add(project.CreateGridNote(step, (step - 155) * 40, 120));
            }
            var previous = DocManager.Inst.TakeProjectForTest(project);
            var previousSink = DocManager.Inst.CommandSink;
            DocManager.Inst.CommandSink = _ => { };
            int oldKey = Preferences.Default.PreferredKey31Fifths;
            bool? oldFold = Preferences.Default.FoldMajor31;
            bool? oldMinor = Preferences.Default.FoldMinor31;
            bool oldLegacyFold = Preferences.Default.FoldDiatonic31;
            string oldTheme = Preferences.Default.ThemeName;
            double oldKeyboardWidth = Preferences.Default.PianoRollKeyboardWidth;
            string prefsPath = PathManager.Inst.PrefsFilePath;
            byte[]? prefs = File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null;
            Window? window = null;
            try {
                Preferences.Default.PreferredKey31Fifths = 2;
                Preferences.Default.FoldDiatonic31 = true;
                Preferences.Default.FoldMajor31 = null;
                Preferences.Default.FoldMinor31 = null;
                Preferences.Default.PianoRollKeyboardWidth = 176;
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                Assert.True(vm.NotesViewModel.FoldMajor31);
                Assert.True(vm.NotesViewModel.FoldMinor31);
                vm.NotesViewModel.FoldMajor31 = false;
                vm.NotesViewModel.FoldMinor31 = false;
                var editor = new PianoRoll(vm);
                var pianoRollGrid = editor.FindControl<Grid>("PianoRollGrid");
                var keyboardColumn = pianoRollGrid.ColumnDefinitions[0];
                var keyboardSplitter = editor.FindControl<GridSplitter>("KeyboardColumnSplitter");
                Assert.Equal(176, keyboardColumn.Width.Value);
                Assert.Equal(72, keyboardColumn.MinWidth);
                Assert.Equal(320, keyboardColumn.MaxWidth);
                Assert.Equal(GridResizeDirection.Columns, keyboardSplitter.ResizeDirection);
                Assert.Equal(GridResizeBehavior.CurrentAndNext, keyboardSplitter.ResizeBehavior);
                window = new Window { Width = 1100, Height = 900, Content = editor };
                window.Show();
                vm.NotesViewModel.OnNext(new LoadPartNotification(part, project, 0), false);
                vm.NotesViewModel.TrackHeight = 18;
                vm.NotesViewModel.TrackOffset = 341 - 1 - 193;
                Dispatcher.UIThread.RunJobs();
                var notes = vm.NotesViewModel;
                Assert.Equal(341, notes.TrackCount);
                Assert.Equal(31, notes.Keys.Count);
                Assert.Equal("Tonic: D", notes.KeyText);
                Assert.True(editor.FindControl<MenuItem>("SpellingMenu").IsVisible);
                for (int step = 155; step <= 186; step++) {
                    var point = notes.TickToneToPoint(0, notes.GridToTone(step));
                    var center = point.WithY(point.Y + notes.TrackHeight / 2);
                    Assert.Equal(step, notes.PointToTone(center));
                    Assert.InRange(Math.Abs(notes.PointToToneDouble(center) - notes.GridToTone(step)), 0, 1e-9);
                }
                var scaleKeyboard = editor.FindControl<Control>("ScaleKeyboard");
                var augmentedSixth = notes.TickToneToPoint(0, notes.GridToTone(185));
                var hoverPoint = scaleKeyboard.TranslatePoint(
                    new Point(10, augmentedSixth.Y + notes.TrackHeight / 2), window)!.Value;
                window.MouseMove(hoverPoint, RawInputModifiers.None);
                Assert.Equal("25 steps", ToolTip.GetTip(scaleKeyboard));
                project.BeforeSave();
                string before = Core.Format.Ustx31.Serialize(project);
                project.AfterSave();
                bool savedBefore = DocManager.Inst.ChangesSaved;
                notes.SetKeyCommand.Execute(0).Subscribe();
                Assert.Equal("Tonic: C", notes.KeyText);
                Assert.Equal(savedBefore, DocManager.Inst.ChangesSaved);
                project.BeforeSave();
                Assert.Equal(before, Core.Format.Ustx31.Serialize(project));
                project.AfterSave();
                notes.SetKeyCommand.Execute(2).Subscribe();
                Capture(window, "keyboard-31edo.png");
                notes.SetKeyCommand.Execute(-15).Subscribe();
                Assert.Equal("Tonic: F♭♭", notes.KeyText);
                Dispatcher.UIThread.RunJobs();
                Capture(window, "keyboard-31edo-f-double-flat.png");
                notes.SetKeyCommand.Execute(2).Subscribe();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("[31-TET]", new MainWindowViewModel().AppVersion);
                Assert.Contains("0.1.570-tet31.", new MainWindowViewModel().AppVersion);
                var toggle = editor.FindControl<CheckBox>("MajorScaleToggle");
                Assert.True(toggle.IsVisible);
                editor.Focus();
                var togglePoint = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
                window.MouseDown(togglePoint, MouseButton.Left);
                window.MouseUp(togglePoint, MouseButton.Left);
                Assert.True(toggle.IsChecked);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.True(toggle.IsChecked);
                var minorToggle = editor.FindControl<CheckBox>("MinorScaleToggle");
                toggle.Focus(NavigationMethod.Tab);
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Assert.True(minorToggle.IsFocused);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.True(minorToggle.IsChecked);
                notes.FoldMinor31 = false;
                editor.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.True(notes.FoldMajor31);
                Assert.True(Preferences.Default.FoldMajor31);
                Assert.DoesNotContain(185, notes.DisplayRows!); // D augmented sixth is not in major.
                Assert.DoesNotContain(181, notes.DisplayRows!); // D major omits B-flat.
                Assert.Contains(155, notes.DisplayRows!); // Existing C remains visible.
                notes.FoldMajor31 = false;
                notes.FoldMinor31 = true;
                Assert.Contains(181, notes.DisplayRows!);
                Assert.DoesNotContain(185, notes.DisplayRows!); // Nor is it in natural minor.
                Assert.DoesNotContain(170, notes.DisplayRows!); // D minor omits F-sharp.
                notes.FoldMajor31 = true;
                Assert.Equal(113, notes.DisplayTrackCount);
                // D major and D natural minor, with both forms of degrees 3, 6, and 7.
                foreach (int step in new[] { 160, 165, 168, 170, 173, 178, 181, 183, 186, 188 }) {
                    Assert.Contains(step, notes.DisplayRows!);
                }
                Assert.Contains(155, notes.DisplayRows!); // C in D natural minor.
                Assert.Contains(186, notes.DisplayRows!); // C in D natural minor.
                Assert.Contains(163, notes.DisplayRows!); // E-flat is a present flat second.
                Assert.Contains(167, notes.DisplayRows!); // E-sharp is a present sharp second.
                Assert.Contains(184, notes.DisplayRows!); // C-flat is a present double-flat seventh.
                Assert.DoesNotContain(132, notes.DisplayRows!); // Exceptional degrees do not reveal every octave.
                Assert.DoesNotContain(136, notes.DisplayRows!);
                Assert.DoesNotContain(153, notes.DisplayRows!);
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
                foreach (string theme in new[] { "Light", "Dark" }) {
                    Preferences.Default.ThemeName = theme;
                    App.SetTheme();
                    Dispatcher.UIThread.RunJobs();
                    Capture(window, $"keyboard-31edo-{theme}.png");
                    notes.TrackHeight = notes.TrackHeightMin;
                    Capture(window, $"keyboard-31edo-{theme}-zoomed-out.png");
                    notes.FoldMajor31 = false;
                    notes.FoldMinor31 = false;
                    Capture(window, $"keyboard-31edo-{theme}-chromatic-zoomed-out.png");
                    notes.FoldMajor31 = true;
                    notes.FoldMinor31 = true;
                    notes.TrackHeight = 18;
                }
                notes.SetKeyCommand.Execute(0).Subscribe();
                Assert.Contains(155, notes.DisplayRows!);
                Assert.DoesNotContain(157, notes.DisplayRows!);
                Assert.DoesNotContain(180, notes.DisplayRows!); // C augmented sixth is hidden by default.
                foreach (int step in new[] { 155, 160, 163, 165, 168, 173, 176, 178, 181, 183 }) {
                    Assert.Contains(step, notes.DisplayRows!);
                }
                notes.FoldMajor31 = false;
                notes.FoldMinor31 = false;
                Assert.Null(notes.DisplayRows);
                notes.FoldMajor31 = true;

                var ordinary = Core.Format.Ustx31.ConvertCopy(project, false);
                DocManager.Inst.TakeProjectForTest(ordinary);
                notes.OnNext(new LoadProjectNotification(ordinary), false);
                notes.OnNext(new LoadPartNotification(ordinary.parts[0], ordinary, 0), false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(132, notes.TrackCount);
                Assert.Null(notes.DisplayRows);
                Assert.False(toggle.IsVisible);
                Assert.Equal(12, notes.Keys.Count);
                Assert.DoesNotContain("Tonic", notes.KeyText);
                Assert.False(editor.FindControl<MenuItem>("SpellingMenu").IsVisible);
                Capture(window, "keyboard-12edo.png");
            } finally {
                window?.Close();
                ThreadGuard.SetUiThread(null);
                Preferences.Default.PreferredKey31Fifths = oldKey;
                Preferences.Default.FoldMajor31 = oldFold;
                Preferences.Default.FoldMinor31 = oldMinor;
                Preferences.Default.FoldDiatonic31 = oldLegacyFold;
                Preferences.Default.ThemeName = oldTheme;
                Preferences.Default.PianoRollKeyboardWidth = oldKeyboardWidth;
                App.SetTheme();
                if (prefs != null) { File.WriteAllBytes(prefsPath, prefs); }
                else if (File.Exists(prefsPath)) { File.Delete(prefsPath); }
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
            }
        }
        static void Capture(Window window, string name) {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(1100, 900), frame.PixelSize);
            string? output = Environment.GetEnvironmentVariable("OPENUTAU_QA_OUTPUT");
            if (!string.IsNullOrEmpty(output)) {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, name));
            }
        }

        [AvaloniaFact]
        public void TextLayoutCacheKeepsItalicNoteNamesDistinct() {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            try {
                var cacheType = typeof(PianoRoll).Assembly.GetType("OpenUtau.App.Controls.TextLayoutCache")!;
                var get = cacheType.GetMethod("Get", System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static)!;
                var upright = (TextLayout)get.Invoke(null, new object[] {
                    "B♯4", Brushes.Black, 12d, false, false, 0d })!;
                var italic = (TextLayout)get.Invoke(null, new object[] {
                    "B♯4", Brushes.Black, 12d, false, true, 0d })!;
                var compact = (TextLayout)get.Invoke(null, new object[] {
                    "F♭♭4", Brushes.Black, 12d, false, false, -0.4d })!;
                Assert.NotSame(upright, italic);
                Assert.Equal(FontStyle.Normal, upright.TextLines.Single().TextRuns.First().Properties!.Typeface.Style);
                Assert.Equal(FontStyle.Italic, italic.TextLines.Single().TextRuns.First().Properties!.Typeface.Style);
                Assert.Equal(-0.4, compact.LetterSpacing);
                var compactSpacing = cacheType.GetMethod("CompactAccidentalLetterSpacing",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
                Assert.Equal(0d, compactSpacing.Invoke(null, new object[] { "F♭4" }));
                Assert.Equal(-0.4d, compactSpacing.Invoke(null, new object[] { "F♭♭4" }));
                Assert.Equal(-0.4d, compactSpacing.Invoke(null, new object[] { "F♯♯4" }));
            } finally {
                ThreadGuard.SetUiThread(null);
            }
        }

        [AvaloniaFact]
        public void PitchReferenceDialogShowsNativeAnchorModes() {
            var absolute = new Views.PitchReference31Dialog(Edo31PitchReference.Default);
            Assert.True(absolute.FindControl<RadioButton>("A4Mode").IsChecked);
            Assert.Equal(440m, absolute.FindControl<NumericUpDown>("A4Frequency").Value);
            Assert.Contains("native anchor at 440", absolute.FindControl<TextBlock>("Summary").Text);

            var synchronized = new Views.PitchReference31Dialog(Edo31PitchReference.LegacyC);
            Assert.True(synchronized.FindControl<RadioButton>("NoteMode").IsChecked);
            Assert.Equal(0, synchronized.FindControl<ComboBox>("ReferenceNote").SelectedIndex);
            Assert.Contains("437.547", synchronized.FindControl<TextBlock>("Summary").Text);
        }
    }
}
