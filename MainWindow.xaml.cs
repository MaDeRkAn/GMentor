using GMentor.Core;
using GMentor.Models;
using GMentor.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GMentor
{
    public partial class MainWindow : Window
    {
        // ---- runtime state
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };
        private byte[]? _lastImage;
        private string? _lastYouTubeQuery;
        private string? _lastNonSelfWindowTitle; // persist last non-GMentor window title

        // STICKY supported game (once detected, don't fall back to General until a new supported game appears)
        private string? _stickyGameTitle; // raw window title as seen
        private string? _stickyGameId;    // pack.GameId (e.g., "ArcRaiders")

        // ---- services
        private readonly Services.SecureKeyStore _keyStore = new("GMentor");
        private readonly Services.StructuredLogger _logger = new("GMentor");
        private readonly Services.TrayService _tray;

        private const string GoogleUsageUrl = "https://aistudio.google.com/app/usage?timeRange=last-28-days";

        // ---- hwnd + global hotkeys
        private HwndSource? _hwndSrc;
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly List<int> _registeredHotkeyIds = new();          // current OS registrations
        private readonly Dictionary<int, string> _hotkeyToCategory = new(); // hotkeyId -> categoryId

        private GameDetector? _gameDetector;

        // ---- dynamic shortcuts cache for current game
        private List<ShortcutCapability> _activeShortcuts = new();

        private const string DefaultModel = "gemini-2.5-flash";

        public MainWindow()
        {
            InitializeComponent();

            // Game detector: detects foreground window title and raises GameChanged
            _gameDetector = new GameDetector(
                pollInterval: TimeSpan.FromSeconds(5),
                debounce: TimeSpan.FromSeconds(15));

            _gameDetector.GameChanged += (_, title) =>
            {
                Dispatcher.Invoke(() => RefreshShortcutsFor(title));
            };

            // Prime once on startup so the UI isn't stale until the first tick
            var initialTitle = Services.ScreenCaptureService.TryDetectGameWindowTitle() ?? "General";
            RefreshShortcutsFor(initialTitle);

            // Surface current provider/model/key
            LblProvider.Text = "Gemini";

            var savedModel = _keyStore.TryLoad("Gemini.Model") ?? DefaultModel;
            LblModel.Text = savedModel;

            LblKey.Text = _keyStore.TryLoad("Gemini") != null ? "•••• " + LocalizationService.T("Text.Saved") : LocalizationService.T("Text.NotSet");
            UpdateGameLabel("General"); // shows "Game: Default"

            // Tray
            _tray = new Services.TrayService(
                onOpen: () => Dispatcher.Invoke(ShowAndActivate),
                onHelp: () => Dispatcher.Invoke(OpenHowTo),
                onQuit: () => Dispatcher.Invoke(Close)
            );
        }

        // ---- window chrome helpers
        private void ShowAndActivate()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwndSrc = (HwndSource)PresentationSource.FromVisual(this)!;
            _hwndSrc.AddHook(WndProc);
            _hwnd = _hwndSrc.Handle;

            // Register initial global hotkeys for whatever shortcuts are active (General on startup)
            ReloadGlobalHotkeys();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (_hwnd != IntPtr.Zero)
                {
                    foreach (var id in _registeredHotkeyIds)
                    {
                        UnregisterHotKey(_hwnd, id);
                    }

                    _registeredHotkeyIds.Clear();
                    _hotkeyToCategory.Clear();
                }

                _hwndSrc?.RemoveHook(WndProc);
            }
            catch
            {
                // ignore on shutdown
            }
            finally
            {
                _hwnd = IntPtr.Zero;
                _hwndSrc = null;
                _tray.Dispose();
                _gameDetector?.Dispose();
                base.OnClosed(e);
            }
        }

        // ===== Sticky-aware dynamic shortcuts wiring =====
        private void RefreshShortcutsFor(string gameWindowTitle)
        {
            // Ask provider for capabilities of the *current* window title.
            var caps = PromptComposer.GetCapabilities(gameWindowTitle);
            var isSupported = !string.IsNullOrWhiteSpace(caps.GameName)
                              && !caps.GameName.Equals("General", StringComparison.OrdinalIgnoreCase);

            // Supported game path
            if (isSupported)
            {
                // If supported game changed, adopt it and visually highlight
                if (!string.Equals(_stickyGameId, caps.GameName, StringComparison.OrdinalIgnoreCase))
                {
                    _stickyGameId = caps.GameName;       // pack.GameId (e.g. "ArcRaiders")
                    _stickyGameTitle = gameWindowTitle;  // raw title from window

                    ApplyCapabilitiesToUi(caps);
                    UpdateGameLabel(caps.GameName);
                    FlashGameChip();
                }
                return;
            }

            // Not supported (General / unknown)
            // If we already locked onto a supported game, keep it (no UI changes).
            if (_stickyGameId is not null)
                return;

            // First run or still no sticky game: show whatever (General) once.
            ApplyCapabilitiesToUi(caps);
            UpdateGameLabel(caps.GameName);
        }

        private void UpdateGameLabel(string? packGameName)
        {
            var display = string.IsNullOrWhiteSpace(packGameName)
                          || packGameName.Equals("General", StringComparison.OrdinalIgnoreCase)
                ? LocalizationService.T("Text.Default")
                : packGameName;

            LblGameChip.Text = LocalizationService.T("Text.Game") + $": {display}";
        }

        private void FlashGameChip()
        {
            try
            {
                if (LblGameChip == null) return;

                // Animate the foreground color for a quick pulse
                var baseBrush = LblGameChip.Foreground as SolidColorBrush;
                if (baseBrush == null || baseBrush.IsFrozen)
                {
                    var accent = (SolidColorBrush)FindResource("AccentBrush");
                    baseBrush = new SolidColorBrush(accent.Color);
                    LblGameChip.Foreground = baseBrush;
                }

                var originalColor = baseBrush.Color;

                var anim = new ColorAnimation
                {
                    From = originalColor,
                    To = Colors.White,
                    Duration = TimeSpan.FromMilliseconds(120),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };

                baseBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            catch
            {
                // Non-critical UX; ignore failures.
            }
        }

        // Build the mapping & UI from a capability snapshot
        private void ApplyCapabilitiesToUi(GameCapabilities caps)
        {
            _activeShortcuts = caps.Shortcuts?.ToList() ?? new List<ShortcutCapability>();

            string FallbackForIndex(int i) => i switch
            {
                0 => "Ctrl+Alt+Q",
                1 => "Ctrl+Alt+G",
                2 => "Ctrl+Alt+L",
                3 => "Ctrl+Alt+K",
                _ => ""
            };

            var lines = new List<string>();
            for (int i = 0; i < _activeShortcuts.Count; i++)
            {
                var s = _activeShortcuts[i];
                var fallback = FallbackForIndex(i);
                var hotkey = string.IsNullOrWhiteSpace(s.HotkeyText) ? fallback : s.HotkeyText;

                if (!string.IsNullOrWhiteSpace(hotkey))
                    lines.Add($"{s.Label} —  {hotkey}");
                else
                    lines.Add($"{s.Label}");
            }

            if (lines.Count == 0)
                lines.Add(LocalizationService.T("Text.NoShortcuts"));

            ShortcutsList.ItemsSource = lines;

            // After updating shortcuts list, refresh actual OS global hotkeys
            ReloadGlobalHotkeys();
        }

        /// <summary>
        /// Registers global hotkeys based on _activeShortcuts (current game/package).
        /// Called on startup and whenever capabilities change.
        /// </summary>
        private void ReloadGlobalHotkeys()
        {
            if (_hwnd == IntPtr.Zero)
                return; // window handle not ready yet

            // Unregister previous set
            foreach (var id in _registeredHotkeyIds)
            {
                UnregisterHotKey(_hwnd, id);
            }

            _registeredHotkeyIds.Clear();
            _hotkeyToCategory.Clear();

            if (_activeShortcuts == null || _activeShortcuts.Count == 0)
                return;

            // Use simple incremental ids for WM_HOTKEY
            var nextId = 2000;

            string FallbackForIndex(int i) => i switch
            {
                0 => "Ctrl+Alt+Q",
                1 => "Ctrl+Alt+G",
                2 => "Ctrl+Alt+L",
                3 => "Ctrl+Alt+K",
                _ => ""
            };

            for (int i = 0; i < _activeShortcuts.Count; i++)
            {
                var s = _activeShortcuts[i];

                // Use the manifest hotkey if set, otherwise fallback (for General/old packs)
                var hotkeyText = string.IsNullOrWhiteSpace(s.HotkeyText)
                    ? FallbackForIndex(i)
                    : s.HotkeyText;

                if (!TryParseHotkey(hotkeyText, out var modifiers, out var key))
                    continue;

                var id = nextId++;
                var vk = KeyInterop.VirtualKeyFromKey(key);

                if (RegisterHotKey(_hwnd, id, modifiers, vk))
                {
                    _registeredHotkeyIds.Add(id);
                    _hotkeyToCategory[id] = s.Id;   // s.Id is categoryId (Quest, LootItem, ItemsHideoutBarters, etc.)
                }
            }
        }

        private static bool TryParseHotkey(string input, out int modifiers, out Key key)
        {
            modifiers = 0;
            key = Key.None;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return false;

            // All but last = modifiers
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
                        modifiers |= 0x0004; // MOD_SHIFT
                        break;
                    case "win":
                    case "windows":
                        modifiers |= 0x0008; // MOD_WIN
                        break;
                }
            }

            var keyPart = parts[^1];
            if (!Enum.TryParse(keyPart, true, out key))
                return false;

            return true;
        }

        // ===== Custom title bar events =====
        private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ===== Window lifetime =====
        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                // Keep classic tray balloon only here.
                _tray.ShowBalloon(LocalizationService.T("Text.MiminizedMessage"));
            }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // If you prefer minimize-on-close, you can intercept here.
        }

        // ===== WndProc: global hotkeys =====
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                if (_hotkeyToCategory.TryGetValue(id, out var categoryId))
                {
                    _ = RunFlowAsync(categoryId);
                }

                handled = true;
            }
            return IntPtr.Zero;
        }

        #region Win32 Hotkey P/Invoke
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion

        // ===== Core run flow =====
        private async Task RunFlowAsync(string uiCategory)
        {
            try
            {
                if ((DateTime.UtcNow - _lastRequestUtc) < TimeSpan.FromSeconds(2))
                {
                    StatusText.Text = LocalizationService.T("Status.Cooldown");
                    return;
                }

                // Clear old content up-front to avoid confusion
                ResetUiForNewRequest(status: LocalizationService.T("Status.CaptureRegion"));

                // Capture before bringing our window to front; also sample the active window title
                var rawTitleBefore = Services.ScreenCaptureService.TryDetectGameWindowTitle() ?? "";
                RememberNonSelfTitle(rawTitleBefore);

                var bmp = Services.ScreenCaptureService.CaptureInteractiveRegion(this);
                if (bmp == null) { StatusText.Text = LocalizationService.T("Status.Canceled"); return; }

                // Now we can safely show our window
                ShowAndActivate();

                PreviewImage.Source = Services.ImagePipeline.ToBitmapImage(bmp);
                _lastImage = Services.ImagePipeline.ToJpeg720(bmp, quality: 75);

                var provider = "Gemini";
                var model = _keyStore.TryLoad("Gemini.Model") ?? DefaultModel;
                var key = _keyStore.TryLoad(provider);
                if (string.IsNullOrWhiteSpace(key))
                {
                    RestoreUiAfterRequest();
                    MessageBoxEx.Show(
                        this,
                        LocalizationService.T("Dialog.MissingKey.Body"),
                        LocalizationService.T("Dialog.MissingKey.Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var cat = uiCategory; // pack category IDs already

                // Prefer sticky title for composing prompts; else fall back to last trusted title.
                var rawTitle = _stickyGameTitle ?? GetTrustedTitle();
                var prompt = PromptComposer.Compose(rawTitle, cat, null, null);
                var game = GetGameCanonicalFromPrompt(prompt);

                // OUTBOUND LOG
                var imgLen = _lastImage?.Length ?? 0;
                var imgHash = imgLen > 0 ? Services.StructuredLogger.Sha256Hex(_lastImage) : null;
                _logger.LogOutbound(new { provider, model, game, category = uiCategory, prompt, image_len = imgLen, image_sha256 = imgHash });

                StatusText.Text = LocalizationService.T("Status.TalkingToAI");

                var client = ProviderRouter.Create(provider, model, key!, _http);
                var started = DateTime.UtcNow;

                var resp = await client.AnalyzeAsync(
                    new LlmRequest(provider, model, game, cat, prompt, _lastImage!), default);

                var text = ResultParser.NormalizeBullets(resp.Text);

                // INBOUND LOG
                var latencyMs = (DateTime.UtcNow - started).TotalMilliseconds;
                _logger.LogInbound(new
                {
                    provider,
                    model,
                    game,
                    category = uiCategory,
                    used_web_search = resp.UsedWebSearch,
                    latency_ms = latencyMs,
                    response_preview = text.Length > 600 ? text[..600] : text
                });

                // Pack-aware YouTube query (if available), else fallback
                var ytFromPack = PromptComposer.Provider?.TryBuildYouTubeQuery(rawTitle, cat, text);
                _lastYouTubeQuery = ytFromPack ?? BuildYouTubeQuery(game, text, cat);

                BadgeSearch.Visibility = resp.UsedWebSearch ? Visibility.Visible : Visibility.Collapsed;

                // Render markdown into the RichTextBox
                RenderResponseMarkdown(text);

                _lastRequestUtc = DateTime.UtcNow;
                StatusText.Text = $"{LocalizationService.T("Status.DoneIn")} {resp.Latency.TotalMilliseconds:F0} ms";

                // Enable actions now that content is present
                CopyBtn.IsEnabled = true;
                OpenYouTubeBtn.IsEnabled = true;
            }
            catch (LlmServiceException ex)
            {
                // Keep the box empty for failed runs
                ResponseBox.Document.Blocks.Clear();
                BadgeSearch.Visibility = Visibility.Collapsed;

                switch (ex.HttpCode)
                {
                    case 503:
                        StatusText.Text = LocalizationService.T("Status.ProviderOverloaded");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.ProviderOverloaded.Body"),
                            LocalizationService.T("Dialog.ProviderOverloaded.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        break;

                    case 429:
                        StatusText.Text = LocalizationService.T("Status.RateLimit");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.RateLimit.Body"),
                            LocalizationService.T("Dialog.RateLimit.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        TryOpen(GoogleUsageUrl);
                        break;

                    case 401:
                    case 403:
                        StatusText.Text = LocalizationService.T("Status.AuthFailed");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.AuthError.Body"),
                            $"{LocalizationService.T("Dialog.AuthError.Title")} ({ex.HttpCode})",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        break;

                    case 408:
                    case 504:
                        StatusText.Text = LocalizationService.T("Status.Timeout");
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.T("Dialog.Timeout.Body"),
                            $"{LocalizationService.T("Dialog.Timeout.Title")} ({ex.HttpCode})",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        break;

                    default:
                        StatusText.Text = LocalizationService.T("Status.GenericError");
                        var details = $"{ex.HttpCode} {ex.ApiStatus}.\n\n{ex.ApiMessage}";
                        MessageBoxEx.Show(
                            this,
                            LocalizationService.TWith("Dialog.AIError.Body", "{DETAILS}", details),
                            LocalizationService.T("Dialog.AIError.Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        break;
                }
            }
            catch (HttpRequestException hre) when ((int?)hre.StatusCode == 429)
            {
                ResponseBox.Document.Blocks.Clear();
                BadgeSearch.Visibility = Visibility.Collapsed;

                StatusText.Text = LocalizationService.T("Status.RateLimit");
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.RateLimit.Http.Body"),
                    LocalizationService.T("Dialog.RateLimit.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TryOpen(GoogleUsageUrl);
            }
            catch (TaskCanceledException)
            {
                ResponseBox.Document.Blocks.Clear();
                BadgeSearch.Visibility = Visibility.Collapsed;

                StatusText.Text = LocalizationService.T("Status.RequestCanceled");
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.RequestCanceled.Body"),
                    LocalizationService.T("Dialog.Timeout.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ResponseBox.Document.Blocks.Clear();
                BadgeSearch.Visibility = Visibility.Collapsed;

                Debug.WriteLine(ex);
                StatusText.Text = LocalizationService.T("Status.GenericError");
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.RequestError.Body"),
                    LocalizationService.T("Dialog.RequestError.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RestoreUiAfterRequest();
            }
        }

        private void RenderResponseMarkdown(string raw)
        {
            var doc = new FlowDocument
            {
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13
            };
            var para = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 6),
                LineHeight = 18
            };

            foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(para);
                    para = new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 18 };
                    continue;
                }

                string text = line;
                var boldMatches = Regex.Split(text, @"(\*\*[^\*]+\*\*)");
                foreach (var part in boldMatches)
                {
                    if (part.StartsWith("**") && part.EndsWith("**"))
                    {
                        para.Inlines.Add(new Bold(new Run(part.Trim('*'))));
                    }
                    else
                    {
                        para.Inlines.Add(new Run(part));
                    }
                }

                para.Inlines.Add(new LineBreak());
            }

            doc.Blocks.Add(para);
            ResponseBox.Document = doc;
        }

        // ---- Title handling (don’t report “GMentor” as the game)
        private void RememberNonSelfTitle(string title)
        {
            var t = (title ?? "").Trim();
            if (string.IsNullOrEmpty(t)) return;

            if (!t.Contains("GMentor", StringComparison.OrdinalIgnoreCase) &&
                !t.Contains("GMentor —", StringComparison.OrdinalIgnoreCase))
            {
                _lastNonSelfWindowTitle = t;
            }
        }

        private string GetTrustedTitle()
        {
            if (!string.IsNullOrWhiteSpace(_lastNonSelfWindowTitle))
                return _lastNonSelfWindowTitle;

            var probe = Services.ScreenCaptureService.TryDetectGameWindowTitle() ?? "";
            if (!probe.Contains("GMentor", StringComparison.OrdinalIgnoreCase))
                return probe;

            return "Unknown";
        }

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

        // --- UI helpers for request lifecycle ---
        private void ResetUiForNewRequest(string status)
        {
            BadgeSearch.Visibility = Visibility.Collapsed;
            ResponseBox.Document.Blocks.Clear();
            ResponseBox.IsReadOnly = true;
            ResponseBox.Opacity = 0.85;
            CopyBtn.IsEnabled = false;
            OpenYouTubeBtn.IsEnabled = false;
            StatusText.Text = status;
        }

        private void RestoreUiAfterRequest()
        {
            ResponseBox.IsReadOnly = false;
            ResponseBox.Opacity = 1.0;

            bool hasText = !string.IsNullOrWhiteSpace(new TextRange(
                ResponseBox.Document.ContentStart,
                ResponseBox.Document.ContentEnd).Text?.Trim());

            CopyBtn.IsEnabled = hasText;
            OpenYouTubeBtn.IsEnabled = hasText;
        }

        // ---- YouTube query builder tuned for gun builds
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

        private static void TryOpen(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        // ===== Menu actions =====
        private void OnChangeProviderKey(object sender, RoutedEventArgs e)
        {
            var w = new SetupWindow();
            if (w.ShowDialog() == true)
            {
                LblKey.Text = _keyStore.TryLoad("Gemini") != null ? $"•••• {LocalizationService.T("Text.Saved")}" : LocalizationService.T("Text.NotSet");

                // refresh model from store
                var savedModel = _keyStore.TryLoad("Gemini.Model") ?? DefaultModel;
                LblModel.Text = savedModel;

                StatusText.Text = LocalizationService.T("Status.ProviderModelUpdated");
            }
        }

        private void OnExit(object sender, RoutedEventArgs e) => Close();

        private void OpenHowTo()
        {
            MessageBoxEx.Show(
                this,
                LocalizationService.T("Help.HowTo.Body"),
                LocalizationService.T("Help.HowTo.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnHelpHowTo(object sender, RoutedEventArgs e) => OpenHowTo();
        private void OnHelpGetKey(object sender, RoutedEventArgs e) => TryOpen("https://aistudio.google.com/app/apikey");
        private void OnHelpUsage(object sender, RoutedEventArgs e) => TryOpen(GoogleUsageUrl);

        private void OnHelpPrivacy(object sender, RoutedEventArgs e)
        {
            MessageBoxEx.Show(
                this,
                LocalizationService.T("Help.Privacy.Body"),
                LocalizationService.T("Help.Privacy.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var text = new TextRange(ResponseBox.Document.ContentStart, ResponseBox.Document.ContentEnd).Text.Trim();
            Clipboard.SetText(text);
        }

        private void OnOpenYouTube(object sender, RoutedEventArgs e)
        {
            var plainText = new TextRange(ResponseBox.Document.ContentStart, ResponseBox.Document.ContentEnd).Text;
            var q = _lastYouTubeQuery ?? ResultParser.SynthesizeYouTubeQueryFrom(plainText);
            if (string.IsNullOrWhiteSpace(q))
            {
                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Help.NoQuery.Body"),
                    LocalizationService.T("Help.NoQuery.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            TryOpen($"https://www.youtube.com/results?search_query={Uri.EscapeDataString(q)}");
        }


        private void OnAppMenuClick(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;

            var ctx = new ContextMenu
            {
                PlacementTarget = btn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false
            };

            // “Language” header
            var languageRoot = new MenuItem
            {
                Header = LocalizationService.T("Menu.Language")
            };

            // English
            var enItem = new MenuItem
            {
                Header = LocalizationService.T("Menu.Language.En"),
                Tag = "en",
                IsCheckable = true,
                IsChecked = string.Equals(LocalizationService.CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase)
            };
            enItem.Click += OnLanguageClick;

            // Turkish
            var trItem = new MenuItem
            {
                Header = LocalizationService.T("Menu.Language.Tr"),
                Tag = "tr",
                IsCheckable = true,
                IsChecked = string.Equals(LocalizationService.CurrentLanguage, "tr", StringComparison.OrdinalIgnoreCase)
            };
            trItem.Click += OnLanguageClick;

            languageRoot.Items.Add(enItem);
            languageRoot.Items.Add(trItem);

            ctx.Items.Add(languageRoot);
            ctx.IsOpen = true;
        }

        private void OnLanguageClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string code && !string.IsNullOrWhiteSpace(code))
            {
                ChangeLanguage(code);
            }
        }


        private void ChangeLanguage(string code)
        {
            AppSettings.SaveLanguage(code);
            LocalizationService.Load(code);

            MessageBox.Show(
                LocalizationService.T("Dialog.LanguageChanged.Body"),
                LocalizationService.T("Dialog.LanguageChanged.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
