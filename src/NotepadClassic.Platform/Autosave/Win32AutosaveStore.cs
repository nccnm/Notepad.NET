using System.Runtime.InteropServices;
using System.Text;
using NotepadClassic.Core.Autosave;
using NotepadClassic.Platform.Interop;

namespace NotepadClassic.Platform.Autosave;

/// <summary>
/// The autosave folder on disk. Each file is written to a <c>.tmp</c> sibling, flushed to the disk
/// and then moved over the old one, so a session cut off mid-save leaves either the old file or
/// the new one, never half of it. Every failure surfaces as <see cref="IOException"/>.
/// </summary>
public sealed class Win32AutosaveStore : IAutosaveStore
{
    /// <summary>Names another folder (tests, E2E runs) instead of <see cref="DefaultFolder"/>.</summary>
    public const string FolderVariable = "NOTEPADCLASSIC_AUTOSAVE_DIR";

    private const string ManifestExtension = ".json";
    private const string TextExtension = ".txt";
    private const string TempSuffix = ".tmp";
    private const string LockExtension = ".lock";
    private const string ProbeName = "probe.tmp";
    private const int SharingViolation = unchecked((int)0x80070020);

    public Win32AutosaveStore(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary><c>%LOCALAPPDATA%\NotepadClassic\Autosave</c>.</summary>
    public static string DefaultFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "NotepadClassic", "Autosave");

    /// <summary>The folder named by <see cref="FolderVariable"/>, otherwise <see cref="DefaultFolder"/>.</summary>
    public static Win32AutosaveStore FromEnvironment()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        return new(string.IsNullOrEmpty(folder) ? DefaultFolder : folder);
    }

    public bool CanSave(long requiredBytes) => Guard(() =>
    {
        Directory.CreateDirectory(Folder);
        using (new FileStream(Path.Combine(Folder, ProbeName), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }

        return Kernel32Restart.GetDiskFreeSpaceEx(Folder, out var free, out _, out _) && free >= (ulong)Math.Max(requiredBytes, 0);
    });

    public void WriteText(string id, ReadOnlySpan<char> text) => Replace(PathOf(id, TextExtension), MemoryMarshal.AsBytes(text));

    public void WriteManifest(string id, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Replace(PathOf(id, ManifestExtension), Encoding.UTF8.GetBytes(json));
    }

    private void Replace(string path, ReadOnlySpan<byte> bytes)
    {
        var temp = path + TempSuffix;
        try
        {
            Directory.CreateDirectory(Folder);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e);
        }
    }

    public string? ReadManifest(string id)
    {
        var path = PathOf(id, ManifestExtension);
        return Guard(() =>
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        });
    }

    public string TextPath(string id) => PathOf(id, TextExtension);

    /// <summary>The manifest goes first, so a delete cut short leaves an entry without one, which
    /// <see cref="AutosaveService.CleanOrphans"/> removes.</summary>
    public void Delete(string id)
    {
        string[] paths =
        [
            PathOf(id, ManifestExtension), PathOf(id, ManifestExtension) + TempSuffix,
            PathOf(id, TextExtension), PathOf(id, TextExtension) + TempSuffix,
        ];
        Guard(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (DirectoryNotFoundException)
                {
                }
            }

            return true;
        });
    }

    /// <summary>
    /// An empty <c>&lt;id&gt;.lock</c> opened without sharing and deleted when closed. The file is only
    /// a name to hold: a process that ends in any way loses its handle, so a crash cannot leave an
    /// entry locked for good.
    /// </summary>
    public IDisposable? TryLock(string id)
    {
        var path = PathOf(id, LockExtension);
        return Guard<IDisposable?>(() =>
        {
            try
            {
                Directory.CreateDirectory(Folder);
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException e) when (e.HResult == SharingViolation)
            {
                return null;
            }
        });
    }

    public IReadOnlyList<AutosaveEntry> List() => Guard<IReadOnlyList<AutosaveEntry>>(() =>
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var entries = new Dictionary<string, (bool HasManifest, bool HasText, DateTime Newest)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(Folder).EnumerateFiles())
        {
            var name = file.Name;
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            var id = dot < 0 ? name : name[..dot];
            if (!AutosaveSession.IsValidId(id) || name.EndsWith(LockExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasManifest = name.Length == id.Length + ManifestExtension.Length && name.EndsWith(ManifestExtension, StringComparison.OrdinalIgnoreCase);
            var hasText = name.Length == id.Length + TextExtension.Length && name.EndsWith(TextExtension, StringComparison.OrdinalIgnoreCase);
            var written = file.LastWriteTimeUtc;
            entries[id] = entries.TryGetValue(id, out var known)
                ? (known.HasManifest || hasManifest, known.HasText || hasText, written > known.Newest ? written : known.Newest)
                : (hasManifest, hasText, written);
        }

        return entries
            .Select(e => new AutosaveEntry(e.Key, e.Value.HasManifest, now > e.Value.Newest ? now - e.Value.Newest : TimeSpan.Zero, e.Value.HasText))
            .ToList();
    });

    private string PathOf(string id, string extension)
    {
        if (!AutosaveSession.IsValidId(id))
        {
            throw new ArgumentException("Not a session id.", nameof(id));
        }

        return Path.Combine(Folder, id + extension);
    }

    /// <summary>Access errors arrive as <see cref="UnauthorizedAccessException"/>; Core expects
    /// <see cref="IOException"/> for every file failure.</summary>
    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e);
        }
    }
}

/// <summary><c>RegisterApplicationRestart(commandLine, 0)</c>: restart after an update, a reboot,
/// a crash or a hang, as the original registers it.</summary>
public sealed class Win32RestartRegistration : IRestartRegistration
{
    public bool Register(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        return Kernel32Restart.RegisterApplicationRestart(commandLine, 0) == 0;
    }

    /// <summary>The command line registered for <paramref name="process"/>
    /// (<c>GetApplicationRestartSettings</c>), or null when there is none.</summary>
    public static unsafe string? RegisteredCommandLine(nint process)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        uint flags;
        return Kernel32RestartQuery.GetApplicationRestartSettings(process, buffer, &size, &flags) == 0 ? new string(buffer) : null;
    }

    /// <summary>The command line registered for this process.</summary>
    public static string? RegisteredCommandLine() => RegisteredCommandLine(Kernel32RestartQuery.GetCurrentProcess());

    /// <summary><c>UnregisterApplicationRestart</c> (tests undo their registration).</summary>
    public static void Unregister() => _ = Kernel32RestartQuery.UnregisterApplicationRestart();
}

/// <summary>The process current directory.</summary>
public sealed class Win32ProcessDirectory : IProcessDirectory
{
    public string? Current
    {
        get
        {
            try
            {
                return Environment.CurrentDirectory;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public bool TrySetCurrent(string path)
    {
        try
        {
            Environment.CurrentDirectory = path;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
