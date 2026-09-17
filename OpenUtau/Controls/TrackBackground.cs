using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        public static readonly StyledProperty<bool> Is31EdoProperty = AvaloniaProperty.Register<TrackBackground, bool>(nameof(Is31Edo));
        public bool Is31Edo { get => GetValue(Is31EdoProperty); set => SetValue(Is31EdoProperty, value); }
        public static readonly StyledProperty<int[]?> DisplayRowsProperty = AvaloniaProperty.Register<TrackBackground, int[]?>(nameof(DisplayRows));
        public int[]? DisplayRows { get => GetValue(DisplayRowsProperty); set => SetValue(DisplayRowsProperty, value); }
        // One continuous tonic-relative hue circle for all 31 pitches, including folded views.
        // Equal OKLCH lightness/chroma (0.82/0.075), hues spaced 360/31 degrees apart.
        // Degree/step labels and tonic boundaries carry meaning independently of hue.
        static readonly IBrush ChromaticDegreeBrush = new SolidColorBrush(Color.Parse("#484848"));
        static readonly IBrush[] MicrotoneBrushes = {
            new SolidColorBrush(Color.Parse("#A0C8F4")),
            new SolidColorBrush(Color.Parse("#A9C5F6")),
            new SolidColorBrush(Color.Parse("#B4C1F6")),
            new SolidColorBrush(Color.Parse("#BEBEF4")),
            new SolidColorBrush(Color.Parse("#C8BBF0")),
            new SolidColorBrush(Color.Parse("#D2B8EA")),
            new SolidColorBrush(Color.Parse("#DAB5E3")),
            new SolidColorBrush(Color.Parse("#E1B3DB")),
            new SolidColorBrush(Color.Parse("#E7B1D1")),
            new SolidColorBrush(Color.Parse("#ECB0C7")),
            new SolidColorBrush(Color.Parse("#EFB0BC")),
            new SolidColorBrush(Color.Parse("#F1B1B2")),
            new SolidColorBrush(Color.Parse("#F1B2A8")),
            new SolidColorBrush(Color.Parse("#EFB49F")),
            new SolidColorBrush(Color.Parse("#ECB797")),
            new SolidColorBrush(Color.Parse("#E7BA91")),
            new SolidColorBrush(Color.Parse("#E1BE8D")),
            new SolidColorBrush(Color.Parse("#D9C28C")),
            new SolidColorBrush(Color.Parse("#D0C68D")),
            new SolidColorBrush(Color.Parse("#C6C991")),
            new SolidColorBrush(Color.Parse("#BBCC96")),
            new SolidColorBrush(Color.Parse("#B1CF9E")),
            new SolidColorBrush(Color.Parse("#A6D2A7")),
            new SolidColorBrush(Color.Parse("#9CD3B1")),
            new SolidColorBrush(Color.Parse("#93D4BB")),
            new SolidColorBrush(Color.Parse("#8DD5C6")),
            new SolidColorBrush(Color.Parse("#89D4D0")),
            new SolidColorBrush(Color.Parse("#88D3DA")),
            new SolidColorBrush(Color.Parse("#8AD1E3")),
            new SolidColorBrush(Color.Parse("#8FCFEA")),
            new SolidColorBrush(Color.Parse("#97CCF0")),
        };
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
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DisplayRowsProperty || change.Property == Is31EdoProperty ||
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
            if (TrackHeight == 0) {
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
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
                    int colorIndex = Edo31.ScaleColorIndex(step, Preferences.Default.PreferredKey31Fifths);
                    var color = MicrotoneBrushes[colorIndex];
                    context.DrawRectangle(IsKeyboard ? color : Background, null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
                    if (!IsKeyboard) {
                        // Tonic emphasis is permanent and survives the minimum eight-pixel row height.
                        using (context.PushOpacity(colorIndex == 0 ? 0.28 : 0.12)) {
                            context.DrawRectangle(color, null, new Rect(0, (int)top, Bounds.Width, TrackHeight));
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
                    if (IsKeyboard && TrackHeight >= 12) {
                        bool isScaleDegree = Edo31.IsMajorDegree(colorIndex) || Edo31.IsMinorDegree(colorIndex) ||
                            colorIndex == Edo31.HarmonicSeventh;
                        var degree = TextLayoutCache.Get(
                            Edo31.ScaleDegreeLabel(step, Preferences.Default.PreferredKey31Fifths),
                            isScaleDegree ? Brushes.Black : ChromaticDegreeBrush, isScaleDegree ? 12 : 10, bold: colorIndex == 0);
                        degree.Draw(context, new Point(4, top + (TrackHeight - degree.Height) / 2));
                        var label = TextLayoutCache.Get(Edo31.Name(step, Preferences.Default.PreferredKey31Fifths), Brushes.Black, 12, bold: colorIndex == 0);
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
                    int degree = mod(tone - Key, 12);
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
