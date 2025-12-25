using GMentor.Core;
using GMentor.Services;
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
        private const string Provider = "Gemini";

        public SetupWindow()
        {
            InitializeComponent();

            // Restore saved model (or default)
            var savedModel = _keys.TryLoad("Gemini.Model") ?? DefaultModel;
            SelectModelInCombo(savedModel);

            // Prefill saved key
            var savedKey = _keys.TryLoad(Provider);
            if (!string.IsNullOrWhiteSpace(savedKey))
            {
                KeyBox.Password = savedKey;
                RememberKey.IsChecked = true;
                BtnContinue.IsEnabled = true;
                Status.Text = LocalizationService.T("Setup.Status.SavedKeyLoaded");
            }
            else
            {
                Status.Text = LocalizationService.T("Setup.Status.EnterKeyFirst");
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

        private void OnCloseClick(object sender, RoutedEventArgs e)
            => DialogResult = false;

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

        private async void OnTest(object sender, RoutedEventArgs e)
        {
            BtnTest.IsEnabled = false;
            BtnContinue.IsEnabled = false;
            Status.Text = LocalizationService.T("Setup.Status.CheckingKey");

            try
            {
                var key = KeyBox.Password?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    Status.Text = LocalizationService.T("Setup.Status.EnterKeyToTest");
                    return;
                }

                var model = GetSelectedModelId();

                // Ping via existing router
                await Core.ProviderRouter.TestAsync(Provider, model, key!, _http, this);

                Status.Text = LocalizationService.TWith("Setup.Status.KeyOk", "{MODEL}", model);
                BtnContinue.IsEnabled = true;

                // Persist model choice (not sensitive)
                _keys.Save("Gemini.Model", model);

                // IMPORTANT: Do not force-save key on Test. Continue decides.
            }
            catch (LlmServiceException ex)
            {
                // Mirror MainWindow behavior: reasoned UX
                switch (ex.HttpCode)
                {
                    case 503:
                        Status.Text = LocalizationService.T("Status.ProviderOverloaded");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.ProviderOverloaded.Body"),
                            LocalizationService.T("Dialog.ProviderOverloaded.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        break;

                    case 429:
                        Status.Text = LocalizationService.T("Status.RateLimit");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.RateLimit.Body"),
                            LocalizationService.T("Dialog.RateLimit.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        TryOpen("https://aistudio.google.com/app/usage?timeRange=last-28-days");
                        break;

                    case 401:
                    case 403:
                        Status.Text = LocalizationService.T("Status.AuthFailed");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.AuthError.Body"),
                            $"{LocalizationService.T("Dialog.AuthError.Title")} ({ex.HttpCode})",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        break;

                    case 408:
                    case 504:
                        Status.Text = LocalizationService.T("Status.Timeout");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.Timeout.Body"),
                            $"{LocalizationService.T("Dialog.Timeout.Title")} ({ex.HttpCode})",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        break;

                    default:
                        Status.Text = LocalizationService.T("Status.GenericError");
                        var details = $"{ex.HttpCode} {ex.ApiStatus}.\n\n{ex.ApiMessage}";
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.TWith("Dialog.AIError.Body", "{DETAILS}", details),
                            LocalizationService.T("Dialog.AIError.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        break;
                }

                BtnContinue.IsEnabled = false;
            }
            catch (HttpRequestException hre) when ((int?)hre.StatusCode == 429)
            {
                Status.Text = LocalizationService.T("Status.RateLimit");
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.RateLimit.Http.Body"),
                    LocalizationService.T("Dialog.RateLimit.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TryOpen("https://aistudio.google.com/app/usage?timeRange=last-28-days");
                BtnContinue.IsEnabled = false;
            }
            catch (TaskCanceledException)
            {
                Status.Text = LocalizationService.T("Status.Timeout");
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.Timeout.Body"),
                    LocalizationService.T("Dialog.Timeout.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                BtnContinue.IsEnabled = false;
            }
            catch
            {
                Status.Text = LocalizationService.T("Setup.Status.TestFailed");
                BtnContinue.IsEnabled = false;
            }
            finally
            {
                BtnTest.IsEnabled = true;
            }
        }

        private static void TryOpen(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }


        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnContinue(object sender, RoutedEventArgs e)
        {
            var key = KeyBox.Password?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                Status.Text = LocalizationService.T("Setup.Status.EnterKeyFirst");
                return;
            }

            var model = GetSelectedModelId();

            // FIX: Always set session key so app works immediately even if not saved
            SessionKeyStore.Set(Provider, key!);

            // Persist only if user opted in
            if (RememberKey.IsChecked == true)
                _keys.Save(Provider, key!);
            else
                _keys.Delete(Provider); // user explicitly chose not to store

            // Always remember selected model (not secret)
            _keys.Save("Gemini.Model", model);

            DialogResult = true;
        }
    }
}
