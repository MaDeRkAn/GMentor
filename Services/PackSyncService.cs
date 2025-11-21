using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GMentor.Services
{
    /// <summary>
    /// Fetches {baseUrl}/index.json and syncs packs into %AppData%\GMentor\packs
    /// and localization packs into %AppData%\GMentor\Localization.
    /// If 'period' is TimeSpan.Zero, no background timer is created (single-shot mode).
    /// </summary>
    public sealed class PackSyncService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _userDir;   // game packs
        private readonly string _locDir;    // localization packs
        private readonly StructuredLogger _log = new("GMentor");
        private readonly string _indexUrl;
        private readonly Timer? _timer;
        private readonly TimeSpan _period;

        public event EventHandler? PacksChanged;

        public PackSyncService(string baseUrl, TimeSpan? period = null, HttpClient? http = null)
        {
            _indexUrl = baseUrl.TrimEnd('/') + "/index.json";
            _period = period ?? TimeSpan.FromHours(6);
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _userDir = Path.Combine(appData, "GMentor", "packs");
            _locDir = Path.Combine(appData, "GMentor", "Localization");

            Directory.CreateDirectory(_userDir);
            Directory.CreateDirectory(_locDir);

            // If period == TimeSpan.Zero => single-shot, no timer.
            if (_period > TimeSpan.Zero)
            {
                _timer = new Timer(async _ => await SafeRun(), null, TimeSpan.FromSeconds(5), _period);
            }
        }

        public async Task CheckNowAsync() => await SafeRun();

        private async Task SafeRun()
        {
            try { await RunOnce(); }
            catch
            {
                // best effort; avoid crashing caller
            }
        }

        private async Task RunOnce()
        {
            using var resp = await _http.GetAsync(_indexUrl);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            var index = JsonSerializer.Deserialize<PackIndex>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (index == null)
                return;

            bool changed = false;

            // ---- GAME PACKS ----
            if (index.Packs is { Count: > 0 })
            {
                var updated = await SyncEntriesAsync(index.Packs, _userDir);
                if (updated) changed = true;
            }

            // ---- LOCALIZATION PACKS ----
            if (index.Localization is { Count: > 0 })
            {
                var updated = await SyncEntriesAsync(index.Localization, _locDir);
                if (updated) changed = true;
            }

            if (changed)
                PacksChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Syncs a list of entries (game packs or localization) into a target directory.
        /// Downloads only when SHA differs.
        /// </summary>
        private async Task<bool> SyncEntriesAsync(
            IList<PackIndexItem> entries,
            string targetDir)
        {
            bool changed = false;

            foreach (var p in entries)
            {
                var localPath = Path.Combine(targetDir, p.Name + ".gpack");
                var localSig = Path.Combine(targetDir, p.Name + ".sig");

                if (File.Exists(localPath))
                {
                    var okSha = VerifySha256(localPath, p.Sha256);
                    if (okSha)
                        continue;
                }

                var tmpData = Path.GetTempFileName();
                var tmpSig = Path.GetTempFileName();

                try
                {
                    await DownloadTo(p.Url, tmpData);
                    await DownloadTo(p.SigUrl, tmpSig);

                    if (!VerifySha256(tmpData, p.Sha256))
                        continue;

                    var dataBytes = await File.ReadAllBytesAsync(tmpData);
                    var sigB64 = (await File.ReadAllTextAsync(tmpSig)).Trim();

                    File.WriteAllBytes(localPath, dataBytes);
                    File.WriteAllText(localSig, sigB64);

                    changed = true;
                }
                finally
                {
                    TryDelete(tmpData);
                    TryDelete(tmpSig);
                }
            }

            return changed;
        }

        private async Task DownloadTo(string url, string targetPath)
        {
            var bytes = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(targetPath, bytes);
        }

        private static bool VerifySha256(string filePath, string expectedHex)
        {
            try
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(filePath);
                var hash = sha.ComputeHash(fs);
                var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return string.Equals(
                    hex,
                    expectedHex.Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string p)
        {
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch { }
        }

        private sealed class PackIndex
        {
            public List<PackIndexItem> Packs { get; set; } = new();

            // NEW: localization entries like strings.en / strings.tr
            public List<PackIndexItem>? Localization { get; set; }
        }

        private sealed class PackIndexItem
        {
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public string Url { get; set; } = "";
            public string SigUrl { get; set; } = "";
        }

        public void Dispose() => _timer?.Dispose();
    }
}
