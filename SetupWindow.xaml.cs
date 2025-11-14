using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace GMentor
{
    public partial class SetupWindow : Window
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly Services.SecureKeyStore _keys = new("GMentor");

        private const string DefaultModel = "gemini-2.5-flash";

        public SetupWindow()
        {
            InitializeComponent();

            // Restore saved model (or default)
            var savedModel = _keys.TryLoad("Gemini.Model") ?? DefaultModel;
            SelectModelInCombo(savedModel);

            // Prefill saved key
            var savedKey = _keys.TryLoad("Gemini");
            if (!string.IsNullOrWhiteSpace(savedKey))
            {
                KeyBox.Password = savedKey;
                RememberKey.IsChecked = true;
                BtnContinue.IsEnabled = true;
                Status.Text = "Saved key loaded — you can Continue.";
            }
            else
            {
                Status.Text = "Enter your API key first.";
            }
        }

        private void SelectModelInCombo(string modelId)
        {
            if (ModelCombo.Items.Count == 0)
            {
                ModelCombo.SelectedIndex = 0;
                return;
            }

            ComboBoxItem? match = null;

            foreach (var item in ModelCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    var content = cbi.Content as string;
                    if (string.Equals(content, modelId, StringComparison.OrdinalIgnoreCase))
                    {
                        match = cbi;
                        break;
                    }
                }
            }

            if (match != null)
            {
                ModelCombo.SelectedItem = match;
            }
            else
            {
                // fallback: default model, or first item
                foreach (var item in ModelCombo.Items)
                {
                    if (item is ComboBoxItem cbi)
                    {
                        var content = cbi.Content as string;
                        if (string.Equals(content, DefaultModel, StringComparison.OrdinalIgnoreCase))
                        {
                            ModelCombo.SelectedItem = cbi;
                            return;
                        }
                    }
                }

                ModelCombo.SelectedIndex = 0;
            }
        }

        private string GetSelectedModelId()
        {
            if (ModelCombo.SelectedItem is ComboBoxItem cbi)
            {
                var content = cbi.Content as string;
                if (!string.IsNullOrWhiteSpace(content))
                    return content;
            }
            return DefaultModel;
        }

        // ===== Title bar =====
        private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ===== Hyperlinks =====
        private void OnLink(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // ===== Buttons =====
        private async void OnTest(object sender, RoutedEventArgs e)
        {
            try
            {
                var key = KeyBox.Password?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    Status.Text = "Enter a key to test.";
                    BtnContinue.IsEnabled = false;
                    return;
                }

                var model = GetSelectedModelId();

                // simple ping via existing router
                await Core.ProviderRouter.TestAsync("Gemini", model, key!, _http, this);

                Status.Text = $"Key looks good for {model}.";
                BtnContinue.IsEnabled = true;

                if (RememberKey.IsChecked == true)
                    _keys.Save("Gemini", key!);

                // Persist model choice regardless (not sensitive)
                _keys.Save("Gemini.Model", model);
            }
            catch (Exception)
            {
                Status.Text = "Test failed. Check the key/model and try again.";
                BtnContinue.IsEnabled = false;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnContinue(object sender, RoutedEventArgs e)
        {
            var key = KeyBox.Password?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                Status.Text = "Enter your API key first.";
                return;
            }

            var model = GetSelectedModelId();

            if (RememberKey.IsChecked == true)
                _keys.Save("Gemini", key!);
            else
                _keys.Delete("Gemini");

            // Always remember selected model (not secret)
            _keys.Save("Gemini.Model", model);

            DialogResult = true;
        }
    }
}
