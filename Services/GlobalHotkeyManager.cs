using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using GMentor.Models;

namespace GMentor.Services
{
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private readonly Dictionary<int, string> _hotkeyToCategory = new();
        private readonly List<int> _registeredIds = new();
        private readonly HwndSource _source;
        private int _nextId = 2000;

        public event Action<string>? HotkeyTriggered;

        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_WIN = 0x0008;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public GlobalHotkeyManager(HwndSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.AddHook(WndProc);
        }

        public void Reload(IEnumerable<ShortcutCapability> shortcuts)
        {
            Clear();

            if (shortcuts is null)
                return;

            foreach (var s in shortcuts)
            {
                var hkText = string.IsNullOrWhiteSpace(s.HotkeyText)
                    ? GetFallbackHotkey(_registeredIds.Count)
                    : s.HotkeyText;

                if (!TryParseHotkey(hkText, out var modifiers, out var key))
                    continue;

                var id = _nextId++;
                var vk = KeyInterop.VirtualKeyFromKey(key);

                if (RegisterHotKey(_source.Handle, id, modifiers, vk))
                {
                    _registeredIds.Add(id);
                    _hotkeyToCategory[id] = s.Id;
                }
            }
        }

        public void Clear()
        {
            foreach (var id in _registeredIds)
            {
                UnregisterHotKey(_source.Handle, id);
            }
            _registeredIds.Clear();
            _hotkeyToCategory.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (_hotkeyToCategory.TryGetValue(id, out var cat))
                {
                    HotkeyTriggered?.Invoke(cat);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Clear();
            _source.RemoveHook(WndProc);
        }

        // --- helpers ---

        private static string GetFallbackHotkey(int index) => index switch
        {
            0 => "Ctrl+Alt+Q",
            1 => "Ctrl+Alt+G",
            2 => "Ctrl+Alt+L",
            3 => "Ctrl+Alt+K",
            _ => ""
        };

        private static bool TryParseHotkey(string input, out int modifiers, out Key key)
        {
            modifiers = 0;
            key = Key.None;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "alt":
                        modifiers |= MOD_ALT;
                        break;
                    case "shift":
                        modifiers |= MOD_SHIFT;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= MOD_WIN;
                        break;
                }
            }

            var keyPart = parts[^1];
            return Enum.TryParse(keyPart, true, out key);
        }
    }
}
