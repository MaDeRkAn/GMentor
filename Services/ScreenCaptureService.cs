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
        /// Temporarily releases any global mouse clipping (e.g. from games like EFT)
        /// so the cursor doesn't feel "stuck" when the overlay appears.
        /// </summary>
        public static Bitmap? CaptureInteractiveRegion(System.Windows.Window owner)
        {
            using var full = CaptureFullVirtualScreen();

            // Temporarily unclip the cursor (if a game clipped it)
            BeginUnclipCursor();
            try
            {
                var overlay = new CropWindow(full) { Owner = owner };
                return overlay.ShowDialog() == true ? overlay.CroppedBitmap : null;
            }
            finally
            {
                RestoreCursorClip();
            }
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

        // ---------------------------------------------------------------------
        // Mouse clipping handling (for games that call ClipCursor)
        // ---------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetClipCursor(out RECT lpRect);

        // Overload: restore a specific clip rectangle
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT lpRect);

        // Overload: pass IntPtr.Zero to free the cursor (no clipping)
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        private static bool _hadClip;
        private static RECT _previousClip;

        /// <summary>
        /// If some app (like EFT) has clipped the cursor, remember the clip and
        /// temporarily free it so the user can move the mouse on our overlay.
        /// </summary>
        private static void BeginUnclipCursor()
        {
            try
            {
                _hadClip = GetClipCursor(out _previousClip);
                // Even if _hadClip is false, calling ClipCursor(IntPtr.Zero) is safe.
                ClipCursor(IntPtr.Zero); // unclip -> full desktop
            }
            catch
            {
                // Fail-safe: never throw from capture code because of cursor APIs
                _hadClip = false;
            }
        }

        /// <summary>
        /// Restore the previous cursor clipping rectangle if there was one.
        /// </summary>
        private static void RestoreCursorClip()
        {
            try
            {
                if (_hadClip)
                {
                    ClipCursor(ref _previousClip);
                }
            }
            catch
            {
                // Ignore restore failures; OS will leave cursor unclipped
            }
            finally
            {
                _hadClip = false;
            }
        }
    }
}
