using System.Runtime.InteropServices;

namespace NotepadClassic.Platform.Interop;

/// <summary>GDI calls of the printer DC.</summary>
internal static partial class Gdi32
{
    public const int HORZRES = 8;
    public const int VERTRES = 10;
    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
    public const int PHYSICALWIDTH = 110;
    public const int PHYSICALHEIGHT = 111;
    public const int PHYSICALOFFSETX = 112;
    public const int PHYSICALOFFSETY = 113;
    public const int TRANSPARENT = 1;
    public const byte TMPF_VECTOR = 0x2;
    public const byte TMPF_TRUETYPE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public ushort tmFirstChar;
        public ushort tmLastChar;
        public ushort tmDefaultChar;
        public ushort tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DOCINFOW
    {
        public int cbSize;
        public nint lpszDocName;
        public nint lpszOutput;
        public nint lpszDatatype;
        public uint fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateDC(string? pwszDriver, string? pwszDevice, nint pszPort, nint pdm);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontIndirectW")]
    public static unsafe partial nint CreateFontIndirect(Comdlg32.LOGFONTW* lplf);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextMetrics(nint hdc, out TEXTMETRICW lptm);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetTextExtentPoint32(nint hdc, char* lpString, int c, out SIZE psizl);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool TextOut(nint hdc, int x, int y, char* lpString, int c);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static unsafe partial int SetAbortProc(nint hdc, delegate* unmanaged<nint, int, int> proc);

    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW", SetLastError = true)]
    public static unsafe partial int StartDoc(nint hdc, DOCINFOW* lpdi);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial int EndDoc(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial int AbortDoc(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial int StartPage(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial int EndPage(nint hdc);
}

internal static partial class User32
{
    public const uint DT_RIGHT = 0x2;
    public const uint DT_WORDBREAK = 0x10;
    public const uint DT_EXPANDTABS = 0x40;
    public const uint DT_NOPREFIX = 0x800;
    public const uint DT_EDITCONTROL = 0x2000;
    public const uint DT_RTLREADING = 0x2_0000;
    public const int WM_INITMENU = 0x0116;
    public const int WM_DESTROY = 0x0002;
    public const int WM_NOTIFY = 0x004E;
    public const int SC_CLOSE = 0xF060;
    public const uint MF_BYCOMMAND = 0x0;
    public const uint MF_GRAYED = 0x1;
    public const uint PM_REMOVE = 0x1;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_LIMITTEXT = 0x00C5;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_RTLREADING = 0x2000;
    public const int NM_CLICK = -2;
    public const int NM_RETURN = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWTEXTPARAMS
    {
        public uint cbSize;
        public int iTabLength;
        public int iLeftMargin;
        public int iRightMargin;
        public uint uiLengthDrawn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NMHDR
    {
        public nint hwndFrom;
        public nuint idFrom;
        public int code;
    }

    [LibraryImport("user32.dll", EntryPoint = "DrawTextExW")]
    public static unsafe partial int DrawTextEx(nint hdc, char* lpchText, int cchText, RECT* lprc, uint format, DRAWTEXTPARAMS* lpdtp);

    [LibraryImport("user32.dll", EntryPoint = "CreateDialogIndirectParamW", SetLastError = true)]
    public static unsafe partial nint CreateDialogIndirectParam(nint hInstance, byte* lpTemplate, nint hWndParent, delegate* unmanaged<nint, uint, nint, nint, nint> lpDialogFunc, nint dwInitParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowEnabled(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [LibraryImport("user32.dll")]
    public static partial int EnableMenuItem(nint hMenu, uint uIDEnableItem, uint uEnable);

    [LibraryImport("user32.dll")]
    public static partial uint GetMenuState(nint hMenu, uint uId, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "SendDlgItemMessageW")]
    public static partial nint SendDlgItemMessage(nint hDlg, int nIDDlgItem, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLong(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    public static partial nint GetDlgItem(nint hDlg, int nIDDlgItem);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);
}

internal static partial class Comdlg32
{
    public const uint PD_NOSELECTION = 0x4;
    public const uint PD_NOPAGENUMS = 0x8;
    public const uint PD_RETURNDC = 0x100;
    public const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x4_0000;
    public const uint PD_NOCURRENTPAGE = 0x80_0000;
    public const uint START_PAGE_GENERAL = 0xFFFF_FFFF;
    public const uint PD_RESULT_CANCEL = 0;
    public const uint PD_RESULT_PRINT = 1;
    public const uint PD_RESULT_APPLY = 2;

    public const uint PSD_MARGINS = 0x2;
    public const uint PSD_INTHOUSANDTHSOFINCHES = 0x4;
    public const uint PSD_INHUNDREDTHSOFMILLIMETERS = 0x8;
    public const uint PSD_NOWARNING = 0x80;
    public const uint PSD_RETURNDEFAULT = 0x400;
    public const uint PSD_ENABLEPAGESETUPHOOK = 0x2000;
    public const uint PSD_ENABLEPAGESETUPTEMPLATEHANDLE = 0x2_0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct PRINTDLGEXW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hDevMode;
        public nint hDevNames;
        public nint hDC;
        public uint Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint nPageRanges;
        public uint nMaxPageRanges;
        public nint lpPageRanges;
        public uint nMinPage;
        public uint nMaxPage;
        public uint nCopies;
        public nint hInstance;
        public nint lpPrintTemplateName;
        public nint lpCallback;
        public uint nPropertyPages;
        public nint lphPropertyPages;
        public uint nStartPage;
        public uint dwResultAction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAGESETUPDLGW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hDevMode;
        public nint hDevNames;
        public uint Flags;
        public POINT ptPaperSize;
        public User32.RECT rtMinMargin;
        public User32.RECT rtMargin;
        public nint hInstance;
        public nint lCustData;
        public nint lpfnPageSetupHook;
        public nint lpfnPagePaintHook;
        public nint lpPageSetupTemplateName;
        public nint hPageSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVNAMES
    {
        public ushort wDriverOffset;
        public ushort wDeviceOffset;
        public ushort wOutputOffset;
        public ushort wDefault;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "PrintDlgExW")]
    public static unsafe partial int PrintDlgEx(PRINTDLGEXW* lppd);

    [LibraryImport("comdlg32.dll", EntryPoint = "PageSetupDlgW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool PageSetupDlg(PAGESETUPDLGW* lppsd);

    [LibraryImport("comdlg32.dll")]
    public static partial int CommDlgExtendedError();
}

internal static partial class Kernel32Ui
{
    public const uint GMEM_FIXED = 0x0;
    public const uint GMEM_MOVEABLE = 0x2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint hMem);
}

internal static partial class WinSpool
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DRIVER_INFO_1W
    {
        public nint pName;
    }

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenPrinter(string pPrinterName, out nint phPrinter, nint pDefault);

    [LibraryImport("winspool.drv", EntryPoint = "GetPrinterDriverW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetPrinterDriver(nint hPrinter, char* pEnvironment, uint level, byte* pDriverInfo, uint cbBuf, out uint pcbNeeded);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClosePrinter(nint hPrinter);
}

internal static partial class Comctl32
{
    public const uint ICC_LINK_CLASS = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitCommonControlsEx(in INITCOMMONCONTROLSEX picce);
}
