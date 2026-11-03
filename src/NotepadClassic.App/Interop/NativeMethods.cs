using System.Runtime.InteropServices;

namespace NotepadClassic.App.Interop;

/// <summary>User32/GDI calls of the shell and the editor host.</summary>
internal static partial class NativeMethods
{
    public const int WM_SETREDRAW = 0x000B;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int WM_CLOSE = 0x0010;
    public const int WM_QUERYENDSESSION = 0x0011;
    public const int WM_ENDSESSION = 0x0016;
    public const nint ENDSESSION_CLOSEAPP = 0x1;
    public const int WM_SIZE = 0x0005;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_SETFOCUS = 0x0007;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_SETFONT = 0x0030;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_COMMAND = 0x0111;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int MK_SHIFT = 0x4;
    public const int MK_CONTROL = 0x8;
    public const int WHEEL_DELTA = 120;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_APPCOMMAND = 0x0319;
    public const int APPCOMMAND_BROWSER_SEARCH = 5;
    public const int WM_CUT = 0x0300;
    public const int WM_COPY = 0x0301;
    public const int WM_PASTE = 0x0302;
    public const int WM_CLEAR = 0x0303;
    public const int WM_REFLECT = 0x2000;

    public const int EM_GETSEL = 0x00B0;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_SCROLLCARET = 0x00B7;
    public const int EM_GETMODIFY = 0x00B8;
    public const int EM_SETMODIFY = 0x00B9;
    public const int EM_GETLINECOUNT = 0x00BA;
    public const int EM_LINEINDEX = 0x00BB;
    public const int EM_SETHANDLE = 0x00BC;
    public const int EM_GETHANDLE = 0x00BD;
    public const int EM_REPLACESEL = 0x00C2;
    public const int EM_LIMITTEXT = 0x00C5;
    public const int EM_CANUNDO = 0x00C6;
    public const int EM_UNDO = 0x00C7;
    public const int EM_LINEFROMCHAR = 0x00C9;
    public const int EM_EMPTYUNDOBUFFER = 0x00CD;
    public const int EM_GETZOOM = 0x04E0;
    public const int EM_SETZOOM = 0x04E1;
    public const int EM_SETEXTENDEDSTYLE = 0x150A;
    public const int EM_GETEXTENDEDSTYLE = 0x150B;
    public const int EM_GETENDOFLINE = 0x150D;
    public const int EM_ENABLESEARCHWEB = 0x150E;
    public const int EM_SEARCHWEB = 0x150F;
    public const int EM_GETCARETINDEX = 0x1512;
    public const int EM_FILELINEFROMCHAR = 0x1513;
    public const int EM_FILELINEINDEX = 0x1514;

    public const int EN_CHANGE = 0x0300;
    public const int EN_ERRSPACE = 0x0500;
    public const int EN_MAXTEXT = 0x0501;

    public const int ES_EX_ALLOWEOL_CR = 0x1;
    public const int ES_EX_ALLOWEOL_LF = 0x2;
    public const int ES_EX_CONVERT_EOL_ON_PASTE = 0x4;
    public const int ES_EX_ZOOMABLE = 0x10;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_RTLREADING = 0x2000;
    public const int GWLP_ID = -12;
    public const uint LMEM_MOVEABLE = 0x2;

    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MAXIMIZED = 2;
    public const int WA_ACTIVE = 1;
    public const int WA_CLICKACTIVE = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    public const int SM_CYMENU = 15;
    public const uint WS_CHILD = 0x40000000;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_MBUTTON = 0x04;

    public delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        public fixed char lfFaceName[32];
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    public static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    public const int SB_SETPARTS = 0x0404;
    public const int SB_SETTEXTW = 0x040B;
    public const int WM_PRINTCLIENT = 0x0318;
    public const int PRF_CLIENT = 0x4;
    public const int PRF_ERASEBKGND = 0x8;

    public const int SP_GRIPPER = 3;

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenThemeDataForDpi(nint hwnd, string pszClassList, uint dpi);

    [LibraryImport("uxtheme.dll")]
    public static partial int CloseThemeData(nint hTheme);

    public const int TS_DRAW = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;
    }

    [LibraryImport("uxtheme.dll")]
    public static partial int GetThemePartSize(nint hTheme, nint hdc, int iPartId, int iStateId, in RECT prc, int eSize, out SIZE psz);

    [LibraryImport("uxtheme.dll")]
    public static partial int DrawThemeBackground(nint hTheme, nint hdc, int iPartId, int iStateId, in RECT pRect, nint pClipRect);

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint MF_STRING = 0x0;
    public const uint SPI_GETNONCLIENTMETRICS = 0x29;

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    public static partial nint GetMenu(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowMenu(nint hWnd, nint hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMenuItemRect(nint hWnd, nint hMenu, uint uItem, out RECT lprcItem);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    /// <summary>The <c>LOGFONTW</c> of <c>NONCLIENTMETRICSW</c>, as <see cref="Font.FromLogFont(object)"/> reads it.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public sealed class LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName = "";
    }

    /// <summary><c>NONCLIENTMETRICSW</c> (with <c>iPaddedBorderWidth</c>), 504 bytes.</summary>
    public const int NonClientMetricsSize = 504;
    public const int NonClientMetricsMenuFont = 224;
    public const int NonClientMetricsStatusFont = 316;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SystemParametersInfoForDpi(uint uiAction, uint uiParam, void* pvParam, uint fWinIni, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsChild(nint hWndParent, nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int nVirtKey);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hWinEventHook);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontIndirectW")]
    public static unsafe partial nint CreateFontIndirect(LOGFONTW* lplf);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextFaceW")]
    public static unsafe partial int GetTextFace(nint hdc, int c, char* lpName);

    [LibraryImport("gdi32.dll", EntryPoint = "EnumFontsW")]
    public static unsafe partial int EnumFonts(nint hdc, char* lpLogfont, delegate* unmanaged<LOGFONTW*, nint, uint, nint, int> lpProc, nint lParam);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint LocalAlloc(uint uFlags, nuint uBytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LocalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint hMem);

    public const int WM_DROPFILES = 0x0233;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_INITMENUPOPUP = 0x0117;
    public const int GWL_STYLE = -16;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_MAXIMIZE = 0xF030;
    public const uint MF_GRAYED = 0x1;
    public const uint MF_DISABLED = 0x2;
    public const nint DPI_AWARENESS_CONTEXT_UNAWARE = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPlacement(nint hWnd, in WINDOWPLACEMENT lpwndpl);

    [LibraryImport("user32.dll")]
    public static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public const uint SWP_NOZORDER = 0x4;
    public const uint SWP_NOACTIVATE = 0x10;
    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_FRAMECHANGED = 0x20;

    [LibraryImport("user32.dll")]
    public static partial nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [LibraryImport("user32.dll")]
    public static partial int EnableMenuItem(nint hMenu, uint uIDEnableItem, uint uEnable);

    [LibraryImport("shell32.dll")]
    public static partial void DragAcceptFiles(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    public static unsafe partial uint DragQueryFile(nint hDrop, uint iFile, char* lpszFile, uint cch);

    [LibraryImport("shell32.dll")]
    public static partial void DragFinish(nint hDrop);

    [LibraryImport("user32.dll")]
    public static partial nint SetActiveWindow(nint hWnd);

    public static int LowWord(nint value) => (int)((long)value & 0xFFFF);

    public static int HighWord(nint value) => (int)(((long)value >> 16) & 0xFFFF);
}
