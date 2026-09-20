using System;

namespace OpenUtau.App.ViewModels {
    public static class TrackLayout {
        public static double Top(int trackNo, double ordinaryOffset, double trackHeight) =>
            trackNo == 0 ? 0 : trackHeight + (trackNo - 1 - ordinaryOffset) * trackHeight;

        public static int TrackNoAt(double y, double ordinaryOffset, double trackHeight) {
            if (trackHeight <= 0 || y < trackHeight) return 0;
            return 1 + (int)Math.Floor(ordinaryOffset + (y - trackHeight) / trackHeight);
        }
    }
}
