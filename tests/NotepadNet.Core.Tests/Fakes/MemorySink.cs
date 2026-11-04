using System.Buffers;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

internal sealed class MemorySink : IByteSink
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public int Writes { get; private set; }

    public byte[] Bytes => _buffer.WrittenSpan.ToArray();

    public void Write(ReadOnlySpan<byte> bytes)
    {
        Writes++;
        _buffer.Write(bytes);
    }
}
