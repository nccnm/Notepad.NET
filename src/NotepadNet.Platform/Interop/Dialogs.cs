using System.Runtime.InteropServices;

namespace NotepadNet.Platform.Interop;

/// <summary>NLS calls of Find and Go To.</summary>
internal static partial class Kernel32Nls
{
    public const uint LOCALE_USER_DEFAULT = 0x400;
    public const uint NORM_IGNORECASE = 0x1;
    public const uint FIND_FROMSTART = 0x40_0000;
    public const uint FIND_FROMEND = 0x80_0000;
    public const uint MAP_FOLDDIGITS = 0x80;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial int FindNLSString(
        uint locale, uint dwFindNLSStringFlags, char* lpStringSource, int cchSource, char* lpStringValue, int cchValue, int* pcchFound);

    [LibraryImport("kernel32.dll", EntryPoint = "FoldStringW", SetLastError = true)]
    public static unsafe partial int FoldString(uint dwMapFlags, char* lpSrcStr, int cchSrc, char* lpDestStr, int cchDest);
}

internal static partial class Kernel32Ui
{
    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();
}

internal static partial class User32
{
    public const int WM_INITDIALOG = 0x0110;
    public const int WM_COMMAND = 0x0111;
    public const int WM_CLOSE = 0x0010;
    public const int WM_USER = 0x0400;
    public const int DWLP_USER = 0x10;
    public const uint MB_OK = 0x0;
    public const uint MB_ICONINFORMATION = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOZORDER = 0x4;
    public const uint SWP_NOACTIVATE = 0x10;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public const int WH_CBT = 5;
    public const int HCBT_ACTIVATE = 5;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static unsafe partial nint SetWindowsHookEx(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static unsafe partial int GetClassName(nint hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(in RECT lprc, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "IsDialogMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool IsDialogMessage(nint hDlg, MSG* lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetMessageTime();

    [LibraryImport("user32.dll")]
    public static partial uint GetMessagePos();

    [LibraryImport("user32.dll", EntryPoint = "DialogBoxIndirectParamW", SetLastError = true)]
    public static unsafe partial nint DialogBoxIndirectParam(
        nint hInstance, void* hDialogTemplate, nint hWndParent, delegate* unmanaged<nint, uint, nint, nint, nint> lpDialogFunc, nint dwInitParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndDialog(nint hDlg, nint nResult);

    [LibraryImport("user32.dll", EntryPoint = "SetDlgItemTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetDlgItemText(nint hDlg, int nIDDlgItem, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "GetDlgItemTextW")]
    public static unsafe partial uint GetDlgItemText(nint hDlg, int nIDDlgItem, char* lpString, int cchMax);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);
}

internal static partial class Comdlg32
{
    public const uint FR_DOWN = 0x1;
    public const uint FR_MATCHCASE = 0x4;
    public const uint FR_FINDNEXT = 0x8;
    public const uint FR_REPLACE = 0x10;
    public const uint FR_REPLACEALL = 0x20;
    public const uint FR_DIALOGTERM = 0x40;
    public const uint FR_ENABLEHOOK = 0x100;
    public const uint FR_HIDEWHOLEWORD = 0x1_0000;
    public const uint FR_SHOWWRAPAROUND = 0x4_0000;
    public const uint FR_WRAPAROUND = 0x10_0000;
    public const string FINDMSGSTRING = "commdlg_FindReplace";

    /// <summary><c>WM_USER + 100</c>: result bits back to the dialog, which announces them.</summary>
    public const int FindResultMessage = 0x464;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FINDREPLACEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public uint Flags;
        public char* lpstrFindWhat;
        public char* lpstrReplaceWith;
        public ushort wFindWhatLen;
        public ushort wReplaceWithLen;
        public nint lCustData;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnHook;
        public nint lpTemplateName;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "FindTextW")]
    public static unsafe partial nint FindText(FINDREPLACEW* lpfr);

    [LibraryImport("comdlg32.dll", EntryPoint = "ReplaceTextW")]
    public static unsafe partial nint ReplaceText(FINDREPLACEW* lpfr);
}
