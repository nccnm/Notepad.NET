using System.ComponentModel;
using System.Runtime.InteropServices;
using NotepadNet.Core.Text;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Text;

/// <summary>
/// Text buffer in a moveable local heap block, the memory the Edit control owns after
/// <c>EM_SETHANDLE</c>. The block stays locked while the
/// decoder writes into it; <see cref="Detach"/> unlocks it and hands the handle over.
/// </summary>
public sealed unsafe class LocalTextBuffer : ITextBuffer
{
    private nint _handle;
    private char* _chars;

    /// <exception cref="OutOfMemoryException">The block cannot be allocated.</exception>
    public LocalTextBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        var bytes = checked((nuint)capacity * sizeof(char));
        _handle = Kernel32Ui.LocalAlloc(Kernel32Ui.LMEM_MOVEABLE, bytes);
        if (_handle == 0)
        {
#pragma warning disable CA2201 // ITextBufferFactory reports allocation failure as OutOfMemoryException.
            throw new OutOfMemoryException($"LocalAlloc of {bytes} bytes failed.");
#pragma warning restore CA2201
        }

        _chars = (char*)Kernel32Ui.LocalLock(_handle);
        if (_chars == null)
        {
            var error = Marshal.GetLastPInvokeError();
            Kernel32Ui.LocalFree(_handle);
            _handle = 0;
            throw new Win32Exception(error);
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public Span<char> Chars
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return new(_chars, Capacity);
        }
    }

    /// <summary>Unlocks the block and gives up ownership of it.</summary>
    /// <returns>The <c>HLOCAL</c> to pass to <c>EM_SETHANDLE</c>.</returns>
    public nint Detach()
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var handle = _handle;
        Kernel32Ui.LocalUnlock(handle);
        _handle = 0;
        _chars = null;
        return handle;
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            Kernel32Ui.LocalUnlock(_handle);
            Kernel32Ui.LocalFree(_handle);
            _handle = 0;
            _chars = null;
        }
    }
}

public sealed class LocalTextBufferFactory : ITextBufferFactory
{
    public static LocalTextBufferFactory Instance { get; } = new();

    public ITextBuffer Allocate(int length) => new LocalTextBuffer(checked(length + 1));
}
