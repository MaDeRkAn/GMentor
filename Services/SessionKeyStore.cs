using System.Collections.Concurrent;

namespace GMentor.Services
{
    /// <summary>
    /// In-memory secrets for the current app session only (not persisted).
    /// </summary>
    public static class SessionKeyStore
    {
        private static readonly ConcurrentDictionary<string, string> _secrets = new();

        public static void Set(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (string.IsNullOrWhiteSpace(value))
            {
                _secrets.TryRemove(name, out _);
                return;
            }

            _secrets[name] = value.Trim();
        }

        public static string? TryGet(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _secrets.TryGetValue(name, out var v) ? v : null;
        }

        public static bool Has(string name)
            => !string.IsNullOrWhiteSpace(TryGet(name));

        public static void Clear() => _secrets.Clear();
    }
}
