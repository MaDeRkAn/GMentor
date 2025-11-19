using System;
using System.IO;
using System.Text.Json;

namespace GMentor.Services
{
    public static class AppSettings
    {
        private sealed class SettingsDto
        {
            public string Language { get; set; } = "en";
        }

        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "GMentor", "settings.json");

        public static string? TryGetLanguage()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return null;

                var json = File.ReadAllText(SettingsPath);
                var dto = JsonSerializer.Deserialize<SettingsDto>(json);
                return string.IsNullOrWhiteSpace(dto?.Language) ? null : dto.Language;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveLanguage(string languageCode)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var dto = new SettingsDto { Language = languageCode ?? "en" };
                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // best-effort; app can still run
            }
        }
    }
}
