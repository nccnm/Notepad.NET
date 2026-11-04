using System.Runtime.InteropServices;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Services;

/// <summary>
/// The Find, Replace and Go To templates have no <c>DS_CENTER</c>, so the dialog manager places
/// them relative to the owner's client area; the Open, Save As and Font dialogs do the same. The
/// original's menu bar is non-client; the port's menu bar lives inside the client area, which
/// moves that origin up by the menu height. The dialogs are moved down by that amount before
/// they show, and kept inside the monitor's work area as the dialog manager does.
/// </summary>
internal static unsafe class DialogPlacement
{
    [ThreadStatic]
    private static int t_pendingOffset;

    public static void ShiftDown(nint dialog, int offset)
    {
        if (offset == 0 || !User32.GetWindowRect(dialog, out var rect))
        {
            return;
        }

        var height = rect.Bottom - rect.Top;
        var top = rect.Top + offset;
        var info = new User32.MONITORINFO { cbSize = Marshal.SizeOf<User32.MONITORINFO>() };
        if (User32.GetMonitorInfo(User32.MonitorFromRect(rect, User32.MONITOR_DEFAULTTONEAREST), ref info))
        {
            top = Math.Max(Math.Min(top, info.rcWork.Bottom - height), info.rcWork.Top);
        }

        User32.SetWindowPos(dialog, 0, rect.Left, top, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Runs <paramref name="show"/> (a modal dialog without a hook of its own: the shell file
    /// dialogs, <c>ChooseFontW</c>) with a thread CBT hook that shifts the first dialog window it
    /// activates. Activation comes before the dialog's first paint.
    /// </summary>
    public static T WhileShowing<T>(int offset, Func<T> show)
    {
        if (offset == 0)
        {
            return show();
        }

        var hook = User32.SetWindowsHookEx(User32.WH_CBT, &CbtProc, 0, Kernel32Ui.GetCurrentThreadId());
        t_pendingOffset = offset;
        try
        {
            return show();
        }
        finally
        {
            t_pendingOffset = 0;
            if (hook != 0)
            {
                User32.UnhookWindowsHookEx(hook);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint CbtProc(int code, nint wParam, nint lParam)
    {
        if (code == User32.HCBT_ACTIVATE && t_pendingOffset != 0 && IsDialog(wParam))
        {
            var offset = t_pendingOffset;
            t_pendingOffset = 0;
            ShiftDown(wParam, offset);
        }

        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool IsDialog(nint window)
    {
        var name = stackalloc char[8];
        var length = User32.GetClassName(window, name, 8);
        return new ReadOnlySpan<char>(name, length).SequenceEqual("#32770");
    }
}
