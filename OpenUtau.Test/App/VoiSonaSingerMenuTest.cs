#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaSingerMenuTest {
        [AvaloniaFact]
        public async Task TrackSingerButtonOpensMenuContainingBothVoices() {
            var manager = SingerManager.Inst;
            var oldSingers = manager.Singers.ToArray();
            var oldGroups = manager.SingerGroups.ToArray();
            var jp = new VoiSonaSinger("ja_JP", "2.1.0", "/unused");
            var en = new VoiSonaSinger("en_US", "2.2.0 Cross-Lingual", "/unused");
            var project = Core.Format.Ustx.Create();
            var oldProject = DocManager.Inst.TakeProjectForTest(project);
            var oldSink = DocManager.Inst.CommandSink;
            DocManager.Inst.CommandSink = _ => {};
            Window? window = null;
            TrackHeader? header = null;
            try {
                manager.Singers.Clear(); manager.SingerGroups.Clear();
                manager.Singers.Add(jp.Id, jp); manager.Singers.Add(en.Id, en);
                manager.SingerGroups.Add(USingerType.VoiSona, new() {jp, en});
                var track = project.tracks.First(candidate => !candidate.IsChordsTrack); track.Singer = jp;
                var vm = new TrackHeaderViewModel(track);
                header = new TrackHeader {DataContext = vm, ViewModel = vm, TrackHeight = 104};
                window = new Window {Width = 500, Height = 300, Content = header};
                window.Show(); Dispatcher.UIThread.RunJobs();
                TrackHeaderViewModel.InvalidateSingerMenuCache();
                var button = header.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Content, jp) && b.IsVisible);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(button.ContextMenu);
                Assert.Same(header.Resources["SingersMenuRes"], button.ContextMenu);
                Assert.True(button.ContextMenu!.IsOpen);
                Assert.True(button.ContextMenu.Items.Count > 0);
                var group = vm.SingerMenuItems!.Single(item => item.Header == "VoiSona ...");
                Assert.Contains(group.Items!, item => item.CommandParameter == jp);
                Assert.Contains(group.Items!, item => item.CommandParameter == en);
                var groupIndex = vm.SingerMenuItems.ToList().IndexOf(group);
                var groupControl = Assert.IsType<MenuItem>(button.ContextMenu.ContainerFromIndex(groupIndex));
                Assert.Equal("VoiSona ...", groupControl.Header);
                Assert.True(groupControl.Bounds.Width > 0);
                Assert.True(groupControl.Bounds.Height > 0);
                button.ContextMenu.Close();
            } finally {
                if (header?.Resources["SingersMenuRes"] is ContextMenu menu) {
                    foreach (var container in menu.GetRealizedContainers().OfType<MenuItem>()) container.IsSubMenuOpen = false;
                    menu.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                window?.Close(); header?.Dispose();
                manager.Singers.Clear(); manager.SingerGroups.Clear();
                foreach (var item in oldSingers) manager.Singers.Add(item.Key, item.Value);
                foreach (var item in oldGroups) manager.SingerGroups.Add(item.Key, item.Value);
                DocManager.Inst.TakeProjectForTest(oldProject);
                DocManager.Inst.CommandSink = oldSink;
                TrackHeaderViewModel.InvalidateSingerMenuCache();
            }
        }
    }
}
