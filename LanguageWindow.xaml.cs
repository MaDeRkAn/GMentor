using System.Windows;
using System.Windows.Input;

namespace GMentor
{
    public partial class LanguageWindow : Window
    {
        public string SelectedLanguageCode { get; private set; } = "en";

        public LanguageWindow()
        {
            InitializeComponent();
        }

        private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnContinue(object sender, RoutedEventArgs e)
        {
            SelectedLanguageCode = RbTurkish.IsChecked == true ? "tr" : "en";
            DialogResult = true;
        }
    }
}
