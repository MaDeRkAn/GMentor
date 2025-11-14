using System;
using System.Runtime.InteropServices;
using System.Windows;

public static class MessageBoxEx
{
    private static IntPtr _ownerHandle;

    public static MessageBoxResult Show(Window owner, string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        _ownerHandle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;

        var hook = new Win32WindowHook();
        hook.CenterNextMessageBoxOn(_ownerHandle);

        return MessageBox.Show(owner, message, caption, buttons, icon);
    }
}

internal class Win32WindowHook
{
    private IntPtr _owner;

    public void CenterNextMessageBoxOn(IntPtr owner)
    {
        _owner = owner;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            new Action(() =>
            {
                var callback = new WinEventDelegate(WinEventProc);
                SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero,
                    callback, 0, 0, WINEVENT_OUTOFCONTEXT);
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint idThread, uint time)
    {
        // Only reposition actual message boxes (class name "#32770")
        var classText = new string('\0', 256);
        GetClassName(hwnd, classText, 256);
        if (!classText.Contains("#32770")) return;

        CenterWindow(hwnd, _owner);
    }

    private static void CenterWindow(IntPtr child, IntPtr parent)
    {
        RECT parentRect;
        RECT childRect;
        GetWindowRect(parent, out parentRect);
        GetWindowRect(child, out childRect);

        int width = childRect.Right - childRect.Left;
        int height = childRect.Bottom - childRect.Top;

        int newLeft = parentRect.Left + ((parentRect.Right - parentRect.Left) - width) / 2;
        int newTop = parentRect.Top + ((parentRect.Bottom - parentRect.Top) - height) / 2;

        SetWindowPos(child, IntPtr.Zero, newLeft, newTop, width, height, 0);
    }

    // Win32 imports
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint idThread, uint time);

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    static extern int GetClassName(IntPtr hWnd, string lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
