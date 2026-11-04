using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace NotepadNet.Core.Text;

/// <summary>Decodes file bytes according to an <see cref="EncodingDecision"/>.</summary>
/// <remarks>
/// Deliberate difference (parity.md): byte order follows the chosen encoding. The original
/// decides it from the first two bytes of the file, so forcing UTF-16 BE on a file without
/// a BOM copies little-endian text, and forcing UTF-16 LE on a "FE FF" file swaps it.
/// </remarks>
public sealed class TextDecoder(ICodePageConverter converter)
{
    private const int Utf8CodePage = 65001;

    private readonly ICodePageConverter _converter = converter;

    /// <summary>Length in UTF-16 code units of the decoded text, without a terminator.</summary>
    public int GetCharCount(EncodingDecision decision, ReadOnlySpan<byte> data)
    {
        var payload = data[decision.PayloadOffset..];
        return decision.Encoding switch
        {
            // An odd trailing byte is dropped without notice, as in the original.
            TextEncodingKind.Utf16LE or TextEncodingKind.Utf16BE => payload.Length / 2,
            TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom when Utf8.IsValid(payload) => Encoding.UTF8.GetCharCount(payload),
            _ => _converter.GetCharCount(CodePageOf(decision.Encoding), payload),
        };
    }

    /// <summary>Decodes into <paramref name="destination"/> and returns the characters written.</summary>
    public int Decode(EncodingDecision decision, ReadOnlySpan<byte> data, Span<char> destination)
    {
        var payload = data[decision.PayloadOffset..];
        switch (decision.Encoding)
        {
            case TextEncodingKind.Utf16LE:
            {
                var units = MemoryMarshal.Cast<byte, char>(payload[..(payload.Length & ~1)]);
                units.CopyTo(destination);
                return units.Length;
            }

            case TextEncodingKind.Utf16BE:
            {
                var units = MemoryMarshal.Cast<byte, ushort>(payload[..(payload.Length & ~1)]);
                BinaryPrimitives.ReverseEndianness(units, MemoryMarshal.Cast<char, ushort>(destination)[..units.Length]);
                return units.Length;
            }

            case TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom when Utf8.IsValid(payload):
                return Encoding.UTF8.GetChars(payload, destination);

            default:
                // Includes invalid UTF-8: Windows and .NET emit a different number of U+FFFD
                // for some malformed sequences, so those go through the Win32 converter.
                return _converter.GetChars(CodePageOf(decision.Encoding), payload, destination);
        }
    }

    private int CodePageOf(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Gb18030 => TextEncodingKinds.Gb18030CodePage,
        TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom => Utf8CodePage,
        _ => _converter.AnsiCodePage,
    };
}
