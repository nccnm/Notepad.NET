using System.Runtime.InteropServices;
using NotepadClassic.Core.Autosave;

namespace NotepadClassic.Core.Tests.Fakes;

/// <summary>Autosave folder in memory. Text files go into <see cref="MemoryFileSystem"/> so the
/// document service loads them like any file.</summary>
internal sealed class FakeAutosaveStore(MemoryFileSystem files) : IAutosaveStore
{
    public const string Folder = @"C:\Autosave";

    public Dictionary<string, string> Manifests { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];

    public List<long> CanSaveRequests { get; } = [];

    public bool CanSaveResult { get; set; } = true;

    public bool CanSaveThrows { get; set; }

    public bool FailWriteText { get; set; }

    public bool FailWriteManifest { get; set; }

    public bool FailReadManifest { get; set; }

    public bool FailDelete { get; set; }

    public bool FailList { get; set; }

    public List<AutosaveEntry> Entries { get; } = [];

    public bool CanSave(long requiredBytes)
    {
        CanSaveRequests.Add(requiredBytes);
        return CanSaveThrows ? throw new IOException("probe") : CanSaveResult;
    }

    public void WriteText(string id, ReadOnlySpan<char> text)
    {
        if (FailWriteText)
        {
            throw new IOException("disk full", unchecked((int)0x80070070));
        }

        files.Files[TextPath(id)] = MemoryMarshal.AsBytes(text).ToArray();
    }

    public void WriteManifest(string id, string json)
    {
        if (FailWriteManifest)
        {
            throw new IOException("disk full", unchecked((int)0x80070070));
        }

        Manifests[id] = json;
    }

    public string? ReadManifest(string id) =>
        FailReadManifest ? throw new IOException("locked") : Manifests.GetValueOrDefault(id);

    public string TextPath(string id) => $@"{Folder}\{id}.txt";

    public string? TextOf(string id) =>
        files.Files.TryGetValue(TextPath(id), out var bytes) ? System.Text.Encoding.Unicode.GetString(bytes) : null;

    public AutosaveManifest? ManifestOf(string id) => Manifests.TryGetValue(id, out var json) ? AutosaveManifest.Parse(json) : null;

    public void Delete(string id)
    {
        if (FailDelete)
        {
            throw new IOException("locked");
        }

        Deleted.Add(id);
        Manifests.Remove(id);
        files.Files.Remove(TextPath(id));
    }

    public IReadOnlyList<AutosaveEntry> List() => FailList ? throw new IOException("no folder") : Entries;

    /// <summary>Ids locked right now, by this run or another.</summary>
    public HashSet<string> Locks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailLock { get; set; }

    public IDisposable? TryLock(string id)
    {
        if (FailLock)
        {
            throw new IOException("no folder");
        }

        return Locks.Add(id) ? new Held(() => Locks.Remove(id)) : null;
    }

    private sealed class Held(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

internal sealed class FakeProcessDirectory : IProcessDirectory
{
    public string? Current { get; set; } = @"C:\Work";

    public bool Refuse { get; set; }

    public List<string> Changes { get; } = [];

    public bool TrySetCurrent(string path)
    {
        Changes.Add(path);
        if (Refuse)
        {
            return false;
        }

        Current = path;
        return true;
    }
}

internal sealed class FakeRestartRegistration : IRestartRegistration
{
    public List<string> CommandLines { get; } = [];

    public bool Result { get; set; } = true;

    public bool Register(string commandLine)
    {
        CommandLines.Add(commandLine);
        return Result;
    }
}
