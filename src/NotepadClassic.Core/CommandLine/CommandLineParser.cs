using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.CommandLine;

/// <summary>What the command line asks for.</summary>
public enum StartupAction
{
    /// <summary>Nothing after the options: the next closed note, otherwise an empty untitled document.</summary>
    None,

    /// <summary><c>/.NEWWINDOW</c> (port addition, File > New Window): an empty untitled document,
    /// even when closed notes are waiting.</summary>
    NewWindow,

    /// <summary>Open <see cref="ParsedCommandLine.FileArgument"/> (create it after asking when missing).</summary>
    Open,

    /// <summary><c>/.SETUP [file]</c>: <c>SlipUpAcc</c>, the restricted window style, then the file.</summary>
    Setup,

    /// <summary><c>/P file</c>: print to the default printer, then close.</summary>
    Print,

    /// <summary><c>/PT "file" "printer"</c>: print to the named printer, then close.</summary>
    PrintTo,

    /// <summary><c>/P</c> or <c>/PT</c> whose arguments are incomplete (no file, or no quoted printer).</summary>
    InvalidPrint,

    /// <summary><c>RestartByRestartManager:&lt;id&gt;</c>: restore the autosaved document.</summary>
    Restart,
}

/// <param name="ForcedEncoding"><c>/A</c> (ANSI) or <c>/W</c> (UTF-16 LE), otherwise <see cref="TextEncodingKind.Detect"/>.</param>
/// <param name="FileArgument">Raw text of the file name argument (quotes included), or "".</param>
/// <param name="PrinterName"><c>/PT</c> printer, at most 255 characters.</param>
/// <param name="RestartId">The 36 characters after <c>RestartByRestartManager:</c>, or null when their count differs.</param>
public sealed record ParsedCommandLine(
    TextEncodingKind ForcedEncoding, StartupAction Action, string FileArgument, string? PrinterName = null, string? RestartId = null);

/// <summary>
/// The original's command-line rules (<c>InitApplication</c>, <c>CmdLineHasPrefix</c>,
/// <c>HandleSetupArg</c>, <c>HandlePrintArgs</c>, <c>HandleRestartManagerArg</c>):
/// <list type="bullet">
/// <item>options are case-insensitive prefixes, so <c>/aXYZ.txt</c> is <c>/A</c> plus <c>XYZ.txt</c>;</item>
/// <item><c>/A</c> and <c>/W</c> come first and still apply to an untrusted source;</item>
/// <item>for a source started with <c>STARTF_UNTRUSTEDSOURCE</c> everything left is a file name;</item>
/// <item>otherwise <c>/.SETUP</c>, then <c>/PT</c> before <c>/P</c>, then <c>RestartByRestartManager:</c>.</item>
/// </list>
/// </summary>
public static class CommandLineParser
{
    public const string SetupOption = "/.SETUP";
    public const string NewWindowOption = "/.NEWWINDOW";
    public const string PrintToOption = "/PT";
    public const string PrintOption = "/P";
    public const string RestartPrefix = "RestartByRestartManager:";
    public const int RestartIdLength = 36;
    public const int MaxPrinterNameLength = 255;

    /// <summary>
    /// What <c>NotepadWinMain</c> passes on: the full command line without the program name, i.e.
    /// everything after the first space or tab outside double quotes, with following spaces and tabs
    /// skipped.
    /// </summary>
    public static string StripProgramName(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var quoted = false;
        var i = 0;
        for (; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is ' ' or '\t')
            {
                break;
            }
        }

