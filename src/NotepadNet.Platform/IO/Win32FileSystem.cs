using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NotepadNet.Core.IO;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.IO;

/// <summary>
/// <see cref="FileSystemWatcher"/> on the folder of the file: no polling, and it also sees an editor
/// that saves by replacing the file. Its error event, a lost notification, counts as a change.
/// </summary>
public sealed class Win32FileWatcher : IFileWatcher
{
    public IDisposable? Watch(string path, Action changed)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(changed);
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Deleted += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// File access with the same <c>CreateFileW</c> modes as the original: reads share read and
/// write, saves open <c>OPEN_ALWAYS</c> and overwrite in place.
/// </summary>
public sealed class Win32FileSystem : IFileSystem
{
    public IFileContent OpenRead(string path)
    {
        var handle = Kernel32.CreateFile(
            path, Kernel32.GENERIC_READ, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0,
            Kernel32.OPEN_EXISTING, Kernel32.FILE_ATTRIBUTE_NORMAL, 0);
        ThrowIfInvalid(handle, path);
        return new MappedFileContent(handle);
    }

    public IWritableFile OpenForSave(string path, bool shareWrite)
    {
        var share = Kernel32.FILE_SHARE_READ | (shareWrite ? Kernel32.FILE_SHARE_WRITE : 0);
        var handle = Kernel32.CreateFile(
            path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE, share, 0,
            Kernel32.OPEN_ALWAYS, Kernel32.FILE_ATTRIBUTE_NORMAL, 0);
        var error = Marshal.GetLastPInvokeError();
        ThrowIfInvalid(handle, path);
        return new InPlaceWritableFile(handle, alreadyExisted: error == Kernel32.ERROR_ALREADY_EXISTS);
    }

    /// <summary>Like <c>PathFileExistsW</c>: true for directories too.</summary>
    public bool Exists(string path) => Kernel32.GetFileAttributes(path) != Kernel32.INVALID_FILE_ATTRIBUTES;

    public void Delete(string path)
    {
        if (!Kernel32.DeleteFile(path))
        {
            throw ToIOException(Marshal.GetLastPInvokeError(), path);
        }
    }

    /// <summary>Asks a handle rather than the directory entry, which can lag behind while another
    /// program holds the file open. Access 0 opens a file even when its holder shares nothing.</summary>
    public FileStamp? GetStamp(string path)
    {
        using var handle = Kernel32.CreateFile(
            path, 0, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE | Kernel32.FILE_SHARE_DELETE, 0,
            Kernel32.OPEN_EXISTING, Kernel32.FILE_ATTRIBUTE_NORMAL, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        try
        {
            return new FileStamp(File.GetLastWriteTimeUtc(handle).Ticks, RandomAccess.GetLength(handle));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static void ThrowIfInvalid(SafeFileHandle handle, string path)
    {
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw ToIOException(error, path);
        }
    }

    internal static IOException ToIOException(int error, string path) =>
        new($"{Marshal.GetPInvokeErrorMessage(error)} ({path})", FileErrors.ToHResult(error));

    private sealed class MappedFileContent(SafeFileHandle handle) : IFileContent
    {
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private unsafe byte* _pointer;

        public long Length { get; } = RandomAccess.GetLength(handle);

        public int Read(long offset, Span<byte> destination) => RandomAccess.Read(handle, destination, offset);

        public unsafe ReadOnlySpan<byte> GetAllBytes()
        {
            if (Length == 0)
            {
                return [];
            }

            if (Length > int.MaxValue)
            {
                throw new InvalidOperationException("The file is too large to map into one span.");
            }

            if (_pointer == null)
            {
                _map = MemoryMappedFile.CreateFromFile(handle, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
                _view = _map.CreateViewAccessor(0, Length, MemoryMappedFileAccess.Read);
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                _pointer = pointer + _view.PointerOffset;
            }

            return new ReadOnlySpan<byte>(_pointer, (int)Length);
        }

        public unsafe void Dispose()
        {
            if (_pointer != null)
            {
                _view!.SafeMemoryMappedViewHandle.ReleasePointer();
                _pointer = null;
            }

            _view?.Dispose();
            _map?.Dispose();
            handle.Dispose();
        }
    }

    private sealed class InPlaceWritableFile(SafeFileHandle handle, bool alreadyExisted) : IWritableFile
    {
        private long _position;

        public bool AlreadyExisted { get; } = alreadyExisted;

        public void Write(ReadOnlySpan<byte> bytes)
        {
            RandomAccess.Write(handle, bytes, _position);
            _position += bytes.Length;
        }

        public void SetEndOfFile() => RandomAccess.SetLength(handle, _position);

        public void Dispose() => handle.Dispose();
    }
}
