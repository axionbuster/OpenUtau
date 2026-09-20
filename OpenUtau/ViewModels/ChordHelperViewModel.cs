using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public sealed class ChordHelperSelectionEvent {
        public UVoicePart? Part { get; }
        public UChordHelper? Helper { get; }
        public ChordHelperSelectionEvent(UVoicePart? part, UChordHelper? helper) {
            Part = part;
            Helper = helper;
        }
    }

    public sealed class ChordRootChoice {
        public int PitchClass { get; }
        public string Name { get; }
        public ChordRootChoice(int pitchClass, string name) {
            PitchClass = pitchClass;
            Name = name;
        }
        public override string ToString() => Name;
    }

    public sealed class ChordBassChoice {
        public UChordInterval? Interval { get; }
        public string Name { get; }
        public ChordBassChoice(UChordInterval? interval, string name) {
            Interval = interval;
            Name = name;
        }
        public override string ToString() => Name;
    }

    public partial class ChordDegreeViewModel : ViewModelBase {
        public UChordInterval Interval { get; }
        [Reactive] public partial bool IsEnabled { get; set; }
        public string Label => Interval.Label;
        public FontWeight Weight => IsEnabled ? FontWeight.Bold : FontWeight.Normal;
        int PaletteIndex {
            get {
                bool is31Edo = DocManager.Inst.Project.Is31Edo;
                int step = Edo31.Mod(Interval.Offset(is31Edo), is31Edo ? 31 : 12);
                return DegreeColorPalette.IndexForInterval(step, is31Edo);
            }
        }
        public string Background {
            get {
                return DegreeColorPalette.Hex(DegreeColorPalette.Background(PaletteIndex, IsEnabled));
            }
        }
        public string Foreground {
            get {
                if (IsEnabled) {
                    return "#FFFFFF";
                }
                var background = DegreeColorPalette.Background(PaletteIndex, IsEnabled);
                return DegreeColorPalette.Hex(DegreeColorPalette.Foreground(background));
            }
        }
        public string Border => IsEnabled ? Foreground : "#64748B";
        public Thickness BorderThickness => new Thickness(IsEnabled ? 2 : 1);
        public ChordDegreeViewModel(UChordInterval interval, bool enabled) {
            Interval = interval;
            IsEnabled = enabled;
        }
        public void Refresh() {
            this.RaisePropertyChanged(nameof(Weight));
            this.RaisePropertyChanged(nameof(Background));
            this.RaisePropertyChanged(nameof(Foreground));
            this.RaisePropertyChanged(nameof(Border));
            this.RaisePropertyChanged(nameof(BorderThickness));
        }
    }

    public partial class ChordHelperViewModel : ViewModelBase {
        bool syncing;
        UVoicePart? selectedPart;
        UChordHelper? selectedHelper;
        string? selectedQuality;
        ChordRootChoice? selectedRoot;
        ChordBassChoice? selectedBass;
        bool highlightRoot;
        bool muted;
        int position;
        int duration;

        public bool HasSelection => selectedPart != null && selectedHelper != null;
        public UVoicePart? SelectedPart => selectedPart;
        public UChordHelper? SelectedHelper => selectedHelper;
        public ObservableCollectionExtended<string> QualityChoices { get; } = new();
        public ObservableCollectionExtended<ChordRootChoice> RootChoices { get; } = new();
        public ObservableCollectionExtended<ChordBassChoice> BassChoices { get; } = new();
        public ObservableCollectionExtended<ChordDegreeViewModel> Degrees { get; } = new();
        public int DegreeColumns => DocManager.Inst.Project.Is31Edo ? 6 : 4;
        public double DegreeGridWidth => DegreeColumns * 46;

        public string? SelectedQuality {
            get => selectedQuality;
            set {
                if (selectedQuality == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref selectedQuality, value);
                if (value == null || syncing || !HasSelection) {
                    return;
                }
                if (value != null && ChordHelperTheory.Presets.Any(preset => preset.Name == value)) {
                    ApplyChange(helper => helper.tones = ChordHelperTheory.CreatePreset(value));
                }
            }
        }

        public ChordRootChoice? SelectedRoot {
            get => selectedRoot;
            set {
                if (selectedRoot == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref selectedRoot, value);
                if (value == null || syncing || !HasSelection) {
                    return;
                }
                ApplyChange(helper => {
                    int divisions = DocManager.Inst.Project.Is31Edo ? 31 : 12;
                    if (helper.rootTone.HasValue) {
                        helper.rootTone = helper.rootTone.Value
                            - Edo31.Mod(helper.rootTone.Value, divisions)
                            + value!.PitchClass;
                    }
                    helper.root = value!.PitchClass;
                });
            }
        }

        public ChordBassChoice? SelectedBass {
            get => selectedBass;
            set {
                if (selectedBass == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref selectedBass, value);
                if (syncing || !HasSelection || value == null) {
                    return;
                }
                ApplyChange(helper => helper.bass = value.Interval?.Clone());
            }
        }

        public bool HighlightRoot {
            get => highlightRoot;
            set {
                if (highlightRoot == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref highlightRoot, value);
                if (!syncing && HasSelection) {
                    ApplyChange(helper => helper.highlightRoot = value);
                }
            }
        }

        public bool Muted {
            get => muted;
            set {
                if (muted == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref muted, value);
                if (!syncing && HasSelection) {
                    ApplyChange(helper => helper.mute = value);
                }
            }
        }

        public int Position {
            get => position;
            set {
                value = Math.Max(0, value);
                if (position == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref position, value);
                if (!syncing && HasSelection) {
                    ApplyChange(helper => helper.position = value);
                }
            }
        }

        public int Duration {
            get => duration;
            set {
                value = Math.Max(1, value);
                if (duration == value) {
                    return;
                }
                this.RaiseAndSetIfChanged(ref duration, value);
                if (!syncing && HasSelection) {
                    ApplyChange(helper => helper.duration = value);
                }
            }
        }

        public ReactiveCommand<ChordDegreeViewModel, RxVoid> ToggleDegreeCommand { get; }
        public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

        public ChordHelperViewModel() {
            ToggleDegreeCommand = ReactiveCommand.Create<ChordDegreeViewModel>(ToggleDegree);
            DeleteCommand = ReactiveCommand.Create(DeleteSelected);
        }

        public static IEnumerable<(UVoicePart Part, UChordHelper Helper)> VisibleHelpers(UProject project) =>
            project.parts.OfType<UVoicePart>()
                .SelectMany(part => part.chordHelpers.Concat(
                    part.chordRegions.SelectMany(region => region.chordHelpers)).Select(helper => (part, helper)));

        public static IEnumerable<(UVoicePart Part, UChordRegion? Region, UChordHelper Helper, int Start, int End)> VisibleOccurrences(
                UProject project, int queryStart, int queryEnd) =>
            project.parts.OfType<UVoicePart>().SelectMany(part =>
                part.chordRegions.Count == 0
                    ? part.chordHelpers.Select(helper => (part, (UChordRegion?)null, helper,
                        part.position + helper.position, part.position + helper.End))
                    : part.chordRegions.SelectMany(region => ChordRegionExpander.Enumerate(
                        region, queryStart - part.position, queryEnd - part.position)
                        .Select(item => (part, (UChordRegion?)region, item.Helper,
                            part.position + item.StartTick, part.position + item.EndTick))));

        public static bool IsOwnedByEditorTrack(UVoicePart? editorPart, UVoicePart ownerPart) =>
            editorPart != null && (ownerPart.IsChordPart || editorPart.trackNo == ownerPart.trackNo);

        public void HandleEditorPartChanged(UVoicePart? editorPart) {
            if (selectedPart != null && !IsOwnedByEditorTrack(editorPart, selectedPart)) {
                Select(null, null);
            }
        }

        public bool TrySelect(UVoicePart? editorPart, UVoicePart ownerPart, UChordHelper helper) {
            if (!IsOwnedByEditorTrack(editorPart, ownerPart)) {
                return false;
            }
            Select(ownerPart, helper);
            return true;
        }

        void Select(UVoicePart? part, UChordHelper? helper) {
            selectedPart = part;
            selectedHelper = helper;
            this.RaisePropertyChanged(nameof(SelectedPart));
            this.RaisePropertyChanged(nameof(SelectedHelper));
            this.RaisePropertyChanged(nameof(HasSelection));
            Refresh();
            MessageBus.Current.SendMessage(new ChordHelperSelectionEvent(part, helper));
        }

        public void Refresh() {
            syncing = true;
            try {
                bool is31 = DocManager.Inst.Project.Is31Edo;
                QualityChoices.Clear();
                QualityChoices.AddRange(ChordHelperTheory.Presets
                    .Where(preset => is31 || preset.Name != "Harmonic seventh")
                    .Select(preset => preset.Name));
                RootChoices.Clear();
                int divisions = is31 ? 31 : 12;
                this.RaisePropertyChanged(nameof(DegreeColumns));
                this.RaisePropertyChanged(nameof(DegreeGridWidth));
                for (int pitchClass = 0; pitchClass < divisions; pitchClass++) {
                    string name = is31
                        ? Edo31.FifthName(Edo31.FifthsForStep(pitchClass, Preferences.Default.PreferredKey31Fifths))
                        : MusicMath.KeysInOctave[pitchClass].Item1;
                    RootChoices.Add(new ChordRootChoice(pitchClass, name));
                }

                Degrees.Clear();
                BassChoices.Clear();
                BassChoices.Add(new ChordBassChoice(null, "Root position"));
                if (!HasSelection) {
                    selectedQuality = null;
                    selectedRoot = null;
                    selectedBass = BassChoices[0];
                    highlightRoot = true;
                    muted = false;
                    position = 0;
                    duration = 480;
                } else {
                    var helper = selectedHelper!;
                    string quality = ChordHelperTheory.QualityName(helper.tones, is31);
                    if (!QualityChoices.Contains(quality)) {
                        QualityChoices.Insert(0, quality);
                    }
                    selectedQuality = quality;
                    selectedRoot = RootChoices.First(choice =>
                        choice.PitchClass == Edo31.Mod(helper.root, divisions));
                    foreach (var tone in helper.tones) {
                        BassChoices.Add(new ChordBassChoice(tone.Clone(),
                            ChordHelperTheory.DisplayInterval(tone, helper.tones, is31).Label));
                    }
                    selectedBass = helper.bass == null
                        ? BassChoices[0]
                        : BassChoices.FirstOrDefault(choice => choice.Interval?.Equals(helper.bass) == true) ?? BassChoices[0];
                    highlightRoot = helper.highlightRoot;
                    muted = helper.mute;
                    position = helper.position;
                    duration = helper.duration;
                    var active = helper.tones
                        .GroupBy(tone => Edo31.Mod(tone.Offset(is31), divisions))
                        .ToDictionary(group => group.Key, group => group.First());
                    for (int step = 0; step < divisions; step++) {
                        bool enabled = active.TryGetValue(step, out var storedTone);
                        var interval = enabled
                            ? storedTone!.Clone()
                            : ChordHelperTheory.CanonicalInterval(step, is31);
                        Degrees.Add(new ChordDegreeViewModel(
                            ChordHelperTheory.DisplayInterval(interval, helper.tones, is31), enabled));
                    }
                }
                this.RaisePropertyChanged(nameof(SelectedQuality));
                this.RaisePropertyChanged(nameof(SelectedRoot));
                this.RaisePropertyChanged(nameof(SelectedBass));
                this.RaisePropertyChanged(nameof(HighlightRoot));
                this.RaisePropertyChanged(nameof(Muted));
                this.RaisePropertyChanged(nameof(Position));
                this.RaisePropertyChanged(nameof(Duration));
            } finally {
                syncing = false;
            }
        }

        void ToggleDegree(ChordDegreeViewModel degree) {
            if (!HasSelection) {
                return;
            }
            bool is31 = DocManager.Inst.Project.Is31Edo;
            int divisions = is31 ? 31 : 12;
            int target = Edo31.Mod(degree.Interval.Offset(is31), divisions);
            int existingIndex = selectedHelper!.tones.FindIndex(tone =>
                Edo31.Mod(tone.Offset(is31), divisions) == target);
            if (existingIndex >= 0 && selectedHelper.tones.Count == 1) {
                return;
            }
            ApplyChange(helper => {
                int index = helper.tones.FindIndex(tone => Edo31.Mod(tone.Offset(is31), divisions) == target);
                if (index >= 0) {
                    helper.tones.RemoveAt(index);
                } else {
                    helper.tones.Add(degree.Interval.Clone());
                }
            });
        }

        void DeleteSelected() {
            if (!HasSelection) {
                return;
            }
            var part = selectedPart!;
            var helper = selectedHelper!;
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new RemoveChordHelperCommand(part, helper));
            DocManager.Inst.EndUndoGroup();
            Select(null, null);
        }

        public bool CopySelected() {
            if (!HasSelection) {
                return false;
            }
            var project = DocManager.Inst.Project;
            DocManager.Inst.ChordsClipboard = new ChordClipboardPayload(
                new[] { selectedHelper! }, project.Is31Edo,
                project.Is31Edo ? project.PitchReference31 : null);
            DocManager.Inst.NotesClipboard = null;
            DocManager.Inst.CurvesClipboard = null;
            return true;
        }

        public bool CutSelected() {
            if (!CopySelected()) {
                return false;
            }
            DeleteSelected();
            return true;
        }

        public bool Paste(int playPosTick, int snapDiv) {
            var payload = DocManager.Inst.ChordsClipboard;
            var project = DocManager.Inst.Project;
            if (payload == null || project.ChordsPart == null) {
                return false;
            }
            int snapUnit = Math.Max(1, project.resolution * 4 / Math.Max(1, snapDiv));
            int targetTick = Math.Max(0, playPosTick / snapUnit * snapUnit);
            var helpers = payload.CloneForPaste(project, targetTick);
            if (helpers == null) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new FileFormatException(
                    "Match the project tuning and 31-TET pitch reference before pasting chords.")));
                return false;
            }
            DocManager.Inst.StartUndoGroup();
            foreach (var helper in helpers) {
                DocManager.Inst.ExecuteCmd(new AddChordHelperCommand(project.ChordsPart, helper));
            }
            DocManager.Inst.EndUndoGroup();
            Select(project.ChordsPart, helpers[0]);
            return true;
        }

        public void CommitSnapshot(UVoicePart part, UChordHelper helper, UChordHelper before) {
            if (helper.position == before.position && helper.duration == before.duration) {
                Select(part, helper);
                return;
            }
            var after = helper.Clone();
            helper.CopyFrom(before);
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ChangeChordHelperCommand(part, helper, after));
            DocManager.Inst.EndUndoGroup();
            Select(part, helper);
        }

        void ApplyChange(Action<UChordHelper> change) {
            var changed = selectedHelper!.Clone();
            change(changed);
            changed.Normalize(DocManager.Inst.Project.Is31Edo);
            if (Equivalent(selectedHelper, changed)) {
                return;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ChangeChordHelperCommand(selectedPart!, selectedHelper!, changed));
            DocManager.Inst.EndUndoGroup();
            Refresh();
            MessageBus.Current.SendMessage(new ChordHelperSelectionEvent(selectedPart, selectedHelper));
        }

        static bool Equivalent(UChordHelper left, UChordHelper right) =>
            left.position == right.position && left.duration == right.duration &&
            left.root == right.root && left.rootTone == right.rootTone &&
            left.highlightRoot == right.highlightRoot && left.mute == right.mute &&
            left.color == right.color && Equals(left.bass, right.bass) &&
            left.tones.SequenceEqual(right.tones);

        public void OnCommand(UCommand command) {
            if (command is not ChordHelperCommand chord || selectedPart != chord.Part) {
                return;
            }
            if (selectedHelper == null || !selectedPart.chordHelpers.Contains(selectedHelper)) {
                Select(null, null);
            } else {
                Refresh();
                MessageBus.Current.SendMessage(new ChordHelperSelectionEvent(selectedPart, selectedHelper));
            }
        }
    }
}
