using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadNet.Core.Text;

/// <summary>Destination of encoded bytes (the file being saved).</summary>
public interface IByteSink
{
    /// <exception cref="IOException">The write failed.</exception>
    void Write(ReadOnlySpan<byte> bytes);
}

/// <summary>
/// Save-side encoding rules of the original:
/// which BOM is written, when the "characters will be lost" warning (string 27) appears,
/// and how text is converted. No EOL conversion happens on save.
/// </summary>
public sealed class TextEncoder(ICodePageConverter converter)
{
    private const int ChunkChars = 32 * 1024;

    private static readonly byte[] Utf16LEPreamble = [0xFF, 0xFE];
    private static readonly byte[] Utf16BEPreamble = [0xFE, 0xFF];
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];

    private readonly ICodePageConverter _converter = converter;

    public static ReadOnlySpan<byte> GetPreamble(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16LE => Utf16LEPreamble,
        TextEncodingKind.Utf16BE => Utf16BEPreamble,
        TextEncodingKind.Utf8Bom => Utf8Preamble,
        _ => [],
    };

    /// <summary>
    /// Whether saving <paramref name="text"/> as <paramref name="kind"/> must ask the user first.
    /// For ANSI: a character has no exact mapping (<c>WC_NO_BEST_FIT_CHARS</c>). For every
    /// byte-oriented encoding: non-empty text converts to zero bytes. UTF-16 never asks.
    /// </summary>
    public bool RequiresLossConfirmation(ReadOnlySpan<char> text, TextEncodingKind kind)
    {
        if (kind.IsUtf16() || text.IsEmpty)
        {
            return false;
        }

        if (kind is TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom)
        {
            // Managed UTF-8 replaces lone surrogates like WideCharToMultiByte(CP_UTF8, 0) and
            // never yields zero bytes for non-empty input.
            return false;
        }

        var isAnsi = kind != TextEncodingKind.Gb18030;
        var count = _converter.GetByteCount(CodePageOf(kind), text, noBestFit: isAnsi, out var usedDefault);
        return count == 0 || (isAnsi && usedDefault);
    }

    /// <summary>Writes the encoded body (without the preamble).</summary>
    /// <param name="allowBestFit">ANSI only: the user accepted the loss warning, so convert
    /// again without <c>WC_NO_BEST_FIT_CHARS</c> (best-fit lookalikes instead of '?').</param>
    public void WriteBody(ReadOnlySpan<char> text, TextEncodingKind kind, bool allowBestFit, IByteSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (text.IsEmpty)
        {
            return;
        }

        switch (kind)
        {
            case TextEncodingKind.Utf16LE:
                sink.Write(MemoryMarshal.AsBytes(text));
                break;
            case TextEncodingKind.Utf16BE:
                WriteUtf16BE(text, sink);
                break;
            case TextEncodingKind.Utf8:
            case TextEncodingKind.Utf8Bom:
                WriteUtf8(text, sink);
                break;
            default:
                WriteCodePage(text, kind, allowBestFit, sink);
                break;
        }
    }

    private static void WriteUtf16BE(ReadOnlySpan<char> text, IByteSink sink)
    {
        var buffer = ArrayPool<ushort>.Shared.Rent(Math.Min(text.Length, ChunkChars));
        try
        {
            for (var start = 0; start < text.Length; start += ChunkChars)
            {
                var chunk = MemoryMarshal.Cast<char, ushort>(text.Slice(start, Math.Min(ChunkChars, text.Length - start)));
                var swapped = buffer.AsSpan(0, chunk.Length);
                BinaryPrimitives.ReverseEndianness(chunk, swapped);
                sink.Write(MemoryMarshal.AsBytes(swapped));
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(buffer);
        }
    }

    private static void WriteUtf8(ReadOnlySpan<char> text, IByteSink sink)
    {
        var encoder = Encoding.UTF8.GetEncoder();
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(Math.Min(text.Length, ChunkChars)));
        try
        {
            var completed = false;
            while (!completed)
            {
                var take = Math.Min(text.Length, ChunkChars);
                var flush = take == text.Length;
                // The stateful encoder keeps a surrogate pair split by the chunk boundary intact.
                encoder.Convert(text[..take], buffer, flush, out var used, out var written, out completed);
                sink.Write(buffer.AsSpan(0, written));
                text = text[used..];
                completed &= text.IsEmpty;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void WriteCodePage(ReadOnlySpan<char> text, TextEncodingKind kind, bool allowBestFit, IByteSink sink)
    {
        var codePage = CodePageOf(kind);
        var noBestFit = kind != TextEncodingKind.Gb18030 && !allowBestFit;
        var count = _converter.GetByteCount(codePage, text, noBestFit, out _);
        if (count == 0)
        {
            return;
        }

        // One conversion of the whole text: splitting it could cut a surrogate pair or change
        // how a code page with shift states maps it.
        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            var written = _converter.GetBytes(codePage, text, buffer.AsSpan(0, count), noBestFit);
            sink.Write(buffer.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private int CodePageOf(TextEncodingKind kind) =>
        kind == TextEncodingKind.Gb18030 ? TextEncodingKinds.Gb18030CodePage : _converter.AnsiCodePage;
}
