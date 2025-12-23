using GMentor.Services;
using System.Windows;

namespace GMentor
{
    public partial class App : Application
    {
        private PromptPackProvider? _packProvider;
        private PackSyncService? _packSync;

        // so we don't spam the user with multiple popups
        private bool _localizationReloadNotified;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            // --- Language bootstrap ---
            var lang = AppSettings.TryGetLanguage();
            if (string.IsNullOrWhiteSpace(lang))
            {
                var langWindow = new LanguageWindow();
                var result = langWindow.ShowDialog();

                if (result == true)
                {
                    lang = langWindow.SelectedLanguageCode;
                    AppSettings.SaveLanguage(lang);
                }
                else
                {
                    lang = "en";
                    AppSettings.SaveLanguage(lang);
                }
            }

            LocalizationService.Load(lang!);

            // --- API key bootstrap ---
            var store = new SecureKeyStore("GMentor");
            var existingKey = store.TryLoad("Gemini");

            // First-run: no key yet -> show setup dialog
            // NOTE: Even if user doesn't save key, SetupWindow now sets SessionKeyStore so runtime works.
            if (string.IsNullOrWhiteSpace(existingKey) && !SessionKeyStore.Has("Gemini"))
            {
                var setup = new SetupWindow();
                var ok = setup.ShowDialog();

                if (ok != true)
                {
                    Shutdown();
                    return;
                }
            }

            // --- Prompt packs provider (singleton) ---
            _packProvider = new PromptPackProvider();
            Core.PromptComposer.Provider = _packProvider;

            // --- One-shot sync on startup (no background timer) ---
            _packSync = new Services.PackSyncService(
                baseUrl: "https://packs.gmentor.ai",
                period: TimeSpan.Zero);

            _packSync.PacksChanged += (_, __) =>
            {
                try
                {
                    _packProvider?.Reload();
                    LocalizationService.Load(LocalizationService.CurrentLanguage);

                    if (!_localizationReloadNotified)
                    {
                        _localizationReloadNotified = true;

                        Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                LocalizationService.T("Dialog.LanguageChanged.Body"),
                                LocalizationService.T("Dialog.LanguageChanged.Title"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        });
                    }
                }
                catch
                {
                    // best effort
                }
            };

            _ = _packSync.CheckNowAsync();

            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _packSync?.Dispose();
                SessionKeyStore.Clear();
            }
            finally
            {
                base.OnExit(e);
            }
        }
    }
}
