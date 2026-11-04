namespace NotepadNet.Core.Text;

/// <summary>
/// Encoding of a document. Numeric values match the codes the original Notepad uses in its
/// globals, its Open/Save "Encoding" combo and the <c>iDefaultEncoding</c> registry value
/// so imported settings keep their meaning.
/// </summary>
public enum TextEncodingKind
{
    /// <summary>Let the loader detect the encoding ("Auto-Detect" in the Open dialog).</summary>
    Detect = 0,
    Ansi = 1,
    Utf16LE = 2,
    Utf16BE = 3,
    Utf8Bom = 4,
    Utf8 = 5,
    Gb18030 = 6,
}

public static class TextEncodingKinds
{
    /// <summary>Code page the original uses for GB18030 (0xD698).</summary>
    public const int Gb18030CodePage = 54936;

    /// <summary>Maps any out-of-range stored value to ANSI, as both the loader and the status
    /// bar of the original do for unknown codes.</summary>
    public static TextEncodingKind Normalize(int value) =>
        value is >= 1 and <= 6 ? (TextEncodingKind)value : TextEncodingKind.Ansi;

    public static bool IsUtf16(this TextEncodingKind kind) =>
        kind is TextEncodingKind.Utf16LE or TextEncodingKind.Utf16BE;
}
