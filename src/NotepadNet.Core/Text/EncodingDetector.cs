using System.Buffers;
using System.Text.Unicode;

namespace NotepadNet.Core.Text;

/// <summary>Where the text payload starts and how to decode it.</summary>
/// <param name="Encoding">Encoding the document will report (status bar, Save default).</param>
/// <param name="PayloadOffset">Bytes to skip before decoding (the BOM, when it is dropped).</param>
public readonly record struct EncodingDecision(TextEncodingKind Encoding, int PayloadOffset);

/// <summary>
/// Encoding rules of the original's file loader and of its 1 KiB pre-scan for the Open dialog.
/// </summary>
public sealed class EncodingDetector(IUnicodeHeuristic heuristic)
{
    /// <summary>Statistics-only matches on inputs shorter than this are ignored
    /// (the "Bush hid the facts" guard).</summary>
    public const int StatisticsMinimumLength = 100;

    /// <summary>Bytes the Open dialog pre-scan reads.</summary>
    public const int SniffLength = 1024;

    private readonly IUnicodeHeuristic _heuristic = heuristic;

    /// <summary>Auto-detection used when a file is loaded without a forced encoding.</summary>
    public EncodingDecision Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return new(TextEncodingKind.Utf8Bom, 3);
        }

        if (StartsWithUtf16LEBom(data))
        {
            return new(TextEncodingKind.Utf16LE, 2);
        }

        if (StartsWithUtf16BEBom(data))
        {
            return new(TextEncodingKind.Utf16BE, 2);
        }

        // No BOM. "EF BB" without the third BOM byte also lands here.
        if (IsUtf16ByHeuristic(data))
        {
            return new(TextEncodingKind.Utf16LE, 0);
        }

        return Utf8.IsValid(data)
            ? new(TextEncodingKind.Utf8, 0)
            : new(TextEncodingKind.Ansi, 0);
    }

    /// <summary>Decoding plan when the user (Open dialog, <c>/A</c>, <c>/W</c>) forced an encoding.</summary>
    public static EncodingDecision Forced(TextEncodingKind forced, ReadOnlySpan<byte> data) => forced switch
    {
        TextEncodingKind.Utf16LE => new(forced, StartsWithUtf16LEBom(data) ? 2 : 0),
        TextEncodingKind.Utf16BE => new(forced, StartsWithUtf16BEBom(data) ? 2 : 0),
        // The original requires more than 2 bytes before it looks for the third BOM byte.
        TextEncodingKind.Utf8Bom => new(forced,
            data.Length > 2 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0),
        // Plain UTF-8 keeps a BOM as U+FEFF text.
        TextEncodingKind.Utf8 => new(forced, 0),
        TextEncodingKind.Gb18030 => new(forced, 0),
        _ => new(TextEncodingKind.Ansi, 0),
    };

    /// <summary>
    /// Pre-scan of the first <see cref="SniffLength"/> bytes, used by the Open dialog when the
    /// user leaves "Auto-Detect": its result becomes the forced encoding of the load.
    /// </summary>
    /// <param name="prefix">At most the first 1 KiB of the file.</param>
    /// <param name="fileIsLonger">The file continues past <paramref name="prefix"/>.</param>
    /// <param name="defaultEncoding"><c>iDefaultEncoding</c>, returned for files under 2 bytes.</param>
    public TextEncodingKind Sniff(ReadOnlySpan<byte> prefix, bool fileIsLonger, TextEncodingKind defaultEncoding)
    {
        if (prefix.Length < 2)
        {
            return defaultEncoding;
        }

        if (prefix[0] == 0xEF && prefix[1] == 0xBB)
        {
            // Unlike the full loader, "EF BB" without "BF" yields the default here.
            return prefix.Length >= 3 && prefix[2] == 0xBF ? TextEncodingKind.Utf8Bom : defaultEncoding;
        }

        if (StartsWithUtf16LEBom(prefix))
        {
            return TextEncodingKind.Utf16LE;
        }

        if (StartsWithUtf16BEBom(prefix))
        {
            return TextEncodingKind.Utf16BE;
        }

        if (IsUtf16ByHeuristic(prefix))
        {
            return TextEncodingKind.Utf16LE;
        }

        return IsValidUtf8Prefix(prefix, fileIsLonger) ? TextEncodingKind.Utf8 : TextEncodingKind.Ansi;
    }

    private bool IsUtf16ByHeuristic(ReadOnlySpan<byte> data)
    {
        var result = _heuristic.Test(data);
        return result.IsUnicode
            && !(result.PassedTests == UnicodeTests.Statistics && data.Length < StatisticsMinimumLength);
    }

    /// <summary>
    /// Strict UTF-8 check of a prefix. Deliberate difference (parity.md): when the file goes on
    /// past the prefix, a multi-byte sequence cut by the 1 KiB boundary does not count as invalid.
    /// </summary>
    private static bool IsValidUtf8Prefix(ReadOnlySpan<byte> prefix, bool fileIsLonger)
    {
        if (!fileIsLonger)
        {
            return Utf8.IsValid(prefix);
        }

        var buffer = ArrayPool<char>.Shared.Rent(prefix.Length);
        try
        {
            var status = Utf8.ToUtf16(prefix, buffer, out _, out _, replaceInvalidSequences: false, isFinalBlock: false);
            return status is OperationStatus.Done or OperationStatus.NeedMoreData;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static bool StartsWithUtf16LEBom(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE;

    private static bool StartsWithUtf16BEBom(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF;
}
