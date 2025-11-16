using System.Text.Json.Serialization;

namespace GMentor.Models
{
    // On-disk signed JSON ("*.gpack") – not encrypted
    public sealed class PromptPack
    {
        [JsonPropertyName("gameId")] public string GameId { get; set; } = "General";
        [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
        [JsonPropertyName("matchers")] public string[] Matchers { get; set; } = [];   // substrings/regexes for window title
        [JsonPropertyName("categories")] public Dictionary<string, PackCategory> Categories { get; set; } = new();
    }

    public sealed class PackCategory
    {
        [JsonPropertyName("label")] public string Label { get; set; } = ""; // UI label e.g., "Gun Mods"
        [JsonPropertyName("hotkey")] public string Hotkey { get; set; } = ""; // e.g., "Ctrl+Alt+G" (informational)
        [JsonPropertyName("template")] public string Template { get; set; } = ""; // The body (no Game/Category header)
        [JsonPropertyName("ytTemplate")] public string? YouTubeTemplate { get; set; }
    }

    public sealed class GameCapabilities
    {
        public string GameName { get; init; } = "General";
        public IReadOnlyList<ShortcutCapability> Shortcuts { get; init; } = Array.Empty<ShortcutCapability>();
    }
    public sealed class ShortcutCapability
    {
        public string Id { get; init; } = "";         // category key from pack (e.g., "GunMods" or anything)
        public string Label { get; init; } = "";      // UI label, e.g., "Boss Weakness"
        public string HotkeyText { get; init; } = ""; // e.g., "Ctrl+Alt+G" (display-only)
    }
}
