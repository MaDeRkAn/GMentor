using System;
using System.Windows;

namespace GMentor
{
    public partial class App : Application
    {
        private GMentor.Core.Services.PromptPackProvider? _packProvider;
        private GMentor.Core.Services.PackSyncService? _packSync;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var store = new Services.SecureKeyStore("GMentor");
            var existing = store.TryLoad("Gemini");
            if (string.IsNullOrWhiteSpace(existing))
            {
                var setup = new SetupWindow();
                var ok = setup.ShowDialog();
                if (ok != true) { Shutdown(); return; }
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
                catch { /* best-effort */ }
            };

            // Fire and forget; UI remains responsive.
            _ = _packSync.CheckNowAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _packSync?.Dispose();
            base.OnExit(e);
        }
    }
}
