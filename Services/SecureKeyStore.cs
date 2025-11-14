using System.IO;
using System.Security.Cryptography;
using System.Text;


namespace GMentor.Services
{
    public sealed class SecureKeyStore
    {
        private readonly string _app; private readonly string _dir;
        public SecureKeyStore(string appName)
        {
            _app = appName;
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
            Directory.CreateDirectory(_dir);
        }

        private string PathFor(string provider) => System.IO.Path.Combine(_dir, $"{provider}.key");

        public void Save(string provider, string key)
        {
            var data = Encoding.UTF8.GetBytes(key);
            var enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PathFor(provider), enc);
        }

        public string? TryLoad(string provider)
        {
            var p = PathFor(provider);
            if (!File.Exists(p)) return null;
            var enc = File.ReadAllBytes(p);
            var dec = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }

        public void Delete(string provider)
        {
            var p = PathFor(provider);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
