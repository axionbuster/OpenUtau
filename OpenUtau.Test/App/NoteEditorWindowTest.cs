using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class NoteEditorWindowTest {
        static PianoRollDetachedWindow? DetachedWindow(MainWindow window) =>
            (PianoRollDetachedWindow?)typeof(MainWindow)
                .GetField("pianoRollWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(window);

        [AvaloniaFact]
        public async Task DetachedNoteEditorReopensAfterItsWindowCloses() {
            ThreadGuard.SetUiThread(Thread.CurrentThread);
            var doc = DocManager.Inst;
            var threadField = typeof(DocManager).GetField("mainThread",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var previousThread = threadField.GetValue(doc);
            var previousProject = doc.Project;
            var previousSink = doc.CommandSink;
            bool previousDetach = Preferences.Default.DetachPianoRoll;
            MainWindow? main = null;
            try {
                threadField.SetValue(doc, Thread.CurrentThread);
                doc.CommandSink = null;
                Preferences.Default.DetachPianoRoll = true;
                doc.SearchAllLegacyPlugins();
                main = new MainWindow();
                main.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(await main.ShowNoteEditorAsync());
                var first = DetachedWindow(main);
                Assert.NotNull(first);
                Assert.True(first!.IsVisible);

                // Its own close button only hides it, so the same window comes back.
                first.Hide();
                Assert.NotNull(await main.ShowNoteEditorAsync());
                Assert.Same(first, DetachedWindow(main));
                Assert.True(first.IsVisible);

                // A window that really closes must not be left behind: showing one again
                // throws "Cannot re-show a closed window."
                first.ForceClose();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(DetachedWindow(main));
                Assert.NotNull(await main.ShowNoteEditorAsync());
                var second = DetachedWindow(main);
                Assert.NotNull(second);
                Assert.NotSame(first, second);
                Assert.True(second!.IsVisible);
                second.ForceClose();
            } finally {
                Preferences.Default.DetachPianoRoll = previousDetach;
                main?.Close();
                doc.ExecuteCmd(new LoadProjectNotification(previousProject));
                doc.CommandSink = previousSink;
                threadField.SetValue(doc, previousThread);
            }
        }
    }
}
