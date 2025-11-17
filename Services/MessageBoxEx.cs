using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public static class MessageBoxEx
{
    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var ownerHandle = new WindowInteropHelper(owner).Handle;

        using (var hook = new Win32MessageBoxCenterHook(ownerHandle))
        {
            // This is still a normal, blocking WPF MessageBox.Show – just centered.
            return MessageBox.Show(owner, message, caption, buttons, icon);
        }
    }
}

internal sealed class Win32MessageBoxCenterHook : IDisposable
{
    private readonly IntPtr _owner;
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hookHandle;

    public Win32MessageBoxCenterHook(IntPtr owner)
    {
        _owner = owner;

        // Keep delegate alive
        _callback = WinEventProc;

        // Hook only OBJECT_SHOW events, out-of-context
        _hookHandle = SetWinEventHook(
            EVENT_OBJECT_SHOW,
            EVENT_OBJECT_SHOW,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);
    }

    private void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idThread,
        uint time)
    {
        // Only reposition common dialog/message box windows (class "#32770")
        var className = new string('\0', 256);
        GetClassName(hwnd, className, className.Length);

        if (!className.Contains("#32770"))
            return;

        CenterWindowOnOwner(hwnd, _owner);

        // Unhook after we centered the first message box
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_hookHandle);
        }
    }

    private static void CenterWindowOnOwner(IntPtr child, IntPtr parent)
    {
        if (parent == IntPtr.Zero)
            return;

        if (!GetWindowRect(parent, out var parentRect))
            return;

        if (!GetWindowRect(child, out var childRect))
            return;

        int width = childRect.Right - childRect.Left;
        int height = childRect.Bottom - childRect.Top;

        int newLeft = parentRect.Left + ((parentRect.Right - parentRect.Left) - width) / 2;
        int newTop = parentRect.Top + ((parentRect.Bottom - parentRect.Top) - height) / 2;

        SetWindowPos(child, IntPtr.Zero, newLeft, newTop, width, height, 0);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_hookHandle);
        }
        // delegate is instance field; GC can collect it after dispose
    }

    // ---- Win32 interop ----

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idThread,
        uint time);

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr hWnd,
        string lpClassName,
        int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(
        IntPtr hwnd,
        out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
