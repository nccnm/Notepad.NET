using System.Runtime.InteropServices;

namespace NotepadNet.PerfHarness;

internal static unsafe partial class Native
{
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const int OBJID_WINDOW = 0;
    public const uint GA_ROOT = 2;
    public const int GWL_STYLE = -16;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const uint WM_SETTEXT = 0x000C;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint EM_SETHANDLE = 0x00BC;
    public const uint EM_GETHANDLE = 0x00BD;
    public const uint EM_LIMITTEXT = 0x00C5;
    public const uint SMTO_NORMAL = 0;
    public const uint PM_REMOVE = 1;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint LMEM_MOVEABLE = 0x2;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_COMMAND = 0x0111;
    public const uint EM_SETSEL = 0x00B1;
    public const uint BM_GETCHECK = 0x00F0;
    public const uint BM_SETCHECK = 0x00F1;
    public const uint BM_CLICK = 0x00F5;
    public const int GWL_ID = -12;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x2;
    public const ushort VK_CONTROL = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)]
        public uint type;

        [FieldOffset(8)]
        public KEYBDINPUT ki;
    }

    [LibraryImport("user32.dll")]
    public static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll")]
    public static partial nint GetDlgItem(nint dialog, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
        public uint lPrivate;
    }

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint idProcess, uint idThread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLong(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    public static partial uint MsgWaitForMultipleObjects(uint count, nint* handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds, uint wakeMask);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(nint parent, delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? name);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    public static partial void* LocalLock(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LocalUnlock(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint handle);
}
