using GMentor.Core;
using GMentor.Models;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GMentor.Services
{
    /// <summary>
    /// Loads signed *.gpack JSONs from %AppData%\GMentor\packs (user) and %ProgramData%\GMentor\packs (machine).
    /// Thread-safe with lightweight locking around the in-memory pack map.
    /// Dynamic categories are passed through unchanged; a tiny legacy mapping keeps old labels working.
    /// </summary>
    public sealed class PromptPackProvider : IPromptPackProvider
    {
        // ---- limits / JSON
        private const int MAX_PACK_BYTES = 512 * 1024;   // 512 KB per pack (defense-in-depth)
        private const int MAX_CATEGORIES = 16;           // guard-rail to avoid UI abuse
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        // Legacy category IDs (kept only for back-compat)
        public const string CatQuest = "Quest";
        public const string CatGun = "GunMods";
        public const string CatLoot = "LootItem";
        public const string CatKeys = "KeysCards";

        // BAKED-IN public key (PEM). Replace with your own. Optional machine override at %ProgramData%\GMentor\packs\public-key.pem
        private const string EmbeddedPublicPem =
@"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBbefaGLcV0z4kRsUrMZSDq/137WP
Y38WB/SecuXzvMZEBHaZN39g16nB/P67KHuRbAaqZ3HyE2eiWSNRdV+S0g==
-----END PUBLIC KEY-----";

        private readonly string _userDir;
        private readonly string _machineDir;
        private readonly StructuredLogger _log = new("GMentor");

        // in-memory pack store guarded by _gate
        private readonly object _gate = new();
        private readonly Dictionary<string, PromptPack> _packs = new(StringComparer.OrdinalIgnoreCase);

        public PromptPackProvider()
        {
            var baseDir = AppContext.BaseDirectory;
            _userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GMentor", "packs");
            _machineDir = Path.Combine(baseDir, "packs");
            // Create only where we are sure we can write; app dir may be read-only
            Directory.CreateDirectory(_userDir);
            // For _machineDir we don't *need* write access – it's mainly read-only,
            // so only create it if possible and ignore failures.
            try { Directory.CreateDirectory(_machineDir); } catch { /* ignore */ }
            LoadAllPacks();
        }

        /// <summary>Hot-reload packs from disk (e.g., after sync completes).</summary>
        public void Reload()
        {
            lock (_gate)
            {
                LoadAllPacks_NoLock();
            }
        }

        public string GetPrompt(string rawGameWindowTitle, string category, string? ocrSnippet)
        {
            // snapshot under lock; render string outside the lock
            PromptPack? pack;
            string catId;

            lock (_gate)
            {
                pack = ResolvePack_NoLock(rawGameWindowTitle) ?? _packs.GetValueOrDefault("General");
                catId = MapCategory(category); // legacy mapping only; dynamic IDs pass through unchanged
            }

            var gameName = pack?.GameId ?? "General";

            if (pack == null || !pack.Categories.TryGetValue(catId, out var cat))
                return ComposeHeaderOnly(gameName, catId, ocrSnippet);

            var sb = new StringBuilder();
            sb.AppendLine("You help gamers by analyzing screenshots and returning concise, verified, game-specific answers.");
            sb.AppendLine($"Game: {gameName}");
            sb.AppendLine($"Category: {(!string.IsNullOrWhiteSpace(cat.Label) ? cat.Label.Trim() : catId)}");
            sb.AppendLine();
            sb.AppendLine((cat.Template ?? string.Empty).Trim());

            if (!string.IsNullOrWhiteSpace(ocrSnippet))
            {
                var trimmed = ocrSnippet!.Length <= 200 ? ocrSnippet : ocrSnippet[..200];
                sb.AppendLine();
                sb.AppendLine($"OCR: \"{trimmed}\"");
            }
            return sb.ToString();
        }

        public GameCapabilities GetActiveCapabilities(string rawGameWindowTitle)
        {
            PromptPack? pack;
            lock (_gate)
            {
                pack = ResolvePack_NoLock(rawGameWindowTitle) ?? _packs.GetValueOrDefault("General");
            }

            var list = pack?.Categories.Select(kv => new ShortcutCapability
            {
                Id = kv.Key,                                                // dynamic ID
                Label = string.IsNullOrWhiteSpace(kv.Value.Label) ? kv.Key : kv.Value.Label.Trim(),
                HotkeyText = kv.Value.Hotkey ?? string.Empty
            }).ToList();

            // Fallback for the "General" case when no pack exists yet
            return new GameCapabilities
            {
                GameName = pack?.GameId ?? "General",
                Shortcuts = list ?? new List<ShortcutCapability>
                {
                    new() { Id = CatQuest, Label = "Quest / Mission", HotkeyText = "Ctrl+Alt+Q" },
                    new() { Id = CatGun,   Label = "Gun / Mods",      HotkeyText = "Ctrl+Alt+G" },
                    new() { Id = CatLoot,  Label = "Loot / Item",     HotkeyText = "Ctrl+Alt+L" },
                    new() { Id = CatKeys,  Label = "Keys / Cards",    HotkeyText = "Ctrl+Alt+K" }
                }
            };
        }

        /// <summary>Optional helper to tailor YouTube search queries per-pack.</summary>
        public string? TryBuildYouTubeQuery(string rawGameWindowTitle, string categoryId, string responseText)
        {
            PromptPack? pack;
            PackCategory? cat;

            lock (_gate)
            {
                pack = ResolvePack_NoLock(rawGameWindowTitle) ?? _packs.GetValueOrDefault("General");
                if (pack is null || !pack.Categories.TryGetValue(categoryId, out cat))
                    return null;
            }

            if (cat is null || string.IsNullOrWhiteSpace(cat.YouTubeTemplate))
                return null;

            var template = cat.YouTubeTemplate!;
            var text = responseText ?? string.Empty;

            // First apply simple {Game}/{Category} replacements
            var gameName = string.IsNullOrWhiteSpace(pack.GameId) ? "Game" : pack.GameId!;
            var categoryLabel = string.IsNullOrWhiteSpace(cat.Label) ? categoryId : cat.Label!;

            string result = template
                .Replace("{Game}", gameName)
                .Replace("{Category}", categoryLabel);

            // Find all <Placeholder> tokens dynamically
            var matches = Regex.Matches(result, "<([^>]+)>");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in matches)
            {
                var placeholder = match.Groups[0].Value; // "<BaseName>"
                var tokenName = match.Groups[1].Value;   // "BaseName"

                // Avoid resolving same token multiple times
                if (!seen.Add(placeholder))
                    continue;

                var value = ExtractTokenValueFromResponse(tokenName, text);
                if (string.IsNullOrWhiteSpace(value))
                    continue; // leave placeholder; we will validate later

                result = result.Replace(placeholder, value);
            }

            // If any <...> remain, template isn't usable, let caller fall back
            if (Regex.IsMatch(result, "<[^>]+>"))
                return null;

            return result.Trim().Replace(" **", "");
        }

        private static string? ExtractTokenValueFromResponse(string tokenName, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var norm = tokenName.Trim().ToLowerInvariant();

            // 1) Exact heading: **TokenName:** value
            var direct = ExtractFromHeading(tokenName, text);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            // 2) Handle "...Base" suffix: WeaponBase -> Weapon
            if (norm.EndsWith("base", StringComparison.Ordinal))
            {
                var root = tokenName[..^"base".Length].Trim();
                var rootVal = ExtractFromHeading(root, text);
                if (!string.IsNullOrWhiteSpace(rootVal))
                    return rootVal;
            }

            // 3) Generic mapping for common cases (works across games)
            var headingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["weaponbase"] = "Weapon",
                ["weapon"] = "Weapon",
                ["quest"] = "Quest",
                ["item"] = "Item",
                ["key/card"] = "Key/Card",
                ["keycard"] = "Key/Card"
            };

            if (headingMap.TryGetValue(norm, out var headingName))
            {
                var mapped = ExtractFromHeading(headingName, text);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }

            // 4) Fallback: first heading-style line in the response
            var fallback = Regex.Match(text, @"\*\*[^:]+:\s*(?<val>[^\r\n]+)");
            if (fallback.Success)
                return fallback.Groups["val"].Value.Trim();

            return null;
        }

        private static string? ExtractFromHeading(string headingName, string text)
        {
            var pattern = $@"\*\*\s*{Regex.Escape(headingName)}\s*:\s*(?<val>[^\r\n]+)";
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["val"].Value.Trim() : null;
        }



        // ---------------- internals ----------------

        private void LoadAllPacks()
        {
            lock (_gate) { LoadAllPacks_NoLock(); }
        }

        private void LoadAllPacks_NoLock()
        {
            _packs.Clear();

            foreach (var dir in new[] { _machineDir, _userDir })
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var gpack in Directory.EnumerateFiles(dir, "*.gpack", SearchOption.TopDirectoryOnly))
                {
                    var sigPath = Path.ChangeExtension(gpack, ".sig");
                    try
                    {
                        if (!Verify(gpack, sigPath, out var json)) continue;

                        var pack = JsonSerializer.Deserialize<PromptPack>(json, JsonOpts);
                        if (pack == null || string.IsNullOrWhiteSpace(pack.GameId)) continue;

                        // Guardrails: cap category count
                        if (pack.Categories?.Count > MAX_CATEGORIES)
                        {
                            pack.Categories = pack.Categories
                                .Take(MAX_CATEGORIES)
                                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
                        }

                        _packs[pack.GameId] = pack;
                    }
                    catch
                    {
                        // malformed pack: ignore
                    }
                }
            }

            if (!_packs.ContainsKey("General"))
            {
                _packs["General"] = new PromptPack
                {
                    GameId = "General",
                    Version = "1.0.0",
                    Matchers = Array.Empty<string>(),
                    Categories = new Dictionary<string, PackCategory>()
                };
            }
        }

        private static string Canonical(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Compatibility normalization (maps full-width, ligatures, etc.)
            var k = s.Normalize(NormalizationForm.FormKC);

            var sb = new StringBuilder(k.Length);
            foreach (var ch in k)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);

                // Remove gremlins that break Contains:
                if (cat is UnicodeCategory.Format        // ZWSP, BOM, joiners…
                    or UnicodeCategory.Control
                    or UnicodeCategory.NonSpacingMark    // combining accents
                    or UnicodeCategory.EnclosingMark
                    or UnicodeCategory.OtherNotAssigned)
                    continue;

                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
                    continue;

                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private PromptPack? ResolvePack_NoLock(string rawGameTitle)
        {
            if (string.IsNullOrWhiteSpace(rawGameTitle))
                return _packs.GetValueOrDefault("General");

            // Exact match by GameId (case-insensitive, no allocations)
            if (_packs.TryGetValue(rawGameTitle, out var direct))
                return direct;

            foreach (var kv in _packs)
            {
                // Also check exact GameId case-insensitive
                if (string.Equals(kv.Key, rawGameTitle, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            // Canonicalize once
            var titleC = Canonical(rawGameTitle);
            if (titleC.Length == 0)
                return _packs.GetValueOrDefault("General");

            // Fuzzy by matchers (canonicalized contains)
            foreach (var kv in _packs)
            {
                var p = kv.Value;
                if (p.Matchers is not { Length: > 0 }) continue;

                foreach (var m in p.Matchers)
                {
                    if (string.IsNullOrWhiteSpace(m)) continue;

                    var mc = Canonical(m);
                    if (mc.Length == 0) continue;

                    if (titleC.IndexOf(mc, StringComparison.Ordinal) >= 0)
                        return p;
                }
            }

            return _packs.GetValueOrDefault("General");
        }

        /// <summary>
        /// Legacy shim: only maps a few old hard-coded labels to canonical IDs.
        /// For dynamic categories, this returns the input unchanged.
        /// </summary>
        private static string MapCategory(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return cat ?? string.Empty;

            // Legacy labels → canonical IDs
            if (string.Equals(cat, "Gun Mods", StringComparison.OrdinalIgnoreCase)) return CatGun;
            if (string.Equals(cat, "Loot", StringComparison.OrdinalIgnoreCase)) return CatLoot;
            if (string.Equals(cat, "Keys", StringComparison.OrdinalIgnoreCase)) return CatKeys;

            // Already an ID (dynamic or legacy): pass through
            return cat;
        }

        private static string ComposeHeaderOnly(string game, string mappedCategory, string? ocr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You help gamers by analyzing screenshots and returning concise, verified, game-specific answers.");
            sb.AppendLine($"Game: {game}");
            sb.AppendLine($"Category: {mappedCategory}");
            if (!string.IsNullOrWhiteSpace(ocr))
            {
                var t = ocr!.Length <= 200 ? ocr : ocr[..200];
                sb.AppendLine();
                sb.AppendLine($"OCR: \"{t}\"");
            }
            return sb.ToString();
        }

        private bool Verify(string gpackPath, string sigPath, out string json)
        {
            json = "";
            if (!File.Exists(gpackPath) || !File.Exists(sigPath)) return false;

            var data = File.ReadAllBytes(gpackPath);
            if (data.Length == 0 || data.Length > MAX_PACK_BYTES) return false; // size guard

            byte[] sig;
            try { sig = Convert.FromBase64String(File.ReadAllText(sigPath).Trim()); }
            catch { return false; }

            try
            {
                using var ecdsa = ECDsa.Create();
                // Prefer machine override if present
                var pemOverride = Path.Combine(_machineDir, "public-key.pem");
                var pem = File.Exists(pemOverride) ? File.ReadAllText(pemOverride) : EmbeddedPublicPem;
                ecdsa.ImportFromPem(pem);

                if (!ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256)) return false;

                json = Encoding.UTF8.GetString(data);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
