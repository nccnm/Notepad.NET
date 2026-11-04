using NotepadNet.Core.Text;

namespace NotepadNet.Core.IO;

/// <summary>
/// File access used by Core. Failures surface as <see cref="IOException"/> whose
/// <see cref="Exception.HResult"/> carries the Win32 error (<c>HRESULT_FROM_WIN32</c>), so the
/// UI can show the same system message the original shows.
/// </summary>
public interface IFileSystem
{
    /// <summary>Opens an existing file for reading with read/write sharing, as the original does.</summary>
    IFileContent OpenRead(string path);

    /// <summary>
    /// Opens or creates <paramref name="path"/> for an in-place save (<c>OPEN_ALWAYS</c>,
    /// read+write access). Nothing is truncated until <see cref="IWritableFile.SetEndOfFile"/>.
    /// </summary>
    /// <param name="shareWrite">Save As shares read and write; Save shares read only.</param>
    IWritableFile OpenForSave(string path, bool shareWrite);

    bool Exists(string path);

    void Delete(string path);

    /// <summary>What identifies this version of the file now, or null when it is missing or cannot
    /// be read. Works while another program has the file open, even without sharing.</summary>
    FileStamp? GetStamp(string path);
}

/// <summary>Tells when a file may have changed.</summary>
public interface IFileWatcher
{
    /// <summary>
    /// Calls <paramref name="changed"/>, on any thread, whenever <paramref name="path"/> may have been
    /// written, replaced, renamed or deleted, until the result is disposed. A call only means "look
    /// again". Null when the file's folder cannot be watched.
    /// </summary>
    IDisposable? Watch(string path, Action changed);
}

/// <summary>Last write time and length of a file: when either differs from an earlier stamp, the
/// file was written in between.</summary>
public readonly record struct FileStamp(long LastWriteUtcTicks, long Length);

/// <summary>Content of a file opened for reading.</summary>
public interface IFileContent : IDisposable
{
    long Length { get; }

    /// <summary>Reads up to <paramref name="destination"/>.Length bytes at <paramref name="offset"/>.</summary>
    int Read(long offset, Span<byte> destination);

    /// <summary>The whole file, mapped or read once and kept until disposal.</summary>
    /// <exception cref="InvalidOperationException">The file does not fit in a span (2 GiB or more).</exception>
    ReadOnlySpan<byte> GetAllBytes();
}

public interface IWritableFile : IByteSink, IDisposable
{
    /// <summary>The file existed before this save opened it (<c>ERROR_ALREADY_EXISTS</c>).
    /// A failed save deletes the file only when this is false.</summary>
    bool AlreadyExisted { get; }

    /// <summary>Truncates the file at the current write position.</summary>
    void SetEndOfFile();
}

public static class FileErrors
{
    public const int FileNotFound = 2;
    public const int AccessDenied = 5;
    public const int NotEnoughMemory = 8;
    public const int OutOfMemory = 14;
    public const int SharingViolation = 32;
    public const int NetworkAccessDenied = 65;
    public const int DiskFull = 112;
    public const int InvalidName = 123;

    /// <summary>The Win32 error code inside an exception from <see cref="IFileSystem"/>, or 0.</summary>
    public static int Win32ErrorOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var hr = unchecked((uint)exception.HResult);
        return (hr & 0xFFFF_0000) == 0x8007_0000 ? (int)(hr & 0xFFFF) : 0;
    }

    /// <summary><c>HRESULT_FROM_WIN32</c>.</summary>
    public static int ToHResult(int win32Error) => win32Error <= 0 ? win32Error : unchecked((int)(0x80070000 | (uint)(win32Error & 0xFFFF)));
}
