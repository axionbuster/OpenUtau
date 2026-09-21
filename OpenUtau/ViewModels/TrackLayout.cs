using System;

namespace OpenUtau.App.ViewModels {
    public static class TrackLayout {
        // The chord row follows the vertical scaler like every other track. Its floor
        // sits below the ordinary track minimum so that shrinking the view keeps
        // shrinking it instead of stalling at the same height as a voice track.
        public static double ChordHeight(double trackHeight) =>
            Math.Clamp(trackHeight * ViewConstants.ChordTrackHeightRatio,
                ViewConstants.ChordTrackHeightMin, ViewConstants.TrackHeightDefault);

        public static double Top(int trackNo, double ordinaryOffset, double trackHeight) =>
            trackNo == 0 ? 0 : ChordHeight(trackHeight) + (trackNo - 1 - ordinaryOffset) * trackHeight;

        public static int TrackNoAt(double y, double ordinaryOffset, double trackHeight) {
            double chordHeight = ChordHeight(trackHeight);
            if (trackHeight <= 0 || y < chordHeight) return 0;
            return 1 + (int)Math.Floor(ordinaryOffset + (y - chordHeight) / trackHeight);
        }
    }
}
