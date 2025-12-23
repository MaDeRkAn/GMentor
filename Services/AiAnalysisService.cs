using System.Net.Http;
using GMentor.Core;
using GMentor.Models;

namespace GMentor.Services
{
    public sealed class MissingKeyException : Exception
    {
        public MissingKeyException() : base("Missing API key.") { }
    }

    public sealed class AiAnalysisService : IAiAnalysisService
    {
        private readonly SecureKeyStore _keyStore;
        private readonly StructuredLogger _logger;
        private readonly HttpClient _http;
        private const string DefaultModel = "gemini-2.5-flash";

        public AiAnalysisService(
            SecureKeyStore keyStore,
            StructuredLogger logger,
            HttpClient http)
        {
            _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<AiResult> AnalyzeAsync(
            string uiCategory,
            string? stickyGameTitle,
            string trustedTitle,
            byte[] image,
            CancellationToken cancellationToken = default)
        {
            if (image is null || image.Length == 0)
                throw new ArgumentException("Image buffer is empty.", nameof(image));

            var provider = "Gemini";
            var model = _keyStore.TryLoad("Gemini.Model") ?? DefaultModel;

            // FIX: session key first, then persisted key
            var key = SessionKeyStore.TryGet(provider) ?? _keyStore.TryLoad(provider);

            if (string.IsNullOrWhiteSpace(key))
                throw new MissingKeyException();

            // Prefer sticky game title if we have one
            var rawTitle = string.IsNullOrWhiteSpace(stickyGameTitle)
                ? trustedTitle
                : stickyGameTitle;

            var prompt = PromptComposer.Compose(rawTitle, uiCategory, null, null);
            var game = GetGameCanonicalFromPrompt(prompt);

            // OUTBOUND LOG
            var imgLen = image.Length;
            var imgHash = imgLen > 0 ? StructuredLogger.Sha256Hex(image) : null;

            _logger.LogOutbound(new
            {
                provider,
                model,
                game,
                category = uiCategory,
                prompt,
                image_len = imgLen,
                image_sha256 = imgHash
            });

            var client = ProviderRouter.Create(provider, model, key!, _http);
            var started = DateTime.UtcNow;

            var resp = await client.AnalyzeAsync(
                new LlmRequest(provider, model, game, uiCategory, prompt, image),
                cancellationToken);

            var latency = DateTime.UtcNow - started;
            var normalizedText = ResultParser.NormalizeBullets(resp.Text);

            // INBOUND LOG
            _logger.LogInbound(new
            {
                provider,
                model,
                game,
                category = uiCategory,
                used_web_search = resp.UsedWebSearch,
                latency_ms = latency.TotalMilliseconds,
                response_preview = normalizedText.Length > 600 ? normalizedText[..600] : normalizedText
            });

            // Pack-aware YouTube query (if available), else fallback
            var ytFromPack = PromptComposer.Provider?.TryBuildYouTubeQuery(rawTitle, uiCategory, normalizedText);
            var ytQuery = ytFromPack ?? BuildYouTubeQuery(game, normalizedText, uiCategory);

            return new AiResult(
                Game: game,
                Text: normalizedText,
                UsedWebSearch: resp.UsedWebSearch,
                Latency: latency,
                YouTubeQuery: ytQuery
            );
        }

        // ---- helpers ----

        private static string GetGameCanonicalFromPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "Unknown";

            var lines = prompt.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("Game:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring("Game:".Length).Trim();
                    return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
                }
            }
            return "Unknown";
        }

        private static string BuildYouTubeQuery(string game, string responseText, string cat)
        {
            try
            {
                var g = string.IsNullOrWhiteSpace(game) ? "game" : game.Trim();
                var alt = ResultParser.SynthesizeYouTubeQueryFrom(responseText ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(alt))
                    return alt;

                return $"{g} guide";
            }
            catch
            {
                return $"{(string.IsNullOrWhiteSpace(game) ? "game" : game)} guide";
            }
        }
    }
}
