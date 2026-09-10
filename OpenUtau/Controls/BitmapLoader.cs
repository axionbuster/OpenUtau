using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace OpenUtau.App.Controls {
    public static class BitmapLoader {
        public static Bitmap LoadScaled(Stream stream, PixelSize size) {
            // Keep the owning bitmap alive until Skia finishes scaling. Passing a
            // temporary lets Avalonia's reference finalizer release its native image.
            using var source = new Bitmap(stream);
            return source.CreateScaledBitmap(size);
        }
    }
}
