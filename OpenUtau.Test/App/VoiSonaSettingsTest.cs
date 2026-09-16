#nullable enable
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaSettingsTest {
        [AvaloniaFact]
        public void NativeControlsAreVisibleBoundedAndCancelDoesNotMutateTrack() {
            var track = new UTrack { Singer = new VoiSonaSinger("ja_JP", "2.1.0", "/unused") };
            track.RendererSettings.renderer = Renderers.VOISONA;
            track.RendererSettings.Validate(track);
            var window = new TrackSettingsDialog(track);
            try {
                window.Show(); Dispatcher.UIThread.RunJobs();
                var vm = Assert.IsType<TrackSettingsViewModel>(window.DataContext);
                Assert.Equal(5, window.GetVisualDescendants().OfType<NumericUpDown>().Count(c => c.IsVisible));
                var accuracy = window.FindControl<NumericUpDown>("PitchAccuracy")!;
                Assert.False(accuracy.IsEnabled);
                window.FindControl<CheckBox>("NativePitch")!.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.NativePitch);
                Assert.True(accuracy.IsEnabled);
                var age = window.FindControl<NumericUpDown>("Age")!;
                age.Value = .4m;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(.4, vm.VoiSona.Age);
                Assert.Null(track.RendererSettings.voisona);
                var confirm = window.FindControl<Button>("ConfirmSettings")!;
                Assert.True(confirm.TranslatePoint(new Avalonia.Point(0, confirm.Bounds.Height), window)!.Value.Y <= window.Bounds.Height);
            } finally { window.Close(); }
            Assert.Null(track.RendererSettings.voisona);
        }
    }
}
