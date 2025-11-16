using GMentor.Models;

namespace GMentor.Core
{
    public static class PromptComposer
    {
        public static IPromptPackProvider? Provider { get; set; }

        public static string Compose(string rawGame, string category, string? questName, string? ocrSnippet)
        {
            if (Provider == null)
                return $"You help gamers...\nGame: {Sanitize(rawGame)}\nCategory: {category}\n";

            // category is already a pack category id when called from RunFlowAsync (dynamic path)
            return Provider.GetPrompt(rawGame, category, ocrSnippet);
        }

        public static GameCapabilities GetCapabilities(string rawGame)
            => Provider is null
                ? new Models.GameCapabilities()
                : Provider.GetActiveCapabilities(rawGame);

        private static string Sanitize(string? t) => string.IsNullOrWhiteSpace(t) ? "Unknown" : t!.Trim();
    }
}