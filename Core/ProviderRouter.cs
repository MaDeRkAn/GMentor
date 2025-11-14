using GMentor.Services.PerProvider;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace GMentor.Core
{
    public static class ProviderRouter
    {
        public static ILlmClient Create(string provider, string model, string apiKey, HttpClient http)
        {
            return provider switch
            {
                "Gemini" => new GeminiClient(http, apiKey, model),
                _ => throw new NotSupportedException($"Provider {provider} not supported")
            };
        }

        public static async Task TestAsync(string provider, string model, string apiKey, HttpClient http, System.Windows.Window _)
        {
            var client = Create(provider, model, apiKey, http);

            // Text-only test: avoids image validation errors on providers.
            const string pingPrompt = "Reply with exactly: OK";

            var req = new LlmRequest(
                Provider: provider,
                Model: model,
                Game: "Ping",
                Category: "Test",
                PromptText: pingPrompt,
                ImageBytes: Array.Empty<byte>() // <- NO IMAGE
            );

            var resp = await client.AnalyzeAsync(req, default);

            if (resp is null || string.IsNullOrWhiteSpace(resp.Text))
                throw new Exception("No response from model.");
            // If you want to be strict:
            // if (!resp.Text.TrimStart().StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            //     throw new Exception($"Unexpected test reply: {resp.Text}");
        }
    }
}
