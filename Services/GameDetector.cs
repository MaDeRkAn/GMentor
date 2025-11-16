namespace GMentor.Services
{
    /// <summary>
    /// Polls foreground window title every N seconds, debounced.
    /// Raises GameChanged when stable and different from previous.
    /// </summary>
    public sealed class GameDetector : IDisposable
    {
        private readonly Timer _timer;
        private readonly TimeSpan _poll;
        private readonly TimeSpan _debounce;
        private string _lastStable = "";
        private string _pending = "";
        private DateTime _firstSeen = DateTime.MinValue;

        public event EventHandler<string>? GameChanged; // passes window title

        public GameDetector(TimeSpan? pollInterval = null, TimeSpan? debounce = null)
        {
            _poll = pollInterval ?? TimeSpan.FromSeconds(10);
            _debounce = debounce ?? TimeSpan.FromSeconds(20);
            _timer = new Timer(_ => Tick(), null, _poll, _poll);
        }

        private void Tick()
        {
            try
            {
                var title = ScreenCaptureService.TryDetectGameWindowTitle() ?? "";
                if (string.IsNullOrWhiteSpace(title)) return;

                if (!string.Equals(title, _pending, StringComparison.OrdinalIgnoreCase))
                {
                    _pending = title;
                    _firstSeen = DateTime.UtcNow;
                    return;
                }

                if (DateTime.UtcNow - _firstSeen < _debounce) return;

                if (!string.Equals(_pending, _lastStable, StringComparison.OrdinalIgnoreCase))
                {
                    _lastStable = _pending;
                    GameChanged?.Invoke(this, _lastStable);
                }
            }
            catch { /* ignore */ }
        }

        public void Dispose() => _timer.Dispose();
    }
}
