using System.Windows;

namespace GMentor
{
    public partial class App : Application
    {
        private GMentor.Core.Services.PromptPackProvider? _packProvider;
        private GMentor.Core.Services.PackSyncService? _packSync;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Prevent WPF from auto-shutting down when the first window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            base.OnStartup(e);

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
            _packProvider = new GMentor.Core.Services.PromptPackProvider();
            GMentor.Core.PromptComposer.Provider = _packProvider;

            // One-shot sync on startup (no background timer)
            _packSync = new GMentor.Core.Services.PackSyncService(
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
