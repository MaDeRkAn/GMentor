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

            // Prevent WPF from auto-shutting down when the first window closes
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
                    // User bailed out; default to English
                    lang = "en";
                    AppSettings.SaveLanguage(lang);
                }
            }

            // Initial localization load (may be overridden later when packs sync)
            LocalizationService.Load(lang!);

            // --- API key bootstrap ---
            var store = new SecureKeyStore("GMentor");
            var existingKey = store.TryLoad("Gemini");

            // First-run: no key yet -> show setup dialog
            if (string.IsNullOrWhiteSpace(existingKey))
            {
                var setup = new SetupWindow();
                var ok = setup.ShowDialog();

                if (ok != true)
                {
                    // User cancelled / closed setup – terminate app cleanly
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
                    // 1) Hot-reload game prompt packs
                    _packProvider?.Reload();

                    // 2) Hot-reload localization packs for the current language
                    //    so newly downloaded strings.<lang>.gpack take effect
                    LocalizationService.Load(LocalizationService.CurrentLanguage);

                    // 3) Show the same “language updated” dialog once per session
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
                    // best effort – never crash the app on background sync
                }
            };

            // Fire-and-forget initial sync
            _ = _packSync.CheckNowAsync();

            // --- Create and show the main window ---
            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            // From this point, closing MainWindow should close the app
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _packSync?.Dispose();
            base.OnExit(e);
        }
    }
}
