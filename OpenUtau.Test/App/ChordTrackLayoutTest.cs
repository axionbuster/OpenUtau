#nullable enable
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData.Binding;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class ChordTrackLayoutTest {
        [AvaloniaFact]
        public void AutomaticAndExtremeCustomColorsKeepControlContrast() {
            var oldDark = ThemeManager.IsDarkMode;
            var oldBackground = ThemeManager.BackgroundBrush;
            try {
                ThemeManager.IsDarkMode = false;
                ThemeManager.BackgroundBrush = Avalonia.Media.Brushes.White;
                Assert.True(ThemeManager.ContrastRatio(
                    ThemeManager.GetTrackColor("Automatic (theme)").AccentColor.Color,
                    Avalonia.Media.Colors.White) >= 3);
                Assert.NotEqual("Yellow", ThemeManager.GetControlTrackColor("Yellow").Name);
                ThemeManager.IsDarkMode = true;
                ThemeManager.BackgroundBrush = Avalonia.Media.Brushes.Black;
                Assert.True(ThemeManager.ContrastRatio(
                    ThemeManager.GetTrackColor("Automatic (theme)").AccentColor.Color,
                    Avalonia.Media.Colors.Black) >= 3);
                Assert.NotEqual("Purple2", ThemeManager.GetControlTrackColor("Purple2").Name);
            } finally {
                ThemeManager.IsDarkMode = oldDark;
                ThemeManager.BackgroundBrush = oldBackground;
            }
        }

        [AvaloniaFact]
        public void NewProjectRendersChordsAndVoiceAsSeparateHeaderRows() {
            AssertHeaderLayout(Core.Format.Ustx.Create());
        }

        [AvaloniaFact]
        public void ReopenedProjectRendersChordsAndVoiceAsSeparateHeaderRows() {
            var project = Core.Format.Ustx.Create();
            project.BeforeSave();
            string text;
            try { text = Ustx31.Serialize(project); }
            finally { project.AfterSave(); }
            var loaded = Ustx31.Deserialize(text);
            loaded.AfterLoad();
            AssertHeaderLayout(loaded);
        }

        [AvaloniaFact]
        public void PrepopulatedPartsCollectionHydratesOnceAndTracksReplacement() {
            var project = Core.Format.Ustx.Create();
            project.ChordsPart.chordRegions.Add(new UChordRegion {
                sourceDuration = 1920, duration = 3840,
            });
            var first = new ObservableCollection<UPart>(project.parts);
            var canvas = new PartsCanvas { Items = first };
            Assert.Equal(first.Count, canvas.Children.OfType<PartControl>().Count());
            canvas.Items = first;
            Assert.Equal(first.Count, canvas.Children.OfType<PartControl>().Count());

            var replacementPart = new UVoicePart { trackNo = 1, duration = 1920 };
            var replacement = new ObservableCollection<UPart> { project.ChordsPart, replacementPart };
            canvas.Items = replacement;
            Assert.Equal(2, canvas.Children.OfType<PartControl>().Count());
            Assert.Contains(canvas.Children.OfType<PartControl>(), control => control.part == project.ChordsPart);
            Assert.Contains(canvas.Children.OfType<PartControl>(), control => control.part == replacementPart);
            Assert.DoesNotContain(canvas.Children.OfType<PartControl>(), control =>
                !replacement.Contains(control.part));
        }

        [AvaloniaFact]
        public void PartsAddRangeResetRehydratesRenderedControls() {
            var project = Core.Format.Ustx.Create();
            project.ChordsPart.duration = 5760;
            project.ChordsPart.chordRegions.Add(new UChordRegion {
                sourceDuration = 1920, duration = 4800,
            });
            var items = new ObservableCollectionExtended<UPart>();
            var canvas = new PartsCanvas { Items = items };
            items.AddRange(project.parts);
            var control = Assert.Single(canvas.Children.OfType<PartControl>(), child =>
                child.part == project.ChordsPart);
            Assert.Equal(5760, control.part.Duration);
            Assert.Same(project.ChordsPart.chordRegions[0],
                Assert.Single(((UVoicePart)control.part).chordRegions));
            Assert.Equal(project.parts.Count, canvas.Children.OfType<PartControl>().Count());
        }

        static void AssertHeaderLayout(UProject project) {
            var oldProject = DocManager.Inst.TakeProjectForTest(project);
            var oldSink = DocManager.Inst.CommandSink;
            var oldUseTrackColor = Core.Util.Preferences.Default.UseTrackColor;
            Window? window = null;
            try {
                Core.Util.Preferences.Default.UseTrackColor = false;
                DocManager.Inst.CommandSink = _ => { };
                var canvas = new TrackHeaderCanvas();
                canvas.SetValue(TrackHeaderCanvas.TrackHeightProperty, 105d);
                window = new Window { Width = 320, Height = 260, Content = canvas };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                canvas.Items = new ObservableCollection<UTrack>();
                foreach (var track in project.tracks) canvas.Items.Add(track);
                Dispatcher.UIThread.RunJobs();

                var headers = canvas.Children.OfType<TrackHeader>()
                    .OrderBy(Canvas.GetTop).ToArray();
                double chordHeight = TrackLayout.ChordHeight(105);
                Assert.Equal(2, headers.Length);
                Assert.Equal(0, Canvas.GetTop(headers[0]));
                Assert.Equal(chordHeight, Canvas.GetTop(headers[1]));
                Assert.Equal(chordHeight, headers[0].Bounds.Height);
                Assert.Equal("Chords", headers[0].ViewModel!.TrackName);
                Assert.Equal(0, headers[0].ViewModel!.TrackNo);
                Assert.Equal(1, headers[1].ViewModel!.TrackNo);
                Assert.Equal("Automatic (theme)", headers[0].ViewModel!.TrackColor.Name);
                Assert.Equal("Blue", headers[1].ViewModel!.TrackColor.Name);
                Assert.True(headers[0].Bounds.Bottom <= headers[1].Bounds.Top);
                Assert.DoesNotContain(headers[0].GetVisualDescendants().OfType<Image>(), image => image.IsEffectivelyVisible);
                Assert.DoesNotContain(headers[0].GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "0");
                Assert.Contains(headers[0].GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && Equals(button.Content, "Edit"));
                Assert.Contains(headers[1].GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "1");

                canvas.SetValue(TrackHeaderCanvas.TrackOffsetProperty, 1d);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, Canvas.GetTop(headers[0]));
                Assert.Equal(chordHeight - 105, Canvas.GetTop(headers[1]));
                Assert.True(headers[0].ZIndex > headers[1].ZIndex);
                Assert.Equal(0, TrackLayout.TrackNoAt(50, 1, 105));
                Assert.Equal(2, TrackLayout.TrackNoAt(chordHeight + 1, 1, 105));

                canvas.SetValue(TrackHeaderCanvas.TrackHeightProperty, 42d);
                Dispatcher.UIThread.RunJobs();
                // The chord row keeps shrinking past the ordinary track minimum.
                Assert.Equal(28, headers[0].Bounds.Height);
                Assert.Equal(28, Canvas.GetTop(headers[1]) + 42);
                Assert.False(headers[0].ViewModel!.IsChordMixerVisible);
            } finally {
                window?.Close();
                Core.Util.Preferences.Default.UseTrackColor = oldUseTrackColor;
                DocManager.Inst.TakeProjectForTest(oldProject);
                DocManager.Inst.CommandSink = oldSink;
            }
        }
    }
}
