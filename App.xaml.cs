using GMentor.Services;
using System;
using System.Windows;

namespace GMentor
{
    public partial class App : Application
    {
        private PromptPackProvider? _packProvider;
        private PackSyncService? _packSync;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Prevent WPF from auto-shutting down when the first window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            base.OnStartup(e);

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

            LocalizationService.Load(lang!);

            var store = new Services.SecureKeyStore("GMentor");
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

            // Boot prompt pack provider (singleton)
            _packProvider = new PromptPackProvider();
            GMentor.Core.PromptComposer.Provider = _packProvider;

            // One-shot sync on startup (no background timer)
            _packSync = new Services.PackSyncService(
                baseUrl: "https://packs.gmentor.ai",
                period: TimeSpan.Zero);

            _packSync.PacksChanged += (_, __) =>
            {
                try { _packProvider?.Reload(); }
                catch { /* best effort */ }
            };

            _ = _packSync.CheckNowAsync();   // fire-and-forget

            // Now create and show the real main window
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
