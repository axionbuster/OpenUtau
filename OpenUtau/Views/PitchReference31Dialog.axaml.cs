using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    public partial class PitchReference31Dialog : Window {
        static readonly string[] PitchClasses = {
            "C", "C♯ / D♭", "D", "D♯ / E♭", "E", "F",
            "F♯ / G♭", "G", "G♯ / A♭", "A", "A♯ / B♭", "B",
        };

        public PitchReference31Dialog() : this(Edo31PitchReference.Default) { }

        public PitchReference31Dialog(Edo31PitchReference reference) {
            InitializeComponent();
            var value = reference.ValidatedCopy();
            ReferenceNote.ItemsSource = PitchClasses;
            A4Frequency.Value = (decimal)value.A4Frequency;
            ReferenceNote.SelectedIndex = value.TwelveTetPitchClass;
            A4Mode.IsChecked = value.Mode == Edo31PitchReferenceMode.A4Frequency;
            NoteMode.IsChecked = value.Mode == Edo31PitchReferenceMode.TwelveTetNote;
            UpdateState();
        }

        void OnModeChanged(object? sender, RoutedEventArgs args) => UpdateState();
        void OnFrequencyChanged(object? sender, NumericUpDownValueChangedEventArgs args) => UpdateState();
        void OnReferenceNoteChanged(object? sender, SelectionChangedEventArgs args) => UpdateState();

        void UpdateState() {
            if (A4Frequency == null || ReferenceNote == null || Summary == null) {
                return;
            }
            bool absolute = A4Mode.IsChecked == true;
            A4Frequency.IsEnabled = absolute;
            ReferenceNote.IsEnabled = !absolute;
            var reference = CurrentReference();
            if (absolute) {
                Summary.Text = $"A4 is the native anchor at {reference.A4Frequency.ToString("0.###", CultureInfo.CurrentCulture)} Hz. " +
                    "Every other pitch is derived directly in equal 31-step octaves.";
            } else {
                int step = Edo31.NearestStepForTwelveTetPitchClass(reference.TwelveTetPitchClass);
                Summary.Text = $"The nearest 31-TET step ({step} steps above C) is fixed to 12-TET {PitchClasses[reference.TwelveTetPitchClass]}. " +
                    $"The resulting 31-TET A4 is {reference.EffectiveA4Frequency.ToString("0.###", CultureInfo.CurrentCulture)} Hz.";
            }
        }

        Edo31PitchReference CurrentReference() => new Edo31PitchReference {
            Mode = A4Mode.IsChecked == true
                ? Edo31PitchReferenceMode.A4Frequency
                : Edo31PitchReferenceMode.TwelveTetNote,
            A4Frequency = (double)(A4Frequency.Value ?? 440m),
            TwelveTetPitchClass = Math.Clamp(ReferenceNote.SelectedIndex, 0, 11),
        }.ValidatedCopy();

        void OnOk(object? sender, RoutedEventArgs args) => Close(CurrentReference());
        void OnCancel(object? sender, RoutedEventArgs args) => Close();
    }
}
