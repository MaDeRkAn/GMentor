using GMentor.Core;
using GMentor.Models;
using GMentor.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private readonly SecureKeyStore _keyStore = new("GMentor");
        private readonly StructuredLogger _logger = new("GMentor");
        private readonly TrayService _tray;
        private readonly IResponseSectionService _responseSections = new ResponseSectionService();
        private GlobalHotkeyManager? _hotkeys;
        private readonly IAiAnalysisService _ai;


        private const string GoogleUsageUrl = "https://aistudio.google.com/app/usage?timeRange=last-28-days";

        // ---- hwnd + global hotkeys
        private HwndSource? _hwndSrc;
        private IntPtr _hwnd = IntPtr.Zero;


        private string? _lastResponseText;

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
            var initialTitle = ScreenCaptureService.TryDetectGameWindowTitle() ?? "General";
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

            _ai = new AiAnalysisService(_keyStore, _logger, _http);
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
            _hwnd = _hwndSrc.Handle;

            _hotkeys = new GlobalHotkeyManager(_hwndSrc);
            _hotkeys.HotkeyTriggered += categoryId => _ = RunFlowAsync(categoryId);

            // initial hotkeys for current shortcuts
            ReloadGlobalHotkeys();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _hotkeys?.Dispose();
                _hotkeys = null;

                _tray.Dispose();
                _gameDetector?.Dispose();
            }
            finally
            {
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
            _hotkeys?.Reload(_activeShortcuts);
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

        // ===== Core run flow =====
        private async Task RunFlowAsync(string uiCategory)
        {
            try
            {
                // simple cooldown
                if ((DateTime.UtcNow - _lastRequestUtc) < TimeSpan.FromSeconds(2))
                {
                    StatusText.Text = LocalizationService.T("Status.Cooldown");
                    return;
                }

                // reset UI before capture
                ResetUiForNewRequest(status: LocalizationService.T("Status.CaptureRegion"));

                // sample active window title BEFORE overlay
                var rawTitleBefore = ScreenCaptureService.TryDetectGameWindowTitle() ?? "";
                RememberNonSelfTitle(rawTitleBefore);

                // capture region
                var bmp = ScreenCaptureService.CaptureInteractiveRegion(this);
                if (bmp == null)
                {
                    StatusText.Text = LocalizationService.T("Status.Canceled");
                    return;
                }

                // bring our window back, show preview
                ShowAndActivate();

                PreviewImage.Source = ImagePipeline.ToBitmapImage(bmp);
                _lastImage = ImagePipeline.ToJpeg720(bmp, quality: 75);

                if (_lastImage == null || _lastImage.Length == 0)
                {
                    StatusText.Text = LocalizationService.T("Status.GenericError");
                    return;
                }

                StatusText.Text = LocalizationService.T("Status.TalkingToAI");

                // prefer sticky game title if we have one
                var trustedTitle = GetTrustedTitle();
                var stickyTitle = _stickyGameTitle;

                // call AI service (handles prompt, logging, provider call, YouTube query)
                var result = await _ai.AnalyzeAsync(
                    uiCategory,
                    stickyTitle,
                    trustedTitle,
                    _lastImage);

                _lastResponseText = result.Text;
                _lastYouTubeQuery = result.YouTubeQuery;

                BadgeSearch.Visibility = result.UsedWebSearch ? Visibility.Visible : Visibility.Collapsed;

                // Render into collapsible sections
                RenderResponseSections(result.Text);

                _lastRequestUtc = DateTime.UtcNow;
                StatusText.Text = $"{LocalizationService.T("Status.DoneIn")} {result.Latency.TotalMilliseconds:F0} ms";

                CopyBtn.IsEnabled = true;
                OpenYouTubeBtn.IsEnabled = true;
            }
            catch (MissingKeyException)
            {
                // key is missing – same UX as before
                StatusText.Text = LocalizationService.T("Status.MissingKey");

                MessageBoxEx.Show(
                    this,
                    LocalizationService.T("Dialog.MissingKey.Body"),
                    LocalizationService.T("Dialog.MissingKey.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (LlmServiceException ex)
            {
                // Clear UI content for failed run
                ResetRunFlowState();

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
                ResetRunFlowState();

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
                ResetRunFlowState();

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
                ResetRunFlowState();

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

        private void ResetRunFlowState()
        {
            ResponseSectionsPanel.Children.Clear();
            _lastResponseText = null;
            BadgeSearch.Visibility = Visibility.Collapsed;
        }

        private void RenderResponseSections(string raw)
        {
            _lastResponseText = raw;
            ResponseSectionsPanel.Children.Clear();

            var sections = _responseSections.SplitIntoSections(raw);

            foreach (var section in sections)
            {
                var expander = new Expander
                {
                    Header = section.Header,
                    IsExpanded = false,
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White
                };

                var box = new RichTextBox
                {
                    IsReadOnly = true,
                    Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x16)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 4, 8, 4),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    IsDocumentEnabled = true,
                    Document = _responseSections.BuildMarkdownDocument(section.Body)
                };

                expander.Content = box;
                ResponseSectionsPanel.Children.Add(expander);
            }
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

            var probe = ScreenCaptureService.TryDetectGameWindowTitle() ?? "";
            if (!probe.Contains("GMentor", StringComparison.OrdinalIgnoreCase))
                return probe;

            return "Unknown";
        }

        // --- UI helpers for request lifecycle ---
        private void ResetUiForNewRequest(string status)
        {
            BadgeSearch.Visibility = Visibility.Collapsed;
            ResponseSectionsPanel.Children.Clear();
            _lastResponseText = null;

            CopyBtn.IsEnabled = false;
            OpenYouTubeBtn.IsEnabled = false;
            StatusText.Text = status;
        }


        private void RestoreUiAfterRequest()
        {
            bool hasText = !string.IsNullOrWhiteSpace(_lastResponseText);

            CopyBtn.IsEnabled = hasText;
            OpenYouTubeBtn.IsEnabled = hasText;
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
            if (!string.IsNullOrWhiteSpace(_lastResponseText))
                Clipboard.SetText(_lastResponseText);
        }

        private void OnOpenYouTube(object sender, RoutedEventArgs e)
        {
            var text = _lastResponseText ?? string.Empty;
            var q = _lastYouTubeQuery ?? ResultParser.SynthesizeYouTubeQueryFrom(text);

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

        private void OnExpandAllSections(object sender, RoutedEventArgs e)
        {
            foreach (var expander in ResponseSectionsPanel.Children.OfType<Expander>())
                expander.IsExpanded = true;
        }

        private void OnCollapseAllSections(object sender, RoutedEventArgs e)
        {
            foreach (var expander in ResponseSectionsPanel.Children.OfType<Expander>())
                expander.IsExpanded = false;
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
