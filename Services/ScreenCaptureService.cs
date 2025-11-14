using GMentor.Views;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GMentor.Services
{
    public static class ScreenCaptureService
    {
        /// <summary>
        /// Captures the entire virtual desktop (all monitors) as a bitmap.
        /// </summary>
        public static Bitmap CaptureFullVirtualScreen()
        {
            var bounds = GetVirtualScreenBounds();
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        /// <summary>
        /// Captures a frozen full-screen image and opens the crop overlay.
        /// </summary>
        public static Bitmap? CaptureInteractiveRegion(System.Windows.Window owner)
        {
            using var full = CaptureFullVirtualScreen();
            var overlay = new CropWindow(full);
            overlay.Owner = owner;
            return overlay.ShowDialog() == true ? overlay.CroppedBitmap : null;
        }

        /// <summary>
        /// Attempts to detect the current foreground window title (for game name).
        /// </summary>
        public static string? TryDetectGameWindowTitle()
        {
            try
            {
                IntPtr h = NativeMethods.GetForegroundWindow();
                var title = NativeMethods.GetWindowText(h);
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }
            catch { }
            return System.Diagnostics.Process.GetCurrentProcess().MainWindowTitle;
        }

        // ---------------------------------------------------------------------
        // Helper: get full virtual screen bounds (multi-monitor safe)
        // ---------------------------------------------------------------------
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

        // ---------------------------------------------------------------------
        // Native helpers for window title
        // ---------------------------------------------------------------------
        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

            internal static string GetWindowText(IntPtr hwnd)
            {
                var sb = new System.Text.StringBuilder(512);
                _ = GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
        }
    }
}
