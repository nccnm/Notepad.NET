using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Services;
using NotepadClassic.Platform.Interop;

namespace NotepadClassic.Platform.Services;

public sealed class SystemClock : IClock
{
    public DateTime LocalNow => DateTime.Now;
}

/// <summary>
/// Short time without seconds and short date of the user locale (<c>InsertTimeDate</c>),
/// with the reading-order flag of <see cref="DateFlagRules"/>; time with
/// seconds and long date for the print header and footer.
/// </summary>
public sealed unsafe class Win32DateTimeFormatter : IDateTimeFormatter
{
    private const int BufferLength = 80;

    public string FormatShortTime(DateTime localTime)
    {
        var time = ToSystemTime(localTime);
        var buffer = stackalloc char[BufferLength];
        var length = Kernel32Ui.GetTimeFormatEx(null, Kernel32Ui.TIME_NOSECONDS, &time, null, buffer, BufferLength);
        return length > 0 ? new string(buffer, 0, length - 1) : "";
    }

    public string FormatShortDate(DateTime localTime)
    {
        var date = ToSystemTime(localTime);
        var buffer = stackalloc char[BufferLength];
        var length = Kernel32Ui.GetDateFormatEx(null, DateFlags(), &date, null, buffer, BufferLength, null);
        return length > 0 ? new string(buffer, 0, length - 1) : "";
    }

    /// <summary>Print jobs read the time and long date into 256-WCHAR buffers.</summary>
    private const int PrintBufferLength = 256;

    private const uint DATE_LONGDATE = 0x2;

    public string FormatTime(DateTime localTime)
    {
        var time = ToSystemTime(localTime);
        var buffer = stackalloc char[PrintBufferLength];
        var length = Kernel32Ui.GetTimeFormatEx(null, 0, &time, null, buffer, PrintBufferLength);
        return length > 0 ? new string(buffer, 0, length - 1) : "";
    }

    public string FormatLongDate(DateTime localTime)
    {
        var date = ToSystemTime(localTime);
        var buffer = stackalloc char[PrintBufferLength];
        var length = Kernel32Ui.GetDateFormatEx(null, DATE_LONGDATE, &date, null, buffer, PrintBufferLength, null);
        return length > 0 ? new string(buffer, 0, length - 1) : "";
    }

    /// <summary>The <c>GetDateFormat</c> flags the original picks for the current user.</summary>
    public static uint DateFlags() =>
        DateFlagRules.Select(IsRtlLocale(Kernel32Ui.LOCALE_USER_DEFAULT), CalendarType, () => IsRtlLocale(Kernel32Ui.GetUserDefaultUILanguage()));

    /// <summary><c>IsRtlLocale</c>: bit <c>0x800</c> of the eighth WORD of <c>LOCALE_FONTSIGNATURE</c>.</summary>
    public static bool IsRtlLocale(uint lcid)
    {
        var signature = stackalloc char[16];
        return Kernel32Ui.GetLocaleInfo(lcid, Kernel32Ui.LOCALE_FONTSIGNATURE, signature, 16) != 0 && (signature[7] & 0x800) != 0;
    }

    private static int CalendarType()
    {
        int value;
        return Kernel32Ui.GetLocaleInfo(
            Kernel32Ui.LOCALE_USER_DEFAULT, Kernel32Ui.LOCALE_ICALENDARTYPE | Kernel32Ui.LOCALE_RETURN_NUMBER, (char*)&value, sizeof(int) / sizeof(char)) != 0
            ? value
            : 0;
    }

    private static Kernel32Ui.SYSTEMTIME ToSystemTime(DateTime t) => new()
    {
        wYear = (ushort)t.Year,
        wMonth = (ushort)t.Month,
        wDayOfWeek = (ushort)t.DayOfWeek,
        wDay = (ushort)t.Day,
        wHour = (ushort)t.Hour,
        wMinute = (ushort)t.Minute,
        wSecond = (ushort)t.Second,
        wMilliseconds = (ushort)t.Millisecond,
    };
}

/// <summary>Paste is enabled when the clipboard opens and offers <c>CF_TEXT</c>.</summary>
public sealed class Win32Clipboard : IClipboard
{
    public bool ContainsText()
    {
        if (!User32.OpenClipboard(0))
        {
            return false;
        }

        try
        {
            return User32.IsClipboardFormatAvailable(User32.CF_TEXT);
        }
        finally
        {
            User32.CloseClipboard();
        }
    }
}

public sealed class Win32Shell : IShell
{
    public void OpenUri(string uri) => Shell32.ShellExecute(0, null, uri, null, null, Shell32.SW_SHOWNORMAL);

    /// <summary>Deliberate difference (parity.md): the original runs its own module path with an
    /// empty parameter string; here that would mean "restore the next closed note".</summary>
    public void StartNewWindow() => Start(CommandLineParser.NewWindowOption);

    public void StartNextNoteWindow() => Start("");

    private static void Start(string parameters)
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            Shell32.ShellExecute(0, null, exe, parameters, null, Shell32.SW_SHOW);
        }
    }

    public void AddToRecentDocuments(string path) => Shell32.SHAddToRecentDocs(Shell32.SHARD_PATHW, path);
}

/// <summary>
/// Title-bar name: the on-disk spelling from <c>FindFirstFileW</c> (the path itself when not
/// found), then <c>GetFileTitleW</c>, which hides the extension when Explorer hides known
/// extensions. Deliberate difference (parity.md): the original only calls <c>GetFileTitleW</c>
/// once comdlg32 is loaded and otherwise cuts after the last backslash.
/// </summary>
public sealed unsafe class Win32FileTitleProvider : IFileTitleProvider
{
    public string GetTitle(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = OnDiskName(path) ?? path;
        var buffer = stackalloc char[260];
        var result = Comdlg32.GetFileTitle(name, buffer, 260);
        if (result == 0)
        {
            return new string(buffer);
        }

        var slash = name.LastIndexOfAny(['\\', '/']);
        return slash < 0 ? name : name[(slash + 1)..];
    }

    private static string? OnDiskName(string path)
    {
        Kernel32Ui.WIN32_FIND_DATAW data;
        var find = Kernel32Ui.FindFirstFile(path, &data);
        if (find == -1)
        {
            return null;
        }

        Kernel32Ui.FindClose(find);
        return new string(data.cFileName);
    }
}

public sealed unsafe class Win32SystemErrorMessages : ISystemErrorMessages
{
    public string? Find(int win32Error)
    {
        const int Size = 256;
        var buffer = stackalloc char[Size];
        var length = Kernel32Ui.FormatMessage(
            Kernel32Ui.FORMAT_MESSAGE_FROM_SYSTEM | Kernel32Ui.FORMAT_MESSAGE_IGNORE_INSERTS, 0, (uint)win32Error, 0, buffer, Size, 0);
        return length > 0 ? new string(buffer, 0, length) : null;
    }
}
