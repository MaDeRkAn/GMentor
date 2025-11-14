using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GMentor.Views
{
    /// <summary>
    /// Full-screen, borderless overlay that shows a frozen screenshot 1:1 (no scaling).
    /// Selection rectangle operates in virtual-screen pixel coords; crop is pixel-perfect.
    /// </summary>
    public class CropWindow : Window
    {
        private readonly Bitmap _frozen;
        private readonly System.Drawing.Rectangle _virtual; // device coords
        private System.Windows.Point? _start;
        private readonly System.Windows.Shapes.Rectangle _rect;

        public Bitmap? CroppedBitmap { get; private set; }

        public CropWindow(Bitmap frozenFullScreen)
        {
            _frozen = (Bitmap)frozenFullScreen.Clone();
            _virtual = GetVirtualScreenBounds();

            // Window chrome-less overlay exactly covering virtual screen (device coords)
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            AllowsTransparency = false;

            // Show frozen background 1:1 (no scaling)
            Background = new ImageBrush(ToBitmapSource(_frozen))
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            Left = _virtual.Left;
            Top = _virtual.Top;
            Width = _virtual.Width;
            Height = _virtual.Height;

            // Visual selection rectangle
            _rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.DeepSkyBlue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 30, 144, 255))
            };

            // Root overlay canvas
            var canvas = new Canvas { IsHitTestVisible = true };
            Content = canvas;
            canvas.Children.Add(_rect);
            Canvas.SetLeft(_rect, -10000); // hide initially

            Cursor = Cursors.Cross;

            // Input
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;

            // Escape to cancel
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }

        private void OnDown(object? s, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(this);
            CaptureMouse();
        }

        private void OnMove(object? s, MouseEventArgs e)
        {
            if (_start is null) return;
            var p = e.GetPosition(this);

            var x = Math.Min(_start.Value.X, p.X);
            var y = Math.Min(_start.Value.Y, p.Y);
            var w = Math.Abs(_start.Value.X - p.X);
            var h = Math.Abs(_start.Value.Y - p.Y);

            // Clamp to window bounds
            x = Math.Max(0, Math.Min(x, ActualWidth));
            y = Math.Max(0, Math.Min(y, ActualHeight));
            w = Math.Max(0, Math.Min(w, ActualWidth - x));
            h = Math.Max(0, Math.Min(h, ActualHeight - y));

            Canvas.SetLeft(_rect, x);
            Canvas.SetTop(_rect, y);
            _rect.Width = w;
            _rect.Height = h;
        }

        private void OnUp(object? s, MouseButtonEventArgs e)
        {
            if (_start is null) { DialogResult = false; Close(); return; }
            ReleaseMouseCapture();

            var end = e.GetPosition(this);

            // Window uses the same pixel grid as the virtual screen (Stretch=None, window size == virtual bounds)
            int left = (int)Math.Round(Math.Min(_start.Value.X, end.X));
            int top = (int)Math.Round(Math.Min(_start.Value.Y, end.Y));
            int width = (int)Math.Round(Math.Abs(_start.Value.X - end.X));
            int height = (int)Math.Round(Math.Abs(_start.Value.Y - end.Y));

            if (width < 8 || height < 8) { DialogResult = false; Close(); return; }

            var rect = new System.Drawing.Rectangle(left, top, width, height);
            rect.Intersect(new System.Drawing.Rectangle(0, 0, _frozen.Width, _frozen.Height));
            CroppedBitmap = _frozen.Clone(rect, _frozen.PixelFormat);

            DialogResult = true;
            Close();
        }

        // ----------------- Helpers -----------------

        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private static System.Drawing.Rectangle GetVirtualScreenBounds()
        {
            int left = GetSystemMetrics(SystemMetric.SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SystemMetric.SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SystemMetric.SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SystemMetric.SM_CYVIRTUALSCREEN);
            return new System.Drawing.Rectangle(left, top, width, height);
        }

        private enum SystemMetric
        {
            SM_XVIRTUALSCREEN = 76,
            SM_YVIRTUALSCREEN = 77,
            SM_CXVIRTUALSCREEN = 78,
            SM_CYVIRTUALSCREEN = 79
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(SystemMetric smIndex);
    }
}
