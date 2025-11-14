using GMentor.Core;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GMentor.Services.PerProvider
{
    public sealed class GeminiClient : ILlmClient
    {
        private readonly HttpClient _http;
        private readonly string _key;
        private readonly string _model;
        private readonly StructuredLogger _logger = new("GMentor");

        public GeminiClient(HttpClient http, string apiKey, string model)
        {
            _http = http;
            _key = apiKey;
            _model = model;
        }

        public async Task<LlmResponse> AnalyzeAsync(LlmRequest request, CancellationToken ct)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_key}";

            object[] BuildParts(string promptText)
                => (request.ImageBytes is { Length: > 0 })
                    ? new object[]
                    {
                        new { text = promptText },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = GuessMime(request.ImageBytes),
                                data = Convert.ToBase64String(request.ImageBytes)
                            }
                        }
                    }
                    : new object[] { new { text = promptText } };

            // Force Google Search tool on every call (we rely on verified info)
            var payload = new
            {
                contents = new[] { new { parts = BuildParts(request.PromptText) } },
                tools = new object[] { new { google_search = new { } } },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 4096,
                    response_mime_type = "text/plain"
                }
            };

            var sw = Stopwatch.StartNew();
            using var resp = await _http.PostAsJsonAsync(url, payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();

            _logger.LogInbound(new
            {
                provider = "Gemini",
                model = _model,
                kind = "raw_response",
                attempt = 1,
                latency_ms = sw.Elapsed.TotalMilliseconds,
                raw_body = TrimBody(body)
            });

            if (!resp.IsSuccessStatusCode)
            {
                // Try parse { error: { code, status, message } }
                TryParseGoogleError(body, (int)resp.StatusCode,
                    out var httpCode, out var apiStatus, out var apiMessage);

                throw new LlmServiceException(httpCode, apiStatus, apiMessage);
            }

            var text = ExtractText(body);
            var usedWeb = ExtractGroundingUsed(body) || body.IndexOf("google_search", StringComparison.OrdinalIgnoreCase) >= 0;

            if (string.IsNullOrWhiteSpace(text))
                text = "Unknown";

            return new LlmResponse(text, usedWeb, sw.Elapsed);
        }

        // ---------------- Helpers ----------------

        private static bool TryParseGoogleError(
            string body, int fallbackCode,
            out int httpCode, out string? apiStatus, out string? apiMessage)
        {
            httpCode = fallbackCode;
            apiStatus = null;
            apiMessage = null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("error", out var err)) return false;

                if (err.TryGetProperty("code", out var c) && c.TryGetInt32(out var codeFromBody))
                    httpCode = codeFromBody;

                if (err.TryGetProperty("status", out var s))
                    apiStatus = s.GetString();

                if (err.TryGetProperty("message", out var m))
                    apiMessage = m.GetString();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GuessMime(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return "image/jpeg";
            if (data.Length >= 8 &&
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
                return "image/png";
            return "image/jpeg";
        }

        private static string TrimBody(string body) => body.Length > 12000 ? body[..12000] + " …[truncated]" : body;

        private static string ExtractText(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                    return string.Empty;

                var cand = cands[0];
                if (cand.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var partsArr) &&
                    partsArr.ValueKind == JsonValueKind.Array)
                {
                    string text = "";
                    foreach (var p in partsArr.EnumerateArray())
                    {
                        if (p.TryGetProperty("text", out var tp))
                        {
                            var t = tp.GetString();
                            if (!string.IsNullOrWhiteSpace(t))
                                text += (text.Length > 0 ? "\n" : "") + t;
                        }
                    }
                    return text ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private static bool ExtractGroundingUsed(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                    return false;

                var cand = cands[0];
                if (!cand.TryGetProperty("groundingMetadata", out var gm)) return false;

                if (gm.ValueKind == JsonValueKind.Object && gm.EnumerateObject().MoveNext())
                    return true;

                if (gm.TryGetProperty("citations", out var cites) &&
                    cites.ValueKind == JsonValueKind.Array && cites.GetArrayLength() > 0)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
