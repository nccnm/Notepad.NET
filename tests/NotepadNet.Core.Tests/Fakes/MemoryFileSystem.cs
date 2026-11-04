using NotepadNet.Core.IO;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>
/// In-memory file system with the in-place save semantics of the platform one: a save writes
/// from offset 0 over the old bytes, and only <see cref="IWritableFile.SetEndOfFile"/> cuts the
/// old tail. Failures are scripted per path.
/// </summary>
internal sealed class MemoryFileSystem : IFileSystem
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Win32 error thrown by <see cref="OpenRead"/> for a path.</summary>
    public Dictionary<string, int> ReadErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Win32 error thrown by <see cref="OpenForSave"/> for a path.</summary>
    public Dictionary<string, int> SaveErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Win32 error thrown by the write with this zero-based index during a save.</summary>
    public (int WriteIndex, int Error)? FailingWrite { get; set; }

    /// <summary>Length reported instead of the real one (to test the 1 GiB limit).</summary>
    public long? ReportedLength { get; set; }

    public List<string> Deleted { get; } = [];

    public List<(string Path, bool ShareWrite)> Saves { get; } = [];

    public IFileContent OpenRead(string path)
    {
        if (ReadErrors.TryGetValue(path, out var error))
        {
            throw Fail(error);
        }

        return Files.TryGetValue(path, out var bytes)
            ? new Content(bytes, ReportedLength ?? bytes.Length)
            : throw Fail(FileErrors.FileNotFound);
    }

    public IWritableFile OpenForSave(string path, bool shareWrite)
    {
        Saves.Add((path, shareWrite));
        if (SaveErrors.TryGetValue(path, out var error))
        {
            throw Fail(error);
        }

        var existed = Files.ContainsKey(path);
        if (!existed)
        {
            Files[path] = [];
        }

        return new Writable(this, path, existed);
    }

    public bool Exists(string path) => Files.ContainsKey(path);

    public void Delete(string path)
    {
        Deleted.Add(path);
        Files.Remove(path);
    }

    /// <summary>Paths whose stamp cannot be read.</summary>
    public HashSet<string> StampErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Added to the stamp's time of a path: a write that keeps the content, as a touch does.</summary>
    public Dictionary<string, long> Touches { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Follows the content, so a test changing <see cref="Files"/> changes the stamp as a
    /// real write changes the time.</summary>
    public FileStamp? GetStamp(string path)
    {
        if (StampErrors.Contains(path) || !Files.TryGetValue(path, out var bytes))
        {
            return null;
        }

        var hash = new HashCode();
        hash.AddBytes(bytes);
        return new FileStamp(hash.ToHashCode() + Touches.GetValueOrDefault(path), bytes.Length);
    }

    public string TextOf(string path, System.Text.Encoding encoding) => encoding.GetString(Files[path]);

    private static IOException Fail(int error) => new($"Win32 error {error}", FileErrors.ToHResult(error));

    private sealed class Content(byte[] bytes, long length) : IFileContent
    {
        public long Length => length;

        public int Read(long offset, Span<byte> destination)
        {
            var available = Math.Max(0, bytes.Length - (int)offset);
            var count = Math.Min(available, destination.Length);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }

        public ReadOnlySpan<byte> GetAllBytes() => bytes;

        public void Dispose()
        {
        }
    }

    private sealed class Writable(MemoryFileSystem owner, string path, bool existed) : IWritableFile
    {
        private readonly List<byte> _written = [];
        private int _writes;

        public bool AlreadyExisted { get; } = existed;

        public void Write(ReadOnlySpan<byte> bytes)
        {
            if (owner.FailingWrite is { } failing && failing.WriteIndex == _writes++)
            {
                throw Fail(failing.Error);
            }

            _written.AddRange(bytes.ToArray());
            var old = owner.Files[path];
            var merged = old.Length > _written.Count ? old.ToArray() : new byte[_written.Count];
            _written.CopyTo(merged);
            owner.Files[path] = merged;
        }

        public void SetEndOfFile() => owner.Files[path] = [.. _written];

        public void Dispose()
        {
        }
    }
}
