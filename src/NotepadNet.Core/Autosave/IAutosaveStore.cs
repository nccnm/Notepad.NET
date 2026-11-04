namespace NotepadNet.Core.Autosave;

/// <summary>A session found in the autosave folder.</summary>
/// <param name="HasManifest">The manifest file exists; without it the entry can never be restored.</param>
/// <param name="Age">Time since the newest of its files was written.</param>
/// <param name="HasText">The text file exists.</param>
public readonly record struct AutosaveEntry(string Id, bool HasManifest, TimeSpan Age, bool HasText = false);

/// <summary>
/// The autosave folder (<c>%LOCALAPPDATA%\Notepad.NET\Autosave</c>): per session id a
/// manifest <c>&lt;id&gt;.json</c> and, when the document was dirty, its text <c>&lt;id&gt;.txt</c>
/// as raw UTF-16 LE. Failures surface as <see cref="IOException"/>.
/// </summary>
public interface IAutosaveStore
{
    /// <summary>
    /// <c>CanAutosave</c>: the folder can be created, a file can be created in it, and the volume
    /// has at least <paramref name="requiredBytes"/> free. Deliberate difference (parity.md): the
    /// original's <c>probe.autosave</c> stays behind as a hidden file; the port's probe is deleted
    /// when it closes.
    /// </summary>
    bool CanSave(long requiredBytes);

    /// <summary>Writes the text file of <paramref name="id"/>, replacing any earlier one whole.</summary>
    void WriteText(string id, ReadOnlySpan<char> text);

    /// <summary>Writes the manifest of <paramref name="id"/>, replacing any earlier one whole.</summary>
    void WriteManifest(string id, string json);

    /// <summary>The manifest of <paramref name="id"/>, or null when there is none.</summary>
    string? ReadManifest(string id);

    /// <summary>Full path of the text file of <paramref name="id"/> (it is loaded like any file).</summary>
    string TextPath(string id);

    /// <summary>Deletes every file of <paramref name="id"/>, except its lock; missing files are not an error.</summary>
    void Delete(string id);

    /// <summary>
    /// Takes the lock of <paramref name="id"/> until the result is disposed, or returns null when
    /// another holder has it. A running window holds the lock of its own entry, and a window restoring
    /// an entry holds that entry's: an entry whose lock is free belongs to no window. The operating
    /// system releases the lock when a process ends in any way.
    /// </summary>
    IDisposable? TryLock(string id);

    /// <summary>Sessions whose files are in the folder; files not named after a session id are left out.</summary>
    IReadOnlyList<AutosaveEntry> List();
}

/// <summary><c>RegisterApplicationRestart</c>.</summary>
public interface IRestartRegistration
{
    /// <summary>Registers <paramref name="commandLine"/> for Windows to start this program with
    /// after an update or a restart. False when Windows refuses it.</summary>
    bool Register(string commandLine);
}

/// <summary>The process current directory (<c>GetCurrentDirectoryW</c> / <c>SetCurrentDirectoryW</c>).</summary>
public interface IProcessDirectory
{
    string? Current { get; }

    /// <summary>False when the directory cannot be made current (it no longer exists, for example).</summary>
    bool TrySetCurrent(string path);
}
