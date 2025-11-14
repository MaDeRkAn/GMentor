using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace GMentor.Services
{
    /// <summary>
    /// Append-only NDJSON logger for prompts/responses.
    /// One file per day: %AppData%\{app}\logs\YYYY-MM-DD.ndjson
    /// Thread-safe, best-effort (exceptions swallowed).
    /// </summary>
    public sealed class StructuredLogger
    {
        private readonly string _dir;
        private readonly object _gate = new();

        public StructuredLogger(string appName)
        {
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName, "logs");
            Directory.CreateDirectory(_dir);
        }

        private string FileFor(DateTime utcNow) => Path.Combine(_dir, $"{utcNow:yyyy-MM-dd}.ndjson");

        public void LogOutbound(object payload)
        {
            WriteLine(new
            {
                ts_utc = DateTime.UtcNow.ToString("o"),
                kind = "outbound",
                payload
            });
        }

        public void LogInbound(object payload)
        {
            WriteLine(new
            {
                ts_utc = DateTime.UtcNow.ToString("o"),
                kind = "inbound",
                payload
            });
        }

        private void WriteLine(object record)
        {
            try
            {
                var line = JsonSerializer.Serialize(record);
                lock (_gate)
                {
                    File.AppendAllText(FileFor(DateTime.UtcNow), line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // best-effort: never throw from logger
            }
        }

        public static string Sha256Hex(ReadOnlySpan<byte> data)
        {
            Span<byte> hash = stackalloc byte[32];
            using var sha = SHA256.Create();
            sha.TryComputeHash(data, hash, out _);
            var sb = new StringBuilder(64);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
