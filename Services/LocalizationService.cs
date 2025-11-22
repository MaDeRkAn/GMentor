using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GMentor.Services
{
    public static class LocalizationService
    {
        private static readonly Dictionary<string, string> _strings = new();
        public static string CurrentLanguage { get; private set; } = "en";

        /// <summary>
        /// Load a language pack:
        /// 1) %AppData%\GMentor\Localization\strings.<lang>.gpack  (downloaded)
        /// 2) ./packs/Localization/strings.<lang>.gpack            (bundled with app)
        /// Falls back to built-in English if nothing works.
        /// </summary>
        public static void Load(string languageCode)
        {
            CurrentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim();
            _strings.Clear();

            // 1) AppData localization (managed by PackSyncService)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataLocDir = Path.Combine(appData, "GMentor", "Localization");
            var appDataGpack = Path.Combine(appDataLocDir, $"strings.{CurrentLanguage}.gpack");

            if (TryLoadFromFile(appDataGpack))
                return;

            // 2) Bundled packs next to the EXE: ./packs/Localization/strings.<lang>.gpack
            var exeDir = AppContext.BaseDirectory;
            var exeLocDir = Path.Combine(exeDir, "packs", "Localization");
            var exeGpack = Path.Combine(exeLocDir, $"strings.{CurrentLanguage}.gpack");

            if (TryLoadFromFile(exeGpack))
                return;

            // 3) Fallback: built-in English defaults
            LoadBuiltInEnglish();
        }

        /// <summary>
        /// Tries to read JSON dictionary from the given .gpack path
        /// and populate _strings. Returns true on success.
        /// </summary>
        private static bool TryLoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is null || dict.Count == 0)
                    return false;

                _strings.Clear();
                foreach (var kv in dict)
                    _strings[kv.Key] = kv.Value;

                return true;
            }
            catch
            {
                // Malformed / incompatible file – treat as not found
                return false;
            }
        }

        private static void LoadBuiltInEnglish()
        {
            _strings.Clear();

            // --- Main flow / status ---
            _strings["Status.Cooldown"] = "Cooldown…";
            _strings["Status.CaptureRegion"] = "Capture a region…";
            _strings["Status.Canceled"] = "Canceled";
            _strings["Status.ProviderOverloaded"] = "Provider overloaded.";
            _strings["Status.RateLimit"] = "Free-tier limit reached.";
            _strings["Status.AuthFailed"] = "Auth failed.";
            _strings["Status.Timeout"] = "AI timed out.";
            _strings["Status.RequestCanceled"] = "Request canceled/timeout.";
            _strings["Status.GenericError"] = "Something went wrong.";
            _strings["Status.MissingKey"] = "Missing key";

            _strings["Status.ProviderModelUpdated"] = "Provider/model updated.";
            _strings["Status.Ready"] = "Ready";

            // --- MessageBox titles ---
            _strings["Dialog.MissingKey.Title"] = "Missing key";
            _strings["Dialog.ProviderOverloaded.Title"] = "AI Model Overloaded (503)";
            _strings["Dialog.RateLimit.Title"] = "Rate Limit (429)";
            _strings["Dialog.AuthError.Title"] = "Auth Error";
            _strings["Dialog.Timeout.Title"] = "Timeout";
            _strings["Dialog.AIError.Title"] = "AI Error";
            _strings["Dialog.RequestError.Title"] = "Request error";
            _strings["Dialog.NoQuery.Title"] = "No query";

            // --- MessageBox bodies ---
            _strings["Dialog.MissingKey.Body"] =
                "You need an API key.\nGo: File → Change Provider/Key…";

            _strings["Dialog.ProviderOverloaded.Body"] =
                "Gemini is overloaded right now.\n\n" +
                "It’s not your key and not your request. The model is at capacity.";

            _strings["Dialog.RateLimit.Body"] =
                "You've hit the usage limits for this model.\n\n" +
                "Action required:\n" +
                "• Switch to another Gemini model in File → Change Provider/Key…\n" +
                "• Or check your usage in Google AI Studio.";

            _strings["Dialog.RateLimit.Http.Body"] =
                "You’ve likely hit the free-tier per-minute or per-day cap.\n\n" +
                "Check your usage in Google AI Studio.";

            _strings["Dialog.AuthError.Body"] =
                "Auth error from Gemini.\n\n" +
                "Double-check your API key in File → Change Provider/Key…";

            _strings["Dialog.Timeout.Body"] =
                "The AI didn’t respond in time.\nTry the shortcut again from the same screen crop.";

            _strings["Dialog.RequestCanceled.Body"] =
                "The request was canceled or timed out.\nTry the shortcut again.";

            _strings["Dialog.AIError.Body"] =
                "The AI returned an error.\n\n{DETAILS}";

            _strings["Dialog.RequestError.Body"] =
                "The request failed. Double-check your key/model and try again.";

            _strings["Dialog.NoQuery.Body"] =
                "No tutorial query yet—run a request first.";

            // --- How to use / Help text ---
            _strings["Help.HowTo.Title"] = "How to use GMentor";
            _strings["Help.HowTo.Body"] =
                "How to use GMentor\n" +
                "------------------\n" +
                "1) Open your game and go to the screen you want analyzed.\n" +
                "2) Press a shortcut and drag a box over that area:\n" +
                "   • Ctrl+Alt+Q – Quest / Mission\n" +
                "   • Ctrl+Alt+G – Gun / Mods\n" +
                "   • Ctrl+Alt+L – Loot / Item\n" +
                "   • Ctrl+Alt+K – Keys / Cards\n" +
                "\n" +
                "Tip: If GMentor detects a supported game, shortcut names and actions adjust automatically. " +
                "Check the “Shortcuts” bar after launching your game.\n" +
                "\n" +
                "You can minimize GMentor — it keeps running in the tray and hotkeys stay active.";

            _strings["Help.Privacy.Title"] = "Privacy";
            _strings["Help.Privacy.Body"] =
                "Privacy:\n\n" +
                "• Your API key stays on your PC (encrypted with Windows).\n" +
                "• GMentor never proxies your key or requests.\n" +
                "• Only the crop you select is sent straight to the AI provider.";

            _strings["Help.NoQuery.Title"] = "No query";
            _strings["Help.NoQuery.Body"] =
                "No tutorial query yet—run a request first.";

            // --- Menu / language ---
            _strings["Menu.App"] = "GMentor";
            _strings["Menu.Language"] = "Language";
            _strings["Menu.Language.En"] = "English (Recommended)";
            _strings["Menu.Language.Tr"] = "Türkçe";
            _strings["Dialog.LanguageChanged.Title"] = "Language updated";
            _strings["Dialog.LanguageChanged.Body"] = "Restart GMentor to see all texts in the new language.";
            _strings["Text.ExpandAll"] = "Expand all";
            _strings["Text.CollapseAll"] = "Collapse all";

        }

        public static string T(string key)
            => _strings.TryGetValue(key, out var value) ? value : key;

        public static string TWith(string key, string placeholder, string value)
        {
            var s = T(key);
            return s.Replace(placeholder, value ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
