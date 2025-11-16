using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GMentor.Services
{
    /// <summary>
    /// Fetches {baseUrl}/index.json and syncs packs into %AppData%\GMentor\packs.
    /// If 'period' is TimeSpan.Zero, no background timer is created (single-shot mode).
    /// </summary>
    public sealed class PackSyncService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _userDir;
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
            _userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GMentor", "packs");
            Directory.CreateDirectory(_userDir);

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
            catch { /* best effort */ }
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
            if (index?.Packs == null || index.Packs.Count == 0) return;

            bool changed = false;
            foreach (var p in index.Packs)
            {
                var localPath = Path.Combine(_userDir, p.Name + ".gpack");
                var localSig = Path.Combine(_userDir, p.Name + ".sig");

                if (File.Exists(localPath))
                {
                    var okSha = VerifySha256(localPath, p.Sha256);
                    if (okSha) continue;
                }

                var tmpData = Path.GetTempFileName();
                var tmpSig = Path.GetTempFileName();
                try
                {
                    await DownloadTo(p.Url, tmpData);
                    await DownloadTo(p.SigUrl, tmpSig);

                    if (!VerifySha256(tmpData, p.Sha256)) continue;

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

            if (changed) PacksChanged?.Invoke(this, EventArgs.Empty);
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
                return string.Equals(hex,
                    expectedHex.Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        private sealed class PackIndex
        {
            public List<PackIndexItem> Packs { get; set; } = new();
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
