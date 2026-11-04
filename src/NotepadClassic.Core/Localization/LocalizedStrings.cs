using System.Globalization;
using System.Resources;
using System.Text;

namespace NotepadClassic.Core.Localization;

public interface ILocalizedStrings
{
    /// <exception cref="KeyNotFoundException">No string has this key.</exception>
    string GetString(string key);
}

/// <summary>Strings from <c>Resources/Strings.resx</c> for the current UI culture.</summary>
public sealed class ResxStrings : ILocalizedStrings
{
    private static readonly ResourceManager Manager = new("NotepadClassic.Core.Resources.Strings", typeof(ResxStrings).Assembly);

    public static ResxStrings Instance { get; } = new();

    public string GetString(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? throw new KeyNotFoundException($"Missing UI string '{key}'.");
}

/// <summary>
/// Keys of <c>Strings.resx</c>. Strings taken from the original's MUI keep its ID in the
/// resx comment.
/// </summary>
public static class StringKeys
{
    public const string CannotOpenFile = nameof(CannotOpenFile);                 // MUI 1
    public const string CannotFindCreate = nameof(CannotFindCreate);             // MUI 2
    public const string InvalidFileName = nameof(InvalidFileName);               // MUI 13
    public const string DefaultHeader = nameof(DefaultHeader);                   // MUI 18
    public const string DefaultTrailer = nameof(DefaultTrailer);                 // MUI 19
    public const string Untitled = nameof(Untitled);                             // MUI 4
    public const string TitleFormat = nameof(TitleFormat);                       // MUI 5
    public const string NotEnoughMemory = nameof(NotEnoughMemory);               // MUI 7
    public const string FileTooLarge = nameof(FileTooLarge);                     // MUI 8
    public const string AppName = nameof(AppName);                               // MUI 9, 3000
    public const string FileDialogsFailed = nameof(FileDialogsFailed);           // MUI 10
    public const string WordWrapTooMuchText = nameof(WordWrapTooMuchText);       // MUI 15
    public const string TextDocumentsFilter = nameof(TextDocumentsFilter);       // MUI 20
    public const string AllFilesFilter = nameof(AllFilesFilter);                 // MUI 21
    public const string OpenTitle = nameof(OpenTitle);                           // MUI 22
    public const string SaveAsTitle = nameof(SaveAsTitle);                       // MUI 23
    public const string AccessDenied = nameof(AccessDenied);                     // MUI 26
    public const string UnicodeLossWarning = nameof(UnicodeLossWarning);         // MUI 27
    public const string EncodingAnsi = nameof(EncodingAnsi);                     // MUI 32
    public const string EncodingUtf16LE = nameof(EncodingUtf16LE);               // MUI 33
    public const string EncodingUtf16BE = nameof(EncodingUtf16BE);               // MUI 34
    public const string EncodingUtf8 = nameof(EncodingUtf8);                     // MUI 35
    public const string LineColumn = nameof(LineColumn);                         // MUI 37
    public const string EncodingLabel = nameof(EncodingLabel);                   // MUI 46
    public const string EditorAccessibleName = nameof(EditorAccessibleName);     // MUI 50
    public const string EolWindows = nameof(EolWindows);                         // MUI 51
    public const string EolUnix = nameof(EolUnix);                               // MUI 52
    public const string EolMacintosh = nameof(EolMacintosh);                     // MUI 53
    public const string ZoomPercent = nameof(ZoomPercent);                       // MUI 56
    public const string EncodingAutoDetect = nameof(EncodingAutoDetect);         // MUI 57
    public const string EncodingUtf8Bom = nameof(EncodingUtf8Bom);               // MUI 58
    public const string StatusBarAccessibleName = nameof(StatusBarAccessibleName); // MUI 59
    public const string EncodingPane = nameof(EncodingPane);                     // MUI 60
    public const string SaveChangesQuestion = nameof(SaveChangesQuestion);       // MUI 3001
    public const string SaveButton = nameof(SaveButton);                         // MUI 3002
    public const string DontSaveButton = nameof(DontSaveButton);                 // MUI 3003
    public const string FileChangedQuestion = nameof(FileChangedQuestion);       // port addition
    public const string OverwriteButton = nameof(OverwriteButton);               // port addition
    public const string SaveAsButton = nameof(SaveAsButton);                     // port addition
    public const string FileChangedReload = nameof(FileChangedReload);           // port addition
    public const string FileChangedReloadLosing = nameof(FileChangedReloadLosing); // port addition
    public const string FileGoneNotice = nameof(FileGoneNotice);                 // port addition
    public const string ReloadButton = nameof(ReloadButton);                     // port addition
    public const string KeepButton = nameof(KeepButton);                         // port addition
    public const string ClipboardBusy = nameof(ClipboardBusy);                   // port addition
    public const string FolderCannotOpen = nameof(FolderCannotOpen);             // port addition
    public const string CannotFind = nameof(CannotFind);                         // MUI 6
    public const string GoToCaption = nameof(GoToCaption);                       // MUI 30
    public const string GoToBeyondEnd = nameof(GoToBeyondEnd);                   // MUI 31
    public const string FoundFromBottom = nameof(FoundFromBottom);               // MUI 54
    public const string FoundFromTop = nameof(FoundFromTop);                     // MUI 55
    public const string GoToTitle = nameof(GoToTitle);                           // dialog 14 caption
    public const string GoToLineLabel = nameof(GoToLineLabel);                   // dialog 14 static 1200
    public const string GoToButton = nameof(GoToButton);                         // dialog 14 IDOK
    public const string CancelButton = nameof(CancelButton);                     // dialog 14 IDCANCEL
    public const string PrintDialogsFailed = nameof(PrintDialogsFailed);         // MUI 11
    public const string CannotPrint = nameof(CannotPrint);                       // MUI 12
    public const string CannotAccessPrinter = nameof(CannotAccessPrinter);       // MUI 25
    public const string CannotShutDown = nameof(CannotShutDown);                 // MUI 24
    public const string PageTooSmall = nameof(PageTooSmall);                     // MUI 28
    public const string CommonDialogError = nameof(CommonDialogError);           // MUI 29
    public const string PageNumber = nameof(PageNumber);                         // MUI 36
    public const string HeaderFooterCodes = nameof(HeaderFooterCodes);           // MUI 45
    public const string NowPrinting = nameof(NowPrinting);                       // dialog 11 static
    public const string PageSetupTitle = nameof(PageSetupTitle);                 // dialog 12 caption
    public const string PageSetupPaper = nameof(PageSetupPaper);                 // dialog 12 0x431
    public const string PageSetupSize = nameof(PageSetupSize);                   // dialog 12 0x441
    public const string PageSetupSource = nameof(PageSetupSource);               // dialog 12 0x442
    public const string PageSetupOrientation = nameof(PageSetupOrientation);     // dialog 12 0x430
    public const string PageSetupPortrait = nameof(PageSetupPortrait);           // dialog 12 0x420
    public const string PageSetupLandscape = nameof(PageSetupLandscape);         // dialog 12 0x421
    public const string PageSetupMargins = nameof(PageSetupMargins);             // dialog 12 0x433
    public const string PageSetupLeft = nameof(PageSetupLeft);                   // dialog 12 0x44E
    public const string PageSetupRight = nameof(PageSetupRight);                 // dialog 12 0x44F
    public const string PageSetupTop = nameof(PageSetupTop);                     // dialog 12 0x450
    public const string PageSetupBottom = nameof(PageSetupBottom);               // dialog 12 0x451
    public const string PageSetupHeader = nameof(PageSetupHeader);               // dialog 12 0x20
    public const string PageSetupFooter = nameof(PageSetupFooter);               // dialog 12 0x21
    public const string PageSetupHelpLink = nameof(PageSetupHelpLink);           // dialog 12 0x22
    public const string OkButton = nameof(OkButton);                             // dialog 12 IDOK
    public const string PageSetupPrinter = nameof(PageSetupPrinter);             // dialog 12 0x402
    public const string PageSetupPreview = nameof(PageSetupPreview);             // dialog 12 preview group
}

/// <summary>The two ways the original fills a string with arguments.</summary>
public static class MessageText
{
    /// <summary>
    /// <c>NpMessageBox</c>: the first <c>%</c> starts a two-character
    /// placeholder (<c>%%</c>) replaced by <paramref name="argument"/>; only the first one is
    /// replaced, and a string without <c>%</c> is returned unchanged. Both inputs are cut to
    /// 512 characters first.
    /// </summary>
    public static string InsertArgument(string format, string argument)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(argument);
        const int MaxLength = 512;
        var fmt = format.AsSpan(0, Math.Min(format.Length, MaxLength));
        var arg = argument.AsSpan(0, Math.Min(argument.Length, MaxLength));
        var marker = fmt.IndexOf('%');
        if (marker < 0)
        {
            return fmt.ToString();
        }

