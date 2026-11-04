namespace NotepadNet.Core.Text;

/// <summary>
/// Writable UTF-16 buffer that receives decoded text. The platform layer can back it with
/// memory the editor control takes over directly (<c>EM_SETHANDLE</c>), so a large file is
/// decoded exactly once into its final location.
/// </summary>
public interface ITextBuffer : IDisposable
{
    /// <summary>Capacity in characters, including room for the terminating NUL.</summary>
    int Capacity { get; }

    Span<char> Chars { get; }
}

public interface ITextBufferFactory
{
    /// <summary>Allocates a buffer for <paramref name="length"/> characters plus a terminating NUL.</summary>
    /// <exception cref="OutOfMemoryException">The buffer cannot be allocated.</exception>
    ITextBuffer Allocate(int length);
}

/// <summary>Managed buffer, used where no native hand-off is needed (tests, benchmarks, autosave).</summary>
public sealed class ManagedTextBufferFactory : ITextBufferFactory
{
    public static ManagedTextBufferFactory Instance { get; } = new();

    public ITextBuffer Allocate(int length) => new ManagedTextBuffer(GC.AllocateUninitializedArray<char>(checked(length + 1)));

    private sealed class ManagedTextBuffer(char[] chars) : ITextBuffer
    {
        public int Capacity => chars.Length;

        public Span<char> Chars => chars;

        public void Dispose()
        {
        }
    }
}
