using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class PartsCanvas : Canvas {
        public static readonly DirectProperty<PartsCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartsCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<PartsCanvas, ObservableCollection<UPart>?> ItemsProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, ObservableCollection<UPart>?>(
                nameof(Items),
                o => o.Items,
                (o, v) => o.Items = v);
        public static readonly DirectProperty<PartsCanvas, UPart?> PianoRollOpenPartProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, UPart?>(
                nameof(PianoRollOpenPart),
                o => o.PianoRollOpenPart,
                (o, v) => o.PianoRollOpenPart = v);
        public static readonly DirectProperty<PartsCanvas, double> PianoRollViewTickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(PianoRollViewTickOffset),
                o => o.PianoRollViewTickOffset,
                (o, v) => o.PianoRollViewTickOffset = v);
        public static readonly DirectProperty<PartsCanvas, double> PianoRollViewViewportTicksProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(PianoRollViewViewportTicks),
                o => o.PianoRollViewViewportTicks,
                (o, v) => o.PianoRollViewViewportTicks = v);

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public ObservableCollection<UPart>? Items {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
        public UPart? PianoRollOpenPart {
            get => _pianoRollOpenPart;
            set {
                if (SetAndRaise(PianoRollOpenPartProperty, ref _pianoRollOpenPart, value)) {
                    foreach (var control in partControls.Values) {
                        control.InvalidateVisual();
                    }
                }
            }
        }
        public double PianoRollViewTickOffset {
            get => _pianoRollViewTickOffset;
            set {
                if (SetAndRaise(PianoRollViewTickOffsetProperty, ref _pianoRollViewTickOffset, value)) {
                    InvalidatePartViewport();
                }
            }
        }
        public double PianoRollViewViewportTicks {
            get => _pianoRollViewViewportTicks;
            set {
                if (SetAndRaise(PianoRollViewViewportTicksProperty, ref _pianoRollViewViewportTicks, value)) {
                    InvalidatePartViewport();
                }
            }
        }

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private ObservableCollection<UPart>? _items;
        private UPart? _pianoRollOpenPart;
        private double _pianoRollViewTickOffset;
        private double _pianoRollViewViewportTicks;

        Dictionary<UPart, PartControl> partControls = new Dictionary<UPart, PartControl>();
        readonly Border pinnedLane = new Border {
            BorderThickness = new Thickness(0, 0, 0, 2),
            IsHitTestVisible = true,
        };

        public PartsCanvas() {
            Canvas.SetLeft(pinnedLane, 0);
            Canvas.SetTop(pinnedLane, 0);
            pinnedLane.SetValue(Panel.ZIndexProperty, 1500);
            pinnedLane.Bind(HeightProperty, this.GetObservable(TrackHeightProperty));
            pinnedLane.Bind(WidthProperty, this.WhenAnyValue(x => x.Bounds).Select(bounds => bounds.Width));
            RefreshPinnedLane();
            Children.Add(pinnedLane);
            MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => {
                    foreach (var (part, control) in partControls) {
                        control.SetPosition();
                    }
                });
            MessageBus.Current.Listen<PartsSelectionEvent>()
                .Subscribe(e => {
                    foreach (var (part, control) in partControls) {
                        control.Selected = e.selectedParts.Contains(part)
                            || e.tempSelectedParts.Contains(part);
                    }
                });
            MessageBus.Current.Listen<PartRefreshEvent>()
                .Subscribe(e => {
                    if (partControls.TryGetValue(e.part, out var control)) {
                        control.SetSize();
                        control.SetPosition();
                        control.Refersh();
                    }
                });
            MessageBus.Current.Listen<PartRedrawEvent>()
                .Subscribe(e => {
                    if (partControls.TryGetValue(e.part, out var control)) {
                        control.InvalidateVisual();
                    }
                });
            MessageBus.Current.Listen<TimeAxisChangedEvent>()
                .Subscribe(e => {
                    foreach (var (part, control) in partControls) {
                        control.InvalidateVisual();
                    }
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => { RefreshPinnedLane(); InvalidateVisual(); });
            MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => RefreshPinnedLane());
        }

        void RefreshPinnedLane() {
            var color = ThemeManager.GetTrackColor(Core.DocManager.Inst.Project.ChordsTrack.TrackColor);
            pinnedLane.Background = color.AccentColorLightSemi;
            pinnedLane.BorderBrush = ThemeManager.NeutralAccentBrush;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ItemsProperty) {
                if (change.OldValue != null && change.OldValue is ObservableCollection<UPart> oldCol) {
                    oldCol.CollectionChanged -= Items_CollectionChanged;
                }
                if (change.NewValue != null && change.NewValue is ObservableCollection<UPart> newCol) {
                    newCol.CollectionChanged += Items_CollectionChanged;
                    foreach (var part in partControls.Keys.Where(part => !newCol.Contains(part)).ToArray()) {
                        Remove(part);
                    }
                    foreach (var part in newCol) {
                        if (!partControls.ContainsKey(part)) {
                            Add(part);
                        }
                    }
                }
            }
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) {
                        foreach (var item in e.OldItems) {
                            if (item is UPart part) {
                                Remove(part);
                            }
                        }
                    }
                    if (e.NewItems != null) {
                        foreach (var item in e.NewItems) {
                            if (item is UPart part) {
                                Add(part);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var part in partControls.Keys.ToArray()) {
                        Remove(part);
                    }
                    if (Items != null) {
                        foreach (var part in Items) Add(part);
                    }
                    break;
            }
        }

        void Add(UPart part) {
            var control = new PartControl(part, this);
            Children.Add(control);
            partControls.Add(part, control);
        }

        void Remove(UPart part) {
            var control = partControls[part];
            control.Dispose();
            partControls.Remove(part);
            Children.Remove(control);
        }

        void InvalidatePartViewport() {
            if (_pianoRollOpenPart != null && partControls.TryGetValue(_pianoRollOpenPart, out var control)) {
                control.InvalidateVisual();
            }
        }

        public (UChordRegion Region, bool LoopHandle)? HitTestChordRegion(Point point) {
            if (TrackLayout.TrackNoAt(point.Y, TrackOffset, TrackHeight) != 0 || TickWidth <= 0) return null;
            int tick = (int)Math.Floor(TickOffset + point.X / TickWidth);
            var part = Core.DocManager.Inst.Project.ChordsPart;
            var region = part.chordRegions.LastOrDefault(item => tick >= item.position && tick < item.End);
            if (region == null) return null;
            bool handle = point.Y <= 12 && Math.Abs(point.X - (region.End - TickOffset) * TickWidth) <= 12;
            return (region, handle);
        }
    }
}
