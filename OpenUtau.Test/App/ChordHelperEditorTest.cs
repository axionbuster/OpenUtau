using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class ChordHelperEditorTest {
        static (UProject Project, UVoicePart Current, UVoicePart SameTrack, UVoicePart Foreign) Fixture(bool is31Edo = false) {
            var project = Core.Format.Ustx.Create();
            project.Is31Edo = is31Edo;
            project.tracks.Clear();
            project.tracks.Add(new UTrack("Selected") { TrackNo = 0 });
            project.tracks.Add(new UTrack("Foreign") { TrackNo = 1 });
            var current = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 };
            var sameTrack = new UVoicePart { trackNo = 0, position = 240, Duration = 1920 };
            var foreign = new UVoicePart { trackNo = 1, position = 0, Duration = 1920 };
            var performed = project.CreateGridNote(is31Edo ? 155 : 60, 420, 400);
            performed.lyric = "performed";
            current.notes.Add(performed);
            sameTrack.chordHelpers.Add(new UChordHelper {
                position = 120,
                duration = 720,
                root = 0,
                tones = ChordHelperTheory.CreatePreset("Major"),
                color = "#E44E73",
            });
            foreign.chordHelpers.Add(new UChordHelper {
                position = 120,
                duration = 720,
                root = is31Edo ? 5 : 2,
                tones = ChordHelperTheory.CreatePreset("Minor"),
                color = "#35B77A",
            });
            project.parts.Add(current);
            project.parts.Add(sameTrack);
            project.parts.Add(foreign);
            return (project, current, sameTrack, foreign);
        }

        [AvaloniaFact]
        public void ForeignHelpersStayVisibleButCannotBeSelectedAndTrackSwitchClearsSelection() {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var fixture = Fixture();
            var previous = DocManager.Inst.TakeProjectForTest(fixture.Project);
            try {
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                vm.NotesViewModel.Part = fixture.Current;
                var visible = ChordHelperViewModel.VisibleHelpers(fixture.Project).ToList();
                Assert.Contains(visible, item => item.Part == fixture.Foreign);
                Assert.True(ChordHelperViewModel.IsOwnedByEditorTrack(fixture.Current, fixture.SameTrack));
                Assert.False(ChordHelperViewModel.IsOwnedByEditorTrack(fixture.Current, fixture.Foreign));

                var localHelper = Assert.Single(fixture.SameTrack.chordHelpers);
                Assert.True(vm.ChordHelpers.TrySelect(fixture.Current, fixture.SameTrack, localHelper));
                Assert.True(vm.ChordHelpers.HasSelection);
                vm.NotesViewModel.Part = fixture.Foreign;
                Assert.False(vm.ChordHelpers.HasSelection);
                Assert.False(vm.ChordHelpers.TrySelect(fixture.Foreign, fixture.SameTrack, localHelper));
                Assert.False(vm.ChordHelpers.HasSelection);
            } finally {
                DocManager.Inst.TakeProjectForTest(previous);
                ThreadGuard.SetUiThread(null);
            }
        }

        [AvaloniaFact]
        public void SelectedHelperUsesDockedPropertyPanelAndRendersOwnershipPreview() {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var fixture = Fixture();
            var previous = DocManager.Inst.TakeProjectForTest(fixture.Project);
            var previousSink = DocManager.Inst.CommandSink;
            Window? window = null;
            try {
                DocManager.Inst.CommandSink = _ => { };
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                var editor = new PianoRoll(vm);
                window = new Window { Width = 1200, Height = 800, Content = editor };
                window.Show();
                vm.NotesViewModel.OnNext(new LoadPartNotification(fixture.Current, fixture.Project, 0), false);
                vm.NotesViewModel.TrackHeight = 22;
                vm.NotesViewModel.TrackOffset = vm.NotesViewModel.TrackCount - 1 - 66;
                var helper = Assert.Single(fixture.SameTrack.chordHelpers);
                Assert.True(vm.ChordHelpers.TrySelect(fixture.Current, fixture.SameTrack, helper));
                vm.NotesViewModel.ShowNoteParams = true;
                Dispatcher.UIThread.RunJobs();

                var panel = editor.FindControl<ScrollViewer>("ChordHelperPanel");
                Assert.True(panel.IsEffectivelyVisible);
                var dock = panel.GetVisualAncestors().OfType<Border>()
                    .First(border => Grid.GetColumn(border) == 3 && Grid.GetRowSpan(border) == 6);
                Assert.NotNull(dock);
                Assert.False(editor.GetVisualDescendants().OfType<NotePropertiesControl>().Single().IsEffectivelyVisible);

                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new Avalonia.PixelSize(1200, 800), frame.PixelSize);
                string? output = Environment.GetEnvironmentVariable("OPENUTAU_QA_OUTPUT");
                if (!string.IsNullOrEmpty(output)) {
                    Directory.CreateDirectory(output);
                    frame.Save(Path.Combine(output, "chord-helper-track-ownership.png"));
                }
            } finally {
                window?.Close();
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
                ThreadGuard.SetUiThread(null);
            }
        }

        [AvaloniaFact]
        public void PresetAndDegreeGridStayBidirectionallyExactAndKeepOneTone() {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var fixture = Fixture();
            var previous = DocManager.Inst.TakeProjectForTest(fixture.Project);
            var previousSink = DocManager.Inst.CommandSink;
            try {
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                vm.NotesViewModel.Part = fixture.Current;
                var helper = Assert.Single(fixture.SameTrack.chordHelpers);
                Assert.True(vm.ChordHelpers.TrySelect(fixture.Current, fixture.SameTrack, helper));
                int commandCount = 0;
                ChangeChordHelperCommand? lastChange = null;
                DocManager.Inst.CommandSink = command => {
                    commandCount++;
                    command.Execute();
                    lastChange = command as ChangeChordHelperCommand ?? lastChange;
                    vm.ChordHelpers.OnCommand(command);
                };

                vm.ChordHelpers.CommitSnapshot(fixture.SameTrack, helper, helper.Clone());
                Assert.Equal(0, commandCount);
                Assert.Equal("Major", vm.ChordHelpers.SelectedQuality);
                vm.ChordHelpers.ToggleDegreeCommand.Execute(
                    vm.ChordHelpers.Degrees.Single(degree => degree.Label == "♭2")).Subscribe();
                Assert.StartsWith("Custom (", vm.ChordHelpers.SelectedQuality);

                vm.ChordHelpers.SelectedQuality = "Diminished seventh";
                Assert.Equal(new[] { "1", "♭3", "♭5", "♭♭7" },
                    vm.ChordHelpers.Degrees.Where(degree => degree.IsEnabled).Select(degree => degree.Label));
                Assert.Equal("Diminished seventh", vm.ChordHelpers.SelectedQuality);
                Assert.NotNull(lastChange);
                lastChange!.Unexecute();
                vm.ChordHelpers.OnCommand(lastChange);
                Assert.StartsWith("Custom (", vm.ChordHelpers.SelectedQuality);

                vm.ChordHelpers.SelectedQuality = "Unison";
                var unison = Assert.Single(vm.ChordHelpers.Degrees.Where(degree => degree.IsEnabled));
                int beforeFinalToggle = commandCount;
                vm.ChordHelpers.ToggleDegreeCommand.Execute(unison).Subscribe();
                Assert.Equal(beforeFinalToggle, commandCount);
                Assert.Single(helper.tones);
                Assert.Equal("Unison", vm.ChordHelpers.SelectedQuality);
            } finally {
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
                ThreadGuard.SetUiThread(null);
            }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void NativeGridAndFoldedRowsRenderOwnershipPreview(bool folded) {
            ThreadGuard.SetUiThread(System.Threading.Thread.CurrentThread);
            var fixture = Fixture(true);
            var previous = DocManager.Inst.TakeProjectForTest(fixture.Project);
            var previousSink = DocManager.Inst.CommandSink;
            bool? oldMajor = Preferences.Default.FoldMajor31;
            bool? oldMinor = Preferences.Default.FoldMinor31;
            int oldKey = Preferences.Default.PreferredKey31Fifths;
            Window? window = null;
            try {
                Preferences.Default.FoldMajor31 = folded;
                Preferences.Default.FoldMinor31 = folded;
                Preferences.Default.PreferredKey31Fifths = 0;
                DocManager.Inst.CommandSink = _ => { };
                DocManager.Inst.SearchAllLegacyPlugins();
                var vm = new PianoRollViewModel();
                var editor = new PianoRoll(vm);
                window = new Window { Width = 1280, Height = 900, Content = editor };
                window.Show();
                vm.NotesViewModel.OnNext(new LoadPartNotification(fixture.Current, fixture.Project, 0), false);
                vm.NotesViewModel.TrackHeight = 22;
                vm.NotesViewModel.TrackOffset = Math.Max(0,
                    vm.NotesViewModel.DisplayTrackCount - 1 - vm.NotesViewModel.StepToDisplayRow(155) - 4);
                var helper = Assert.Single(fixture.SameTrack.chordHelpers);
                Assert.True(vm.ChordHelpers.TrySelect(fixture.Current, fixture.SameTrack, helper));
                vm.NotesViewModel.ShowNoteParams = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(31, vm.ChordHelpers.Degrees.Count);
                Assert.Equal(6, vm.ChordHelpers.DegreeColumns);

                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                string? output = Environment.GetEnvironmentVariable("OPENUTAU_QA_OUTPUT");
                if (!string.IsNullOrEmpty(output)) {
                    Directory.CreateDirectory(output);
                    frame.Save(Path.Combine(output, folded
                        ? "chord-helper-31-folded.png"
                        : "chord-helper-31-all-rows.png"));
                }
            } finally {
                window?.Close();
                Preferences.Default.FoldMajor31 = oldMajor;
                Preferences.Default.FoldMinor31 = oldMinor;
                Preferences.Default.PreferredKey31Fifths = oldKey;
                DocManager.Inst.CommandSink = previousSink;
                DocManager.Inst.TakeProjectForTest(previous);
                ThreadGuard.SetUiThread(null);
            }
        }
    }
}
