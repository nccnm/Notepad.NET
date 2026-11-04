namespace NotepadClassic.Core.Services;

/// <summary>Current local time (<c>GetLocalTime</c> in the original).</summary>
public interface IClock
{
    DateTime LocalNow { get; }
}

/// <summary>
/// The user's short time and short date formats, as <c>GetTimeFormatEx(TIME_NOSECONDS)</c> and
/// <c>GetDateFormatEx(DATE_SHORTDATE | reading-order flag)</c> produce them.
/// </summary>
public interface IDateTimeFormatter
{
    string FormatShortTime(DateTime localTime);

    string FormatShortDate(DateTime localTime);

    /// <summary><c>GetTimeFormatW(LOCALE_USER_DEFAULT, 0)</c>: the print job's <c>&amp;t</c>.</summary>
    string FormatTime(DateTime localTime);

    /// <summary><c>GetDateFormatW(LOCALE_USER_DEFAULT, DATE_LONGDATE)</c>: the print job's <c>&amp;d</c>.</summary>
    string FormatLongDate(DateTime localTime);
}

public interface IClipboard
{
    /// <summary>Whether Paste is enabled: the clipboard opens and offers <c>CF_TEXT</c>
    /// (<c>UpdateMenuState</c>; Windows synthesizes it from <c>CF_UNICODETEXT</c>).</summary>
    bool ContainsText();

    /// <summary>Puts <paramref name="text"/> on the clipboard. False when another program holds the
    /// clipboard or memory runs out; what was on it then normally stays.</summary>
    bool SetText(string text);
}

public interface IShell
{
    /// <summary><c>ShellExecuteW(NULL, NULL, uri, ...)</c>; the result is ignored.</summary>
    void OpenUri(string uri);

    /// <summary>File > New Window: starts this executable again for an empty document, with
    /// <see cref="CommandLine.CommandLineParser.NewWindowOption"/> so that it leaves closed notes alone.</summary>
    void StartNewWindow();

    /// <summary>Starts this executable again without arguments: a normal start, which takes the next closed note.</summary>
    void StartNextNoteWindow();

    /// <summary><c>SHAddToRecentDocs(SHARD_PATHW)</c> after a successful load.</summary>
    void AddToRecentDocuments(string path);

    /// <summary>Shows the file selected in its folder, or just the folder when the file is gone.
    /// False when the folder cannot be shown either.</summary>
    bool ShowInFolder(string path);
}

/// <summary>Name shown in the title bar for a saved document.</summary>
public interface IFileTitleProvider
{
    /// <summary>The on-disk spelling of the file name (<c>FindFirstFileW</c>, or the path when the
    /// file is not found) passed through <c>GetFileTitleW</c>.</summary>
    string GetTitle(string path);
}

public interface ISystemErrorMessages
{
    /// <summary><c>FormatMessageW(FROM_SYSTEM | IGNORE_INSERTS)</c> for a Win32 error, or null.</summary>
    string? Find(int win32Error);
}

/// <summary>
/// Extension point where the original evaluates Windows Information Protection before a load
/// (<c>PreLoadCheck</c>) and protects a file after a save. The port installs
/// <see cref="AllowAllFileAccessPolicy"/>; EDP itself is not ported.
/// </summary>
public interface IFileAccessPolicy
{
    /// <summary>False refuses the load; the policy has already told the user why.</summary>
    bool CanOpen(string path);

    void AfterSave(string path);
}

public sealed class AllowAllFileAccessPolicy : IFileAccessPolicy
{
    public bool CanOpen(string path) => true;

    public void AfterSave(string path)
    {
    }
}

/// <summary>Configuration the original hard-codes: Help, Feedback and Page Setup help links.</summary>
/// <param name="HelpUrl">View Help (F1). Null or empty hides the menu item.</param>
/// <param name="FeedbackUrl">Send Feedback. Null or empty hides the menu item.</param>
/// <param name="PageSetupHelpUrl">The "Input Values" link of Page Setup (header and footer codes).
/// Null or empty leaves the link out.</param>
public sealed record AppOptions(string? HelpUrl, string? FeedbackUrl, string? PageSetupHelpUrl = null)
{
    public bool HasHelp => !string.IsNullOrEmpty(HelpUrl);

    public bool HasFeedback => !string.IsNullOrEmpty(FeedbackUrl);
}
