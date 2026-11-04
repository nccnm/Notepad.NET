using NotepadNet.Core.CommandLine;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Services;

/// <summary><c>FindFirstFileW</c> and <c>GetFullPathNameW</c> for the command-line file name.</summary>
public sealed unsafe class Win32PathService : IPathService
{
    public bool FindFirstFileSucceeds(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Kernel32Ui.WIN32_FIND_DATAW data;
        var find = Kernel32Ui.FindFirstFile(path, &data);
        if (find == -1)
        {
            return false;
        }

        Kernel32Ui.FindClose(find);
        return true;
    }

    public string GetFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var length = Kernel32Ui.GetFullPathName(path, 0, null, 0);
        if (length <= 0)
        {
            return path;
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            var written = Kernel32Ui.GetFullPathName(path, length, p, 0);
            return written > 0 && written < length ? new string(p, 0, written) : path;
        }
    }

    /// <summary>The real <c>PathFindExtensionW</c>, for parity tests of <see cref="PathRules.FindExtension"/>.</summary>
    public static string FindExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        fixed (char* p = path)
        {
            var extension = ShlwApi.PathFindExtension(p);
            return extension is null ? "" : path[(int)(extension - p)..];
        }
    }
}

/// <summary>Process start information the original reads.</summary>
public static unsafe class Win32StartupInfo
{
    /// <summary>
    /// <c>GetCommandLineW</c>: the command line exactly as the process got it. .NET rebuilds
    /// <see cref="Environment.CommandLine"/> from the parsed arguments and drops quotes that the
    /// original's parser depends on (<c>/PT "file" "printer"</c> needs a quoted file name).
    /// </summary>
    public static string CommandLine() => new(Kernel32Ui.GetCommandLine());

    /// <summary><c>GetStartupInfoW().dwFlags &amp; STARTF_UNTRUSTEDSOURCE</c>.</summary>
    public static bool IsUntrustedSource()
    {
        Kernel32Ui.STARTUPINFOW info = default;
        info.cb = sizeof(Kernel32Ui.STARTUPINFOW);
        Kernel32Ui.GetStartupInfo(&info);
        return (info.dwFlags & Kernel32Ui.STARTF_UNTRUSTEDSOURCE) != 0;
    }

    /// <summary><c>GetLocaleInfoW(LOCALE_USER_DEFAULT, LOCALE_IMEASURE)</c> is not "1" (US).</summary>
    public static bool UsesMetricMeasure()
    {
        var buffer = stackalloc char[4];
        var length = Kernel32Ui.GetLocaleInfo(Kernel32Ui.LOCALE_USER_DEFAULT, Kernel32Ui.LOCALE_IMEASURE, buffer, 4);
        return !(length >= 2 && buffer[0] == '1');
    }
}
