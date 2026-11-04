using System.Text;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>
/// Stand-in for the Win32 converter built on .NET code page encodings. Good enough to test the
/// Core rules; exact Windows mappings are covered by the Platform tests.
/// </summary>
internal sealed class ManagedCodePageConverter(int ansiCodePage = 1252) : ICodePageConverter
{
    static ManagedCodePageConverter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public int AnsiCodePage { get; } = ansiCodePage;

    public List<(int CodePage, bool NoBestFit)> EncodeCalls { get; } = [];

    public List<int> DecodeCodePages { get; } = [];

    /// <summary>When set, <see cref="GetByteCount"/> returns this instead of the real count.</summary>
    public int? ForcedByteCount { get; set; }

    public int GetCharCount(int codePage, ReadOnlySpan<byte> bytes)
    {
        DecodeCodePages.Add(codePage);
        return Encoding.GetEncoding(codePage).GetCharCount(bytes);
    }

    public int GetChars(int codePage, ReadOnlySpan<byte> bytes, Span<char> chars) =>
        Encoding.GetEncoding(codePage).GetChars(bytes, chars);

    public int GetByteCount(int codePage, ReadOnlySpan<char> chars, bool noBestFit, out bool usedDefaultChar)
    {
        EncodeCalls.Add((codePage, noBestFit));
        var fallback = new RecordingFallback();
        var count = Encoding.GetEncoding(codePage, fallback, DecoderFallback.ReplacementFallback).GetByteCount(chars);
        usedDefaultChar = fallback.Used;
        return ForcedByteCount ?? count;
    }

    public int GetBytes(int codePage, ReadOnlySpan<char> chars, Span<byte> bytes, bool noBestFit)
    {
        EncodeCalls.Add((codePage, noBestFit));
        return Encoding.GetEncoding(codePage, new RecordingFallback(), DecoderFallback.ReplacementFallback).GetBytes(chars, bytes);
    }

    private sealed class RecordingFallback : EncoderFallback
    {
        public bool Used { get; private set; }

        public override int MaxCharCount => 1;

        public override EncoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);

        private sealed class Buffer(RecordingFallback owner) : EncoderFallbackBuffer
        {
            private int _remaining;

            public override int Remaining => _remaining;

            public override bool Fallback(char charUnknown, int index)
            {
                owner.Used = true;
                _remaining = 1;
                return true;
            }

            public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index) => Fallback(charUnknownHigh, index);

            public override char GetNextChar()
            {
                if (_remaining == 0)
                {
                    return '\0';
                }

                _remaining--;
                return '?';
            }

            public override bool MovePrevious() => false;
        }
    }
}
