using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class TickBackground : TemplatedControl {
        private static readonly IDashStyle DashStyle = new ImmutableDashStyle(new double[] { 2, 4 }, 0);

        public static readonly DirectProperty<TickBackground, int> ResolutionProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(Resolution),
                o => o.Resolution,
                (o, v) => o.Resolution = v);
        public static readonly DirectProperty<TickBackground, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<TickBackground, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<TickBackground, int> TickOriginProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(TickOrigin),
                o => o.TickOrigin,
                (o, v) => o.TickOrigin = v);
        public static readonly DirectProperty<TickBackground, int> SnapDivProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(SnapDiv),
                o => o.SnapDiv,
                (o, v) => o.SnapDiv = v);
        public static readonly DirectProperty<TickBackground, ObservableCollection<int>?> SnapTicksProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, ObservableCollection<int>?>(
                nameof(SnapTicks),
                o => o.SnapTicks,
                (o, v) => o.SnapTicks = v);
        public static readonly DirectProperty<TickBackground, bool> ShowBarProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, bool>(
                nameof(ShowBar),
                o => o.ShowBar,
                (o, v) => o.ShowBar = v);
        public static readonly DirectProperty<TickBackground, bool> ShowChordsProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, bool>(
                nameof(ShowChords),
                o => o.ShowChords,
                (o, v) => o.ShowChords = v);

        public int Resolution {
            get => _resolution;
            private set => SetAndRaise(ResolutionProperty, ref _resolution, value);
        }
        // Tick width in pixel.
        public double TickWidth {
            get => _tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref _tickWidth, value);
        }
        public double TickOffset {
            get => _tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref _tickOffset, value);
        }
        public int TickOrigin {
            get => _tickOrigin;
            private set => SetAndRaise(TickOriginProperty, ref _tickOrigin, value);
        }
        public int SnapDiv {
            get => _snapDiv;
            set => SetAndRaise(SnapDivProperty, ref _snapDiv, value);
        }
        public ObservableCollection<int>? SnapTicks {
            get => _snapTicks;
            set => SetAndRaise(SnapTicksProperty, ref _snapTicks, value);
        }
        public bool ShowBar {
            get => _showBar;
            set => SetAndRaise(ShowBarProperty, ref _showBar, value);
        }
        /// <summary>Draws the key-signature and chord lanes under the bar ruler.</summary>
        public bool ShowChords {
            get => _showChords;
            set => SetAndRaise(ShowChordsProperty, ref _showChords, value);
        }

        private int _resolution = 480;
        private double _tickWidth;
        private double _tickOffset;
        private int _tickOrigin;
        private int _snapDiv;
        private ObservableCollection<int>? _snapTicks;
        private bool _showBar = true;
        private bool _showChords;

        private Pen penBar;
        private Pen penBeatUnit;
        private Pen penDanshed;

        public TickBackground() {
            penBar = new Pen(Foreground, 1);
            penBeatUnit = new Pen(Background, 1);
            penDanshed = new Pen(Background, 1) {
                DashStyle = DashStyle,
            };
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
            MessageBus.Current.Listen<TimeAxisChangedEvent>()
                .Subscribe(e => InvalidateVisual());
            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(e => { if (ShowChords) InvalidateVisual(); });
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ForegroundProperty) {
                penBar = new Pen(Foreground, 1);
            }
            if (change.Property == BackgroundProperty) {
                penBeatUnit = new Pen(Background, 1);
                penDanshed = new Pen(Background, 1) {
                    DashStyle = DashStyle,
                };
            }
            if (change.Property == ResolutionProperty ||
                change.Property == TickOriginProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == SnapDivProperty ||
                change.Property == ShowBarProperty ||
                change.Property == ShowChordsProperty) {
                InvalidateVisual();
            }
        }

        // Bar numbers, tempo and time signature occupy the top block; the key and
        // chord lanes stack under it when this ruler carries them.
        private const double BarLaneHeight = 24;
        private const double KeyLaneHeight = 14;
        private const double ChordLaneHeight = 16;
        public const double HeaderHeightWithChords = BarLaneHeight + KeyLaneHeight + ChordLaneHeight;
        private double HeaderHeight => !ShowBar ? 0 : ShowChords ? HeaderHeightWithChords : BarLaneHeight;

        public override void Render(DrawingContext context) {
            if (TickWidth <= 0) {
                return;
            }
            var project = Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / SnapDiv;
            while (snapUnit * TickWidth < ViewConstants.MinTicklineWidth) {
                snapUnit *= 2; // Avoid drawing too dense.
            }
            double minLineTick = ViewConstants.MinTicklineWidth / TickWidth;
            double pixelOffset = (TickOffset + TickOrigin) * TickWidth;
            double leftTick = TickOffset + TickOrigin;
            double rightTick = TickOffset + TickOrigin + Bounds.Width / TickWidth;

            project.timeAxis.TickPosToBarBeat(TickOrigin, out int bar, out int beat, out int remainingTicks);
            if (bar > 0) {
                bar--;
            }
            int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);
            SnapTicks?.Clear();
            while (barTick <= rightTick) {
                SnapTicks?.Add(barTick);
                double x = Math.Round(barTick * TickWidth - pixelOffset) + 0.5;
                double y = -0.5;
                if (ShowBar) {
                    var textLayout = TextLayoutCache.Get((bar + 1).ToString(), ThemeManager.BarNumberBrush, 10);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 10))) {
                        textLayout.Draw(context, new Point());
                    }
                }
                context.DrawLine(penBar, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                // Lines between bars.
                var timeSig = project.timeAxis.TimeSignatureAtBar(bar);
                int nextBarTick = project.timeAxis.BarBeatToTickPos(bar + 1, 0);
                int ticksPerBeat = project.resolution * 4 * timeSig.beatPerBar / timeSig.beatUnit;
                int ticksPerLine = snapUnit;
                if (ticksPerBeat < snapUnit) {
                    ticksPerLine = ticksPerBeat;
                } else if (ticksPerBeat % snapUnit != 0) {
                    if (ticksPerBeat > minLineTick) {
                        ticksPerLine = ticksPerBeat;
                    } else {
                        ticksPerLine = nextBarTick - barTick;
                    }
                }
                if (nextBarTick > leftTick) {
                    for (int tick = barTick + ticksPerLine; tick < nextBarTick; tick += ticksPerLine) {
                        SnapTicks?.Add(tick);
                        project.timeAxis.TickPosToBarBeat(tick, out int snapBar, out int snapBeat, out int snapRemainingTicks);
                        var pen = snapRemainingTicks != 0 ? penDanshed : penBeatUnit;
                        x = Math.Round(tick * TickWidth - pixelOffset) + 0.5;
                        y = HeaderHeight;
                        context.DrawLine(pen, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                    }
                }
                barTick = nextBarTick;
                bar++;
            }
            SnapTicks?.Add(barTick);

            if (ShowBar) {
                foreach (var tempo in project.tempos) {
                    double x = Math.Round(tempo.position * TickWidth - pixelOffset) + 0.5;
                    context.DrawLine(penDanshed, new Point(x, 0), new Point(x, BarLaneHeight));
                    var textLayout = TextLayoutCache.Get(tempo.bpm.ToString("#0.00"), ThemeManager.BarNumberBrush, 10);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 0))) {
                        textLayout.Draw(context, new Point());
                    }
                }

                foreach (var timeSig in project.timeSignatures) {
                    int tick = project.timeAxis.BarBeatToTickPos(timeSig.barPosition, 0);
                    var barTextLayout = TextLayoutCache.Get((timeSig.barPosition + 1).ToString(), ThemeManager.BarNumberBrush, 10);
                    double x = Math.Round(tick * TickWidth - pixelOffset) + 0.5 + barTextLayout.Width + 4;
                    var textLayout = TextLayoutCache.Get($"{timeSig.beatPerBar}/{timeSig.beatUnit}", ThemeManager.BarNumberBrush, 10);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 10))) {
                        textLayout.Draw(context, new Point());
                    }
                }
            }

            if (ShowBar && ShowChords) {
                RenderKeyLane(context, project, pixelOffset);
                RenderChordLane(context, project, pixelOffset, leftTick, rightTick);
            }
        }

        /// <summary>
        /// Key sections as a banded lane. The name sticks to the left edge so the
        /// key in force stays readable however far the view has scrolled.
        /// </summary>
        void RenderKeyLane(DrawingContext context, UProject project, double pixelOffset) {
            var timeline = project.KeyTimeline().ToArray();
            var band = ThemeManager.IsDarkMode
                ? new ImmutableSolidColorBrush(Color.FromArgb(38, 255, 255, 255))
                : new ImmutableSolidColorBrush(Color.FromArgb(28, 0, 0, 0));
            var alternateBand = ThemeManager.IsDarkMode
                ? new ImmutableSolidColorBrush(Color.FromArgb(68, 255, 255, 255))
                : new ImmutableSolidColorBrush(Color.FromArgb(52, 0, 0, 0));
            var divider = new Pen(ThemeManager.BarNumberBrush, 1);
            for (int i = 0; i < timeline.Length; i++) {
                double start = timeline[i].position * TickWidth - pixelOffset;
                double end = i + 1 < timeline.Length
                    ? timeline[i + 1].position * TickWidth - pixelOffset
                    : Bounds.Width;
                double left = Math.Max(0, start);
                double right = Math.Min(Bounds.Width, end);
                if (right <= left) {
                    continue;
                }
                context.DrawRectangle(i % 2 == 0 ? band : alternateBand, null,
                    new Rect(left, BarLaneHeight, right - left, KeyLaneHeight));
                if (i > 0 && start >= 0) {
                    context.DrawLine(divider, new Point(start, BarLaneHeight),
                        new Point(start, BarLaneHeight + KeyLaneHeight));
                }
                var label = TextLayoutCache.Get(timeline[i].Label(project.Is31Edo),
                    ThemeManager.BarNumberBrush, 10);
                if (label.Width + 8 > right - left) {
                    continue;
                }
                using var state = context.PushTransform(
                    Matrix.CreateTranslation(left + 4, BarLaneHeight + (KeyLaneHeight - label.Height) / 2));
                label.Draw(context, new Point());
            }
        }

        /// <summary>Chord names laid out over the span each chord covers.</summary>
        void RenderChordLane(DrawingContext context, UProject project,
                double pixelOffset, double leftTick, double rightTick) {
            var chordTrack = project.tracks.FirstOrDefault(track => track.IsChordsTrack);
            var accent = chordTrack != null
                ? ThemeManager.GetTrackColor(chordTrack.TrackColor).AccentColor.Color
                : Color.Parse("#35A7D8");
            var fill = new ImmutableSolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B));
            var border = new Pen(new ImmutableSolidColorBrush(
                Color.FromArgb(220, accent.R, accent.G, accent.B)), 1);
            foreach (var (_, _, helper, absoluteStart, absoluteEnd) in
                    ChordHelperViewModel.VisibleOccurrences(project,
                        (int)Math.Max(0, leftTick - 480), (int)(rightTick + 480))) {
                if (helper.tones.Count == 0) {
                    continue;
                }
                double start = absoluteStart * TickWidth - pixelOffset;
                double end = absoluteEnd * TickWidth - pixelOffset;
                double left = Math.Max(-2, start);
                double right = Math.Min(Bounds.Width + 2, end);
                if (right - left < 2) {
                    continue;
                }
                var rect = new Rect(left + 0.5, BarLaneHeight + KeyLaneHeight + 1.5,
                    right - left - 1, ChordLaneHeight - 3);
                context.DrawRectangle(fill, border, rect, 2, 2);
                var label = TextLayoutCache.Get(
                    ChordHelperTheory.ChordName(helper, project.Is31Edo, project.KeyAt(absoluteStart).key),
                    ThemeManager.BarNumberBrush, 10);
                if (label.Width + 6 > rect.Width) {
                    continue;
                }
                using var state = context.PushTransform(Matrix.CreateTranslation(
                    Math.Max(rect.X + 3, 3), rect.Y + (rect.Height - label.Height) / 2));
                label.Draw(context, new Point());
            }
        }
    }
}