        return SkipBlanks(commandLine, i);
    }

    public static ParsedCommandLine Parse(string arguments, bool untrustedSource)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var rest = SkipBlanks(arguments, 0);
        var forced = TextEncodingKind.Detect;
        if (HasPrefix(rest, "/A"))
        {
            forced = TextEncodingKind.Ansi;
        }
        else if (HasPrefix(rest, "/W"))
        {
            forced = TextEncodingKind.Utf16LE;
        }

        if (forced != TextEncodingKind.Detect)
        {
            rest = SkipBlanks(rest, 2);
        }

        if (!untrustedSource)
        {
            if (HasPrefix(rest, SetupOption))
            {
                return new(forced, StartupAction.Setup, SkipBlanks(rest, SetupOption.Length));
            }

            if (HasPrefix(rest, NewWindowOption))
            {
                return new(forced, StartupAction.NewWindow, "");
            }

            if (HasPrefix(rest, PrintToOption))
            {
                return ParsePrint(forced, SkipBlanks(rest, PrintToOption.Length), printTo: true);
            }

            if (HasPrefix(rest, PrintOption))
            {
                return ParsePrint(forced, SkipBlanks(rest, PrintOption.Length), printTo: false);
            }

            if (HasPrefix(rest, RestartPrefix))
            {
                var id = rest[RestartPrefix.Length..];
                return new(forced, StartupAction.Restart, "", RestartId: id.Length == RestartIdLength ? id : null);
            }
        }

        return new(forced, rest.Length == 0 ? StartupAction.None : StartupAction.Open, rest);
    }

    /// <summary>
    /// <c>/P file</c> or <c>/PT "file" "printer"</c>. The printer name must follow a quoted file
    /// name; an unquoted name takes the rest of the line, so nothing is left for the printer.
    /// Deliberate difference (parity.md): the original then retries the whole line as a file name
    /// (and, for an unquoted name, looks for the printer from the name's second character).
    /// </summary>
    private static ParsedCommandLine ParsePrint(TextEncodingKind forced, string rest, bool printTo)
    {
        if (rest.Length == 0)
        {
            return new(forced, StartupAction.InvalidPrint, "");
        }

        if (!printTo)
        {
            return new(forced, StartupAction.Print, rest);
        }

        var (_, consumed) = FileArgument.Split(rest);
        var after = SkipBlanks(rest, consumed);
        if (after.Length == 0 || after[0] != '"' || consumed == rest.Length)
        {
            return new(forced, StartupAction.InvalidPrint, rest);
        }

        var end = after.IndexOf('"', 1);
        var name = end < 0 ? after[1..] : after[1..end];
        if (name.Length > MaxPrinterNameLength)
        {
            name = name[..MaxPrinterNameLength];
        }

        return new(forced, StartupAction.PrintTo, rest[..consumed], name);
    }

    /// <summary><c>CmdLineHasPrefix</c>: compares each character upper-cased (<c>CharUpperW</c>).</summary>
    public static bool HasPrefix(string text, string prefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(prefix);
        if (text.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (UpperCase(text[i]) != UpperCase(prefix[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>CharUpperW</c> for one character. It agrees with <see cref="char.ToUpperInvariant"/> on
    /// every character that can upper-case to a letter of the options, except LATIN SMALL LETTER
    /// LONG S, which .NET maps to <c>S</c> and USER32 leaves alone (checked over the whole BMP in
    /// the Platform tests).
    /// </summary>
    private static char UpperCase(char c) => c == 'ſ' ? c : char.ToUpperInvariant(c);

    private static string SkipBlanks(string text, int start)
    {
        var i = Math.Min(start, text.Length);
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        return text[i..];
    }
}

/// <summary>File system queries the file name rules need.</summary>
public interface IPathService
{
    /// <summary><c>FindFirstFileW</c> finds something (wildcards allowed).</summary>
    bool FindFirstFileSucceeds(string path);

    /// <summary><c>GetFullPathNameW</c>; the input when it fails.</summary>
    string GetFullPath(string path);
}

/// <summary>
/// <c>ParseFileNameArg</c>: a name starting with
/// <c>"</c> or <c>'</c> ends at the matching quote, otherwise it is the whole rest of the line;
/// a name without extension that <c>FindFirstFileW</c> does not find gets <c>.txt</c>.
/// </summary>
public static class FileArgument
{
    /// <summary>The name and how many characters of <paramref name="text"/> it used (closing quote included).</summary>
    public static (string Name, int Consumed) Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] is '"' or '\'')
        {
            var close = text.IndexOf(text[0], 1);
            return close < 0 ? (text[1..], text.Length) : (text[1..close], close + 1);
        }

        return (text, text.Length);
    }

    /// <summary>
    /// The path to open. Deliberate difference (parity.md): the name is always made a full path;
    /// the original keeps a bare file name (and, by testing the wrong pointer, any quoted name)
    /// relative, which only changes the path shown in the save-changes question.
    /// </summary>
    public static string Resolve(string argument, IPathService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var (name, _) = Split(argument);
        var path = paths.GetFullPath(name);
        if (!paths.FindFirstFileSucceeds(path) && PathRules.FindExtension(path).Length == 0)
        {
            path += ".txt";
        }

        return path;
    }
}

/// <summary>Managed versions of the shlwapi path rules the original uses.</summary>
public static class PathRules
{
    /// <summary>
    /// <c>PathFindExtensionW</c>: from the last <c>.</c> that comes after the last <c>\</c> and has
    /// no space after it, or "" when there is none.
    /// </summary>
    public static string FindExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var dot = -1;
        for (var i = 0; i < path.Length; i++)
        {
            switch (path[i])
            {
                case '.':
                    dot = i;
                    break;
                case '\\':
                case ' ':
                    dot = -1;
                    break;
            }
        }

        return dot < 0 ? "" : path[dot..];
    }
}
