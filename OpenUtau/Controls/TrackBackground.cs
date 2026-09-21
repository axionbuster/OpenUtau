using System;
using System.Linq;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        public static readonly StyledProperty<bool> Is31EdoProperty = AvaloniaProperty.Register<TrackBackground, bool>(nameof(Is31Edo));
        public bool Is31Edo { get => GetValue(Is31EdoProperty); set => SetValue(Is31EdoProperty, value); }
        public static readonly StyledProperty<int[]?> DisplayRowsProperty = AvaloniaProperty.Register<TrackBackground, int[]?>(nameof(DisplayRows));
        public int[]? DisplayRows { get => GetValue(DisplayRowsProperty); set => SetValue(DisplayRowsProperty, value); }
        public static readonly StyledProperty<bool> FoldMajor31Property = AvaloniaProperty.Register<TrackBackground, bool>(nameof(FoldMajor31));
        public bool FoldMajor31 { get => GetValue(FoldMajor31Property); set => SetValue(FoldMajor31Property, value); }
        public static readonly StyledProperty<bool> FoldMinor31Property = AvaloniaProperty.Register<TrackBackground, bool>(nameof(FoldMinor31));
        public bool FoldMinor31 { get => GetValue(FoldMinor31Property); set => SetValue(FoldMinor31Property, value); }
        public static readonly StyledProperty<bool> HasPinnedChordTrackProperty =
            AvaloniaProperty.Register<TrackBackground, bool>(nameof(HasPinnedChordTrack));
        public bool HasPinnedChordTrack { get => GetValue(HasPinnedChordTrackProperty); set => SetValue(HasPinnedChordTrackProperty, value); }
        public static readonly StyledProperty<double> TickWidthProperty = AvaloniaProperty.Register<TrackBackground, double>(nameof(TickWidth));
        public double TickWidth { get => GetValue(TickWidthProperty); set => SetValue(TickWidthProperty, value); }
        public static readonly StyledProperty<double> TickOffsetProperty = AvaloniaProperty.Register<TrackBackground, double>(nameof(TickOffset));
        public double TickOffset { get => GetValue(TickOffsetProperty); set => SetValue(TickOffsetProperty, value); }
        public static readonly StyledProperty<int> TickOriginProperty = AvaloniaProperty.Register<TrackBackground, int>(nameof(TickOrigin));
        public int TickOrigin { get => GetValue(TickOriginProperty); set => SetValue(TickOriginProperty, value); }
        // One continuous tonic-relative hue circle for all 31 pitches, including folded views.
        // Equal OKLCH lightness/chroma (0.82/0.075), hues spaced 360/31 degrees apart.
        // Interval labels and tonic boundaries carry meaning independently of hue.
        static readonly IBrush ChromaticDegreeBrush = new SolidColorBrush(Color.Parse("#484848"));
        public static readonly DirectProperty<TrackBackground, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackBackground, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TrackBackground, bool> IsKeyboardProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsKeyboard),
                o => o.IsKeyboard,
                (o, v) => o.IsKeyboard = v);
        public static readonly DirectProperty<TrackBackground, int> KeyProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, int>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);

        public double TrackHeight {
            get => _trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref _trackHeight, value);
        }
        public double TrackOffset {
            get => _trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref _trackOffset, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public bool IsKeyboard {
            get => _isKeyboard;
            set => SetAndRaise(IsPianoRollProperty, ref _isKeyboard, value);
        }
        public int Key {
            get => _key;
            set => SetAndRaise(KeyProperty, ref _key, value);
        }

        private double _trackHeight;
        private double _trackOffset;
        private bool _isPianoRoll;
        private bool _isKeyboard;
        private int _key;

        public TrackBackground() {
            MessageBus.Current.Listen<OpenUtau.App.ViewModels.Spelling31ChangedEvent>().Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
            ToolTip.SetShowDelay(this, 250);
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            UpdateScaleToolTip(e.GetPosition(this).Y);
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }

        void UpdateScaleToolTip(double y) {
            if (!IsPianoRoll || !IsKeyboard || !Is31Edo || TrackHeight <= 0) {
                ToolTip.SetTip(this, null);
                return;
            }
            int track = (int)Math.Floor(TrackOffset + y / TrackHeight);
            int rowCount = DisplayRows?.Length ?? Edo31.MaxStep;
            int row = rowCount - 1 - track;
            if (row < 0 || row >= rowCount) {
                ToolTip.SetTip(this, null);
                return;
            }
            int step = DisplayRows == null ? row : DisplayRows[row];
            int relativeStep = Edo31.ScaleColorIndex(step, Key);
            string unit = relativeStep == 1 ? "step" : "steps";
            ToolTip.SetTip(this, $"{relativeStep} {unit}");
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DisplayRowsProperty || change.Property == Is31EdoProperty ||
                change.Property == FoldMajor31Property || change.Property == FoldMinor31Property ||
                change.Property == HasPinnedChordTrackProperty ||
                change.Property == TickWidthProperty || change.Property == TickOffsetProperty || change.Property == TickOriginProperty ||
                change.Property == TrackHeightProperty ||
                change.Property == TrackOffsetProperty ||
                change.Property == ForegroundProperty ||
                change.Property == KeyProperty) {
                InvalidateVisual();
            }
        }

        int mod(int a, int b){
            return (a % b + b) % b;
        }

        public override void Render(DrawingContext context) {
            if (!IsPianoRoll || IsKeyboard || TickWidth <= 0) {
                RenderRows(context, new UKeySignature { key = Key, major = FoldMajor31, minor = FoldMinor31 });
                return;
            }
            var timeline = DocManager.Inst.Project.KeyTimeline().ToArray();
            for (int i = 0; i < timeline.Length; i++) {
                var signature = timeline[i];
                double start = (signature.position - TickOrigin - TickOffset) * TickWidth;
                double end = i + 1 < timeline.Length
                    ? (timeline[i + 1].position - TickOrigin - TickOffset) * TickWidth : Bounds.Width;
                double left = Math.Max(0, start), right = Math.Min(Bounds.Width, end);
                if (right <= left) continue;
                using (context.PushClip(new Rect(left, 0, right - left, Bounds.Height))) {
                    RenderRows(context, signature);
                    // The key name itself lives in the ruler's key lane.
                    if (i > 0 && start >= 0) {
                        context.DrawLine(new Pen(Brushes.Gray, 1.5), new Point(start, 0), new Point(start, Bounds.Height));
                    }
                }
            }
        }

        void RenderRows(DrawingContext context, UKeySignature signature) {
            int key = signature.key;
            if (TrackHeight == 0) {
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
            if (HasPinnedChordTrack && !IsPianoRoll) {
                double chordHeight = OpenUtau.App.ViewModels.TrackLayout.ChordHeight(TrackHeight);
                context.DrawRectangle(Background, null, new Rect(0, 0, Bounds.Width, chordHeight));
                context.DrawLine(new Pen(Brushes.Gray, 0.5),
                    new Point(0, chordHeight), new Point(Bounds.Width, chordHeight));
                track++;
                top += chordHeight;
            }
            // Draw oversized labels after all row fills so neighboring rows cannot erase them.
            var perfectLabels = IsPianoRoll && Is31Edo && IsKeyboard && TrackHeight < 12
                ? new List<(int Degree, double CenterY)>() : null;
            string[] degreeNames;
            switch(Preferences.Default.DegreeStyle){
                case 1:
                    degreeNames = MusicMath.Solfeges;
                    break;
                case 2:
                    degreeNames = MusicMath.NumberedNotations;
                    break;
                default:
                    degreeNames = Enumerable.Repeat("", 12).ToArray();
                    break;
            }
            while (top < Bounds.Height) {
                if (IsPianoRoll && Is31Edo) {
                    int row = (DisplayRows?.Length ?? Edo31.MaxStep) - 1 - track;
                    if (row < 0 || row >= (DisplayRows?.Length ?? Edo31.MaxStep)) { break; }
                    int step = DisplayRows == null ? row : DisplayRows[row];
                    int colorIndex = Edo31.ScaleColorIndex(step, key);
                    var color = DegreeColorPalette.Brushes[colorIndex];
                    context.DrawRectangle(IsKeyboard ? color : Background, null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
                    if (!IsKeyboard) {
                        // Tonic emphasis is permanent and survives the minimum eight-pixel row height.
                        using (context.PushOpacity(colorIndex == 0 ? 0.28 : signature.Contains(step, true) ? 0.20 : 0.06)) {
                            context.DrawRectangle(color, null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
                        }
                        if (colorIndex is 13 or 18) {
                            using (context.PushOpacity(0.03)) {
                                context.DrawRectangle(ThemeManager.IsDarkMode ? Brushes.White : Brushes.Black,
                                    null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
                            }
                        }
                    }
                    context.DrawLine(new Pen(Brushes.Gray, 0.5), new Point(0, (int)top), new Point(Bounds.Width, (int)top));
                    if (colorIndex == 0) {
                        // Underline the tonic row, including when labels disappear at minimum zoom.
                        var contrast = ThemeManager.IsDarkMode ? Brushes.White : Brushes.Black;
                        if (!IsKeyboard) {
                            using (context.PushOpacity(0.07)) {
                                context.DrawRectangle(contrast, null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
                            }
                        }
                        using (context.PushOpacity(IsKeyboard ? 1 : 0.35)) {
                            var tonicPen = new Pen(IsKeyboard ? Brushes.Black : contrast, 1);
                            context.DrawLine(tonicPen, new Point(0, top + TrackHeight - 1), new Point(Bounds.Width, top + TrackHeight - 1));
                            if (IsKeyboard) {
                                context.DrawLine(tonicPen, new Point(0, top + TrackHeight - 3), new Point(Bounds.Width, top + TrackHeight - 3));
                            }
                        }
                    }
                    if (perfectLabels != null && colorIndex is 0 or 13 or 18) {
                        perfectLabels.Add((colorIndex, top + TrackHeight / 2));
                    }
                    if (IsPianoRoll && !IsKeyboard && signature.Contains(ViewConstants.MaxTone - 1 - track, false)) {
                    using (context.PushOpacity(0.10)) {
                        context.DrawRectangle(ThemeManager.IsDarkMode ? Brushes.White : Brushes.Black, null,
                            new Rect(0, (int)top, Bounds.Width, TrackHeight));
                    }
                }
                if (IsKeyboard && TrackHeight >= 12) {
                        bool isFoldedScale = signature.major || signature.minor;
                        bool isScaleDegree = signature.Contains(step, true);
                        string degreeText = Edo31.ScaleDegreeLabel(step, key);
                        var degree = TextLayoutCache.Get(
                            degreeText,
                            isScaleDegree ? Brushes.Black : ChromaticDegreeBrush, isScaleDegree ? 12 : 10,
                            bold: colorIndex == 0,
                            letterSpacing: TextLayoutCache.CompactAccidentalLetterSpacing(degreeText));
                        degree.Draw(context, new Point(4, top + (TrackHeight - degree.Height) / 2));
                        bool isSelectedScaleDegree = Edo31.IsDegreeInSelectedScales(
                            colorIndex, signature.major, signature.minor);
                        string labelText = Edo31.Name(step, key);
                        var label = TextLayoutCache.Get(
                            labelText,
                            Brushes.Black, 12, bold: colorIndex == 0,
                            italic: isFoldedScale && !isSelectedScaleDegree,
                            letterSpacing: TextLayoutCache.CompactAccidentalLetterSpacing(labelText));
                        label.Draw(context, new Point(Bounds.Width - 4 - label.Width, top + (TrackHeight - label.Height) / 2));
                    }
                    track++;
                    top += TrackHeight;
                    continue;
                }
                bool isAltTrack = IsAltTrack(track) ^ (ThemeManager.IsDarkMode && !IsKeyboard);
                bool isCenterKey = IsKeyboard && IsCenterKey(track);
                var brush = isCenterKey ? ThemeManager.CenterKeyBrush
                    : IsKeyboard ? (isAltTrack ? ThemeManager.BlackKeyBrush : ThemeManager.WhiteKeyBrush)
                    : isAltTrack ? Foreground : Background;
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(0, (int)top, Bounds.Width, TrackHeight));
                if (IsPianoRoll && !IsKeyboard && signature.Contains(ViewConstants.MaxTone - 1 - track, false)) {
                    using (context.PushOpacity(0.10)) {
                        context.DrawRectangle(ThemeManager.IsDarkMode ? Brushes.White : Brushes.Black, null,
                            new Rect(0, (int)top, Bounds.Width, TrackHeight));
                    }
                }
                if (IsKeyboard && TrackHeight >= 12) {
                    brush = isCenterKey ? ThemeManager.CenterKeyNameBrush
                        : isAltTrack ? ThemeManager.BlackKeyNameBrush
                            : ThemeManager.WhiteKeyNameBrush;
                    int tone = ViewConstants.MaxTone - 1 - track;
                    string toneName = MusicMath.GetToneName(tone);
                    var toneTextLayout = TextLayoutCache.Get(toneName, brush, 12);
                    var toneTextPosition = new Point(Bounds.Width - 4 - (int)toneTextLayout.Width, (int)(top + (TrackHeight - toneTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(toneTextPosition))) {
                        toneTextLayout.Draw(context, new Point());
                    }
                    //scale degree display
                    int degree = mod(tone - key, 12);
                    string degreeName = degreeNames[degree];
                    var degreeTextLayout = TextLayoutCache.Get(degreeName, brush, 12);
                    var degreeTextPosition = new Point(4, (int)(top + (TrackHeight - degreeTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(degreeTextPosition))) {
                        degreeTextLayout.Draw(context, new Point());
                    }
                }
                track++;
                top += TrackHeight;
            }
            if (perfectLabels != null) {
                using (context.PushClip(new Rect(Bounds.Size))) {
                    foreach (var (degree, centerY) in perfectLabels) {
                        string text = degree == 0 ? "1" : degree == 13 ? "4" : "5";
                        var label = TextLayoutCache.Get(text, Brushes.Black, 18, bold: true);
                        // Separate columns keep adjacent fourth/fifth rows readable in folded views.
                        double x = degree == 0 ? 8 : degree == 13 ? (Bounds.Width - label.Width) / 2
                            : Bounds.Width - 8 - label.Width;
                        label.Draw(context, new Point(x, centerY - label.Height / 2));
                    }
                }
            }
        }

        private bool IsAltTrack(int track) {
            if (!IsPianoRoll) {
                return track % 2 == 1;
            }
            int tone = ViewConstants.MaxTone - 1 - track;
            if (tone < 0) {
                return false;
            }
            return MusicMath.IsBlackKey(tone);
        }

        private bool IsCenterKey(int track) {
            int tone = ViewConstants.MaxTone - 1 - track;
            return MusicMath.IsCenterKey(tone);
        }
    }
}
