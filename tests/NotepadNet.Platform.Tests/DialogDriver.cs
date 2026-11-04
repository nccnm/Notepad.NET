using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NotepadNet.Platform.Tests;

/// <summary>
/// Drives a modal dialog of the test thread from a second thread: waits for the dialog with the
/// given title and runs the script on it. A failing script cancels the dialog so the test thread
/// never hangs; its exception is rethrown by <see cref="Join"/>.
/// </summary>
internal sealed class DialogDriver
{
    private readonly Thread _thread;
    private Exception? _error;

    /// <param name="thread">The test thread, which owns every window the script looks for;
    /// windows of earlier tests are on other threads.</param>
    private DialogDriver(uint thread, string title, Action<uint, nint> script)
    {
        _thread = new Thread(() =>
        {
            nint dialog = 0;
            try
            {
                dialog = DialogWait.ForWindow(thread, title);
                script(thread, dialog);
            }
#pragma warning disable CA1031 // Reported to the test thread by Join.
            catch (Exception e)
#pragma warning restore CA1031
            {
                _error = e;
                if (dialog != 0)
                {
                    Native.PostMessage(dialog, Native.WM_COMMAND, 2, 0);
                }
            }
        })
        { IsBackground = true };
        _thread.Start();
    }

    public static DialogDriver Run(string title, Action<uint, nint> script) => new(Native.GetCurrentThreadId(), title, script);

    public void Join()
    {
        _thread.Join();
        if (_error is not null)
        {
            throw new InvalidOperationException("The dialog script failed.", _error);
        }
    }
}

internal static class DialogWait
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static nint ForWindow(uint thread, string title)
    {
        nint window = 0;
        Until(() => (window = Native.FindThreadDialog(thread, title)) != 0);
        return window;
    }

    public static void Until(Func<bool> condition, Func<string>? state = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Timeout)
            {
                throw new TimeoutException("The dialog did not reach the expected state. " + state?.Invoke());
            }

            Thread.Sleep(10);
        }
    }
}

internal static partial class Native
{
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_CLOSE = 0x0010;
    public const int WM_CHAR = 0x0102;
    public const int WM_COMMAND = 0x0111;
    public const int EM_GETSEL = 0x00B0;
    public const uint GW_OWNER = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumThreadWindows(uint thread, delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint window, char* name, int count);

    [ThreadStatic]
    private static List<nint>? s_windows;

    [UnmanagedCallersOnly]
    private static int Collect(nint window, nint lParam)
    {
        s_windows!.Add(window);
        return 1;
    }

    /// <summary>A top-level dialog of <paramref name="thread"/> with this title.</summary>
    public static unsafe nint FindThreadDialog(uint thread, string title)
    {
        s_windows = [];
        EnumThreadWindows(thread, &Collect, 0);
        var name = stackalloc char[16];
        foreach (var window in s_windows)
        {
            var length = GetClassName(window, name, 16);
            if (new string(name, 0, length) == "#32770" && Text(window) == title)
            {
                return window;
            }
        }

        return 0;
    }

    [LibraryImport("user32.dll")]
    public static partial nint GetDlgItem(nint dialog, int id);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint window, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessage(nint window, int msg, nint wParam, string lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint window, int msg, out int wParam, out int lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static unsafe partial nint SendMessage(nint window, int msg, nint wParam, char* lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint window, nint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetWindowRect(nint window, int* rect);

    public static unsafe int Top(nint window)
    {
        var rect = stackalloc int[4];
        return GetWindowRect(window, rect) ? rect[1] : int.MinValue;
    }

    public static unsafe string Text(nint window)
    {
        var buffer = stackalloc char[512];
        var length = (int)SendMessage(window, WM_GETTEXT, 512, buffer);
        return new string(buffer, 0, length);
    }

    public static nint FocusOf(nint window)
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(window, 0), ref info) ? info.hwndFocus : 0;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    /// <summary>A hidden top-level window of the calling thread to own dialogs.</summary>
    public static nint CreateOwner() => CreateWindowEx(0, "STATIC", "owner", unchecked((int)0x00CF0000), 0, 0, 400, 300, 0, 0, 0, 0);

    public static void DestroyOwner(nint window) => DestroyWindow(window);
}
