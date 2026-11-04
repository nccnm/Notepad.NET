namespace NotepadNet.Core.Text;

/// <summary>
/// Conversions for the Windows code pages the original handles through
/// <c>MultiByteToWideChar</c>/<c>WideCharToMultiByte</c>: the ANSI code page and GB18030.
/// The platform implementation calls those APIs so lossy-save warnings and best-fit
/// mappings match Windows exactly. Valid UTF-8 and all UTF-16 are handled in managed code;
/// malformed UTF-8 comes here as code page 65001 so its U+FFFD replacements match Windows.
/// </summary>
public interface ICodePageConverter
{
    /// <summary>The system ANSI code page (<c>GetACP</c>), read at the time of the call.</summary>
    int AnsiCodePage { get; }

    int GetCharCount(int codePage, ReadOnlySpan<byte> bytes);

    int GetChars(int codePage, ReadOnlySpan<byte> bytes, Span<char> chars);

    /// <summary>Byte count of <paramref name="chars"/> in <paramref name="codePage"/>.</summary>
    /// <param name="noBestFit">Pass <c>WC_NO_BEST_FIT_CHARS</c>.</param>
    /// <param name="usedDefaultChar">Whether a character had no mapping. Always false for code
    /// pages that cannot report it (GB18030).</param>
    int GetByteCount(int codePage, ReadOnlySpan<char> chars, bool noBestFit, out bool usedDefaultChar);

    int GetBytes(int codePage, ReadOnlySpan<char> chars, Span<byte> bytes, bool noBestFit);
}
