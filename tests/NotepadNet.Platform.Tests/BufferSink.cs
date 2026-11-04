using System.Buffers;
using NotepadNet.Core.Text;

namespace NotepadNet.Platform.Tests;

internal sealed class BufferSink : IByteSink
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public byte[] Bytes => _buffer.WrittenSpan.ToArray();

    public void Write(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);
}
