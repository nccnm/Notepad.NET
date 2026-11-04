using System.Runtime.InteropServices;

namespace NotepadClassic.Platform.Interop;

internal static partial class Kernel32Ui
{
    public const uint LMEM_MOVEABLE = 0x2;
    public const uint TIME_NOSECONDS = 0x2;
    public const uint LOCALE_USER_DEFAULT = 0x400;
    public const uint LOCALE_FONTSIGNATURE = 0x58;
    public const uint LOCALE_ICALENDARTYPE = 0x1009;
    public const uint LOCALE_RETURN_NUMBER = 0x2000_0000;
    public const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x200;
    public const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        // Each FILETIME is two DWORDs (4-byte aligned); a long here would add padding.
        public uint ftCreationTimeLow;
        public uint ftCreationTimeHigh;
        public uint ftLastAccessTimeLow;
        public uint ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow;
        public uint ftLastWriteTimeHigh;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint LocalAlloc(uint uFlags, nuint uBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint LocalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LocalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint LocalFree(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial nuint LocalSize(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial int MulDiv(int nNumber, int nNumerator, int nDenominator);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetTimeFormatEx(string? lpLocaleName, uint dwFlags, SYSTEMTIME* lpTime, string? lpFormat, char* lpTimeStr, int cchTime);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetDateFormatEx(string? lpLocaleName, uint dwFlags, SYSTEMTIME* lpDate, string? lpFormat, char* lpDateStr, int cchDate, string? lpCalendar);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLocaleInfoW", SetLastError = true)]
    public static unsafe partial int GetLocaleInfo(uint locale, uint lcType, char* lpLCData, int cchData);

    [LibraryImport("kernel32.dll")]
    public static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("kernel32.dll", EntryPoint = "FormatMessageW", SetLastError = true)]
    public static unsafe partial int FormatMessage(uint dwFlags, nint lpSource, uint dwMessageId, uint dwLanguageId, char* lpBuffer, int nSize, nint arguments);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial nint FindFirstFile(string lpFileName, WIN32_FIND_DATAW* lpFindFileData);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(nint hFindFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? lpModuleName);

    public const uint LOCALE_IMEASURE = 0xD;
    public const int STARTF_UNTRUSTEDSOURCE = 0x8000;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFullPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetFullPathName(string lpFileName, int nBufferLength, char* lpBuffer, nint lpFilePart);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    public static unsafe partial char* GetCommandLine();

    [LibraryImport("kernel32.dll", EntryPoint = "GetStartupInfoW")]
    public static unsafe partial void GetStartupInfo(STARTUPINFOW* lpStartupInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }
}

internal static partial class ShlwApi
{
    [LibraryImport("shlwapi.dll", EntryPoint = "PathFindExtensionW")]
    public static unsafe partial char* PathFindExtension(char* pszPath);
}

internal static partial class User32
{
    public const uint CF_TEXT = 1;
    public const uint CF_UNICODETEXT = 13;
    public const uint MB_OKCANCEL = 0x1;
    public const uint MB_YESNOCANCEL = 0x3;
    public const uint MB_YESNO = 0x4;
    public const uint MB_ICONHAND = 0x10;
    public const uint MB_ICONQUESTION = 0x20;
    public const uint MB_ICONEXCLAMATION = 0x30;
    public const uint MB_DEFBUTTON2 = 0x100;
    public const uint MB_SYSTEMMODAL = 0x1000;
    public const int IDOK = 1;
    public const int IDCANCEL = 2;
    public const int IDYES = 6;
    public const int IDNO = 7;
    public const int LOGPIXELSY = 90;
    public const nint DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2;
    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTSIZE = 0x40;
    public const uint LR_SHARED = 0x8000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    public static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MessageBeep(uint uType);

    [LibraryImport("user32.dll")]
    public static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW")]
    public static partial nint LoadImage(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);
}

internal static partial class Gdi32
{
    [LibraryImport("gdi32.dll")]
    public static partial int GetDeviceCaps(nint hdc, int index);
}

internal static partial class Shell32
{
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOW = 5;
    public const uint SHARD_PATHW = 3;

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecute(nint hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial void SHAddToRecentDocs(uint uFlags, string pv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string name, nint bindContext, out nint itemIdList, uint attributesWanted, out uint attributes);

    [LibraryImport("shell32.dll")]
    public static partial int SHOpenFolderAndSelectItems(nint itemIdList, uint count, nint children, uint flags);

    [LibraryImport("shell32.dll", EntryPoint = "ShellAboutW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ShellAbout(nint hWnd, string szApp, string szOtherStuff, nint hIcon);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);
}

internal static partial class Comdlg32
{
    public const uint CF_SCREENFONTS = 0x1;
    public const uint CF_INITTOLOGFONTSTRUCT = 0x40;
    public const uint CF_FORCEFONTEXIST = 0x1_0000;
    public const uint CF_NOVERTFONTS = 0x100_0000;
    public const ushort SCREEN_FONTTYPE = 0x2000;

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

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CHOOSEFONTW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hDC;
        public LOGFONTW* lpLogFont;
        public int iPointSize;
        public uint Flags;
        public uint rgbColors;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint hInstance;
        public nint lpszStyle;
        public ushort nFontType;
        public ushort missingAlignment;
        public int nSizeMin;
        public int nSizeMax;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "ChooseFontW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ChooseFont(CHOOSEFONTW* lpcf);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetFileTitleW", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial short GetFileTitle(string lpszFile, char* buf, ushort cchSize);
}

internal static partial class Comctl32
{
    public const int TDCBF_CANCEL_BUTTON = 0x8;
    public const int TDF_ALLOW_DIALOG_CANCELLATION = 0x8;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TASKDIALOG_BUTTON
    {
        public int nButtonID;
        public nint pszButtonText;
    }

    /// <summary>commctrl.h declares the task dialog structures inside pshpack1.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public nint hwndParent;
        public nint hInstance;
        public int dwFlags;
        public int dwCommonButtons;
        public nint pszWindowTitle;
        public nint hMainIcon;
        public nint pszMainInstruction;
        public nint pszContent;
        public uint cButtons;
        public TASKDIALOG_BUTTON* pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public nint pRadioButtons;
        public int nDefaultRadioButton;
        public nint pszVerificationText;
        public nint pszExpandedInformation;
        public nint pszExpandedControlText;
        public nint pszCollapsedControlText;
        public nint hFooterIcon;
        public nint pszFooter;
        public nint pfCallback;
        public nint lpCallbackData;
        public uint cxWidth;
    }

    [LibraryImport("comctl32.dll")]
    public static unsafe partial int TaskDialogIndirect(TASKDIALOGCONFIG* pTaskConfig, int* pnButton, int* pnRadioButton, int* pfVerificationFlagChecked);
}

internal static partial class Ole32
{
    public const uint CLSCTX_INPROC_SERVER = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint pv);
}
