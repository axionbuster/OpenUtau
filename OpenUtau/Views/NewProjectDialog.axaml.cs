using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class NewProjectDialog : Window {
        public NewProjectDialog() {
            InitializeComponent();
        }

        void On31Tet(object? sender, RoutedEventArgs args) => Close((int?)31);
        void On12Tet(object? sender, RoutedEventArgs args) => Close((int?)12);
        void OnCancel(object? sender, RoutedEventArgs args) => Close();

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) {
                Close();
                e.Handled = true;
            }
        }
    }
}
