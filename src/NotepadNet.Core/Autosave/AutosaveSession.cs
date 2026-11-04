using NotepadNet.Core.CommandLine;

namespace NotepadNet.Core.Autosave;

/// <summary>
/// The autosave session id: one per run, sent to
/// <c>RegisterApplicationRestart</c> as <c>RestartByRestartManager:&lt;id&gt;</c>.
/// </summary>
public static class AutosaveSession
{
    /// <summary>
    /// <c>CreateSessionGuidString</c>: <c>"%08lX-%04X-%04x-%02X%02X-%02X%02X%02X%02X%02X%02X"</c>,
    /// 36 characters without braces, the third group (<c>Data3</c>) in lower case and the others
    /// in upper case.
    /// </summary>
    public static string FormatId(Guid value)
    {
        var chars = value.ToString("D").ToUpperInvariant().ToCharArray();
        for (var i = 14; i < 18; i++)
        {
            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    /// <summary><c>BuildRestartCommandLine</c>: the prefix followed by the id.</summary>
    public static string RestartCommandLine(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return CommandLineParser.RestartPrefix + id;
    }

    /// <summary>
    /// Whether an id from the command line may name autosave files: 36 characters in the 8-4-4-4-12
    /// layout of hex digits and dashes. Deliberate difference (parity.md): the original accepts
    /// any 36 characters and builds a path from them; the port refuses anything else, so a crafted
    /// command line cannot reach outside the autosave folder.
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (id is null || id.Length != CommandLineParser.RestartIdLength)
        {
            return false;
        }

        for (var i = 0; i < id.Length; i++)
        {
            var dash = i is 8 or 13 or 18 or 23;
            if (dash ? id[i] != '-' : !char.IsAsciiHexDigit(id[i]))
            {
                return false;
            }
        }

        return true;
    }
}
