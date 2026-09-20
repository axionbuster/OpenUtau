#nullable enable
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
                Assert.Equal(2, headers.Length);
                Assert.Equal(0, Canvas.GetTop(headers[0]));
                Assert.Equal(105, Canvas.GetTop(headers[1]));
                Assert.Equal("Chords", headers[0].ViewModel!.TrackName);
                Assert.Equal(1, headers[0].ViewModel!.TrackNo);
                Assert.Equal(2, headers[1].ViewModel!.TrackNo);
                Assert.Equal("Ivory", headers[0].ViewModel!.TrackColor.Name);
                Assert.Equal("Blue", headers[1].ViewModel!.TrackColor.Name);
                Assert.True(headers[0].Bounds.Bottom <= headers[1].Bounds.Top);
            } finally {
                window?.Close();
                Core.Util.Preferences.Default.UseTrackColor = oldUseTrackColor;
                DocManager.Inst.TakeProjectForTest(oldProject);
                DocManager.Inst.CommandSink = oldSink;
            }
        }
    }
}
