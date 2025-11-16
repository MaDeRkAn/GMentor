using GMentor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GMentor.Core
{
    public interface IPromptPackProvider
    {
        // Returns the best prompt for (gameTitle, category, ocr). Falls back to "General".
        string GetPrompt(string rawGameWindowTitle, string category, string? ocrSnippet);

        // Returns capabilities (labels/hotkeys/templates) for the active game, else general.
        GameCapabilities GetActiveCapabilities(string rawGameWindowTitle);

        // NEW: optional per-pack YouTube template hook
        string? TryBuildYouTubeQuery(string rawGameWindowTitle, string categoryId, string responseText);
    }
}