        var rest = fmt[Math.Min(marker + 2, fmt.Length)..];
        return string.Concat(fmt[..marker], arg, rest);
    }

    /// <summary>
    /// <c>FormatMessageW(FORMAT_MESSAGE_FROM_STRING | FORMAT_MESSAGE_ARGUMENT_ARRAY)</c> for the
    /// inserts the UI strings use: <c>%1</c>-<c>%9</c> take an argument, <c>%%</c> is a percent sign.
    /// </summary>
    public static string FormatInserts(string format, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new StringBuilder(format.Length + 32);
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '%' && i + 1 < format.Length)
            {
                var next = format[i + 1];
                if (next is >= '1' and <= '9' && next - '1' < arguments.Length)
                {
                    result.Append(arguments[next - '1']);
                    i++;
                    continue;
                }

                if (next == '%')
                {
                    result.Append('%');
                    i++;
                    continue;
                }
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// The <c>printf</c> subset of the UI strings: <c>%d</c>, <c>%s</c>, <c>%x</c> with an optional
    /// zero flag and width (string 29 "Common Dialog error (0x%04x)"), and <c>%%</c>.
    /// </summary>
    public static string Printf(string format, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new StringBuilder(format.Length + 16);
        var next = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c != '%' || i + 1 >= format.Length)
            {
                result.Append(c);
                continue;
            }

            var start = i;
            var zero = format[i + 1] == '0';
            var width = 0;
            var j = zero ? i + 2 : i + 1;
            while (j < format.Length && char.IsAsciiDigit(format[j]))
            {
                width = (width * 10) + (format[j++] - '0');
            }

            var conversion = j < format.Length ? format[j] : '\0';
            string? text = conversion switch
            {
                '%' when j == start + 1 => "%",
                'd' or 's' => Convert.ToString(arguments[next++], CultureInfo.InvariantCulture),
                'x' => Convert.ToInt32(arguments[next++], CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture),
                _ => null,
            };
            if (text is null)
            {
                result.Append('%');
                continue;
            }

            result.Append(zero ? '0' : ' ', Math.Max(0, width - text.Length)).Append(text);
            i = j;
        }

        return result.ToString();
    }
}
