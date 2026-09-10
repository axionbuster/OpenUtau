using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenUtau.App.Controls;
using Xunit;

namespace OpenUtau.App {
    public class BitmapLoaderTest {
        [AvaloniaFact]
        public void ScalingKeepsTheSourceAliveDuringGarbageCollection() {
            using var data = new MemoryStream();
            using (var image = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul)) {
                image.Save(data);
            }
            data.Position = 0;
            // Avalonia hides its locator from reference assemblies; use it only in
            // this scoped test to force collection at the native scaling boundary.
            var locator = typeof(AvaloniaLocator);
            var current = locator.GetProperty("Current")!.GetValue(null)!;
            var original = (IPlatformRenderInterface)current.GetType().GetMethod("GetService")!.Invoke(current, new object[] { typeof(IPlatformRenderInterface) })!;
            var proxy = DispatchProxy.Create<IPlatformRenderInterface, CollectingRenderProxy>();
            var collecting = (CollectingRenderProxy)(object)proxy;
            collecting.Original = original;
            using ((IDisposable)locator.GetMethod("EnterScope")!.Invoke(null, null)!) {
                var mutable = locator.GetProperty("CurrentMutable")!.GetValue(null)!;
                var registration = mutable.GetType().GetMethod("Bind")!.MakeGenericMethod(typeof(IPlatformRenderInterface)).Invoke(mutable, null)!;
                registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IPlatformRenderInterface)).Invoke(registration, new object[] { proxy });
                using var scaled = BitmapLoader.LoadScaled(data, new PixelSize(100, 100));
                Assert.Equal(new PixelSize(100, 100), scaled.PixelSize);
                Assert.True(collecting.Checked);
            }
        }

        public class CollectingRenderProxy : DispatchProxy {
            public IPlatformRenderInterface Original = null!;
            public bool Checked;
            protected override object? Invoke(MethodInfo? method, object?[]? args) {
                if (method!.Name == "ResizeBitmap") {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    // Guard the real Skia call so a regression fails as an assertion,
                    // rather than bringing down the test process with a native SIGSEGV.
                    var image = args![0]!.GetType().GetField("_image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(args[0])!;
                    var handle = (IntPtr)image.GetType().GetProperty("Handle")!.GetValue(image)!;
                    Assert.NotEqual(IntPtr.Zero, handle);
                    Checked = true;
                }
                return method.Invoke(Original, args);
            }
        }
    }
}
