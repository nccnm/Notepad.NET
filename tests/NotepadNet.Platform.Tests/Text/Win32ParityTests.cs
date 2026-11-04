using System.Text;
using System.Text.Unicode;
using NotepadNet.Core.Text;
using NotepadNet.Platform.Interop;
using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Text;

/// <summary>
/// Core uses managed UTF-8 for speed where the original calls Win32. These tests pin that the
/// managed paths give the same answers as the Win32 calls the original makes.
/// </summary>
public sealed class Win32ParityTests
{
    private const uint MB_ERR_INVALID_CHARS = 0x8;

    public static TheoryData<byte[]> Utf8Corpus => new()
    {
        Array.Empty<byte>(),
        "plain"u8.ToArray(),
        Encoding.UTF8.GetBytes("Tiếng Việt 中文 \U0001F600"),
        new byte[] { 0xEF, 0xBB, 0xBF, 0x41 },
        new byte[] { 0x00, 0x41 },
        new byte[] { 0xC0, 0x80 },             // overlong NUL
        new byte[] { 0xC1, 0xBF },             // overlong
        new byte[] { 0xE0, 0x80, 0x80 },       // overlong 3-byte
        new byte[] { 0xED, 0xA0, 0x80 },       // high surrogate
        new byte[] { 0xED, 0xBF, 0xBF },       // low surrogate
        new byte[] { 0xEF, 0xBF, 0xBF },       // U+FFFF noncharacter (valid)
        new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }, // U+10FFFF
        new byte[] { 0xF4, 0x90, 0x80, 0x80 }, // above U+10FFFF
        new byte[] { 0xF5, 0x80, 0x80, 0x80 },
        new byte[] { 0x80 },
        new byte[] { 0xE2, 0x82 },
        new byte[] { 0x41, 0xE9, 0x42 },
        new byte[] { 0xFF, 0xFE },
    };

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [MemberData(nameof(Utf8Corpus))]
    public unsafe void Managed_strict_utf8_check_matches_MultiByteToWideChar(byte[] data)
    {
        bool win32Valid;
        fixed (byte* p = data)
        {
            // The original: invalid only when the call fails with ERROR_NO_UNICODE_TRANSLATION.
            var n = Kernel32.MultiByteToWideChar(65001, MB_ERR_INVALID_CHARS, p, data.Length, null, 0);
            win32Valid = !(n == 0 && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == 0x459);
        }

        Utf8.IsValid(data).Should().Be(win32Valid);
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [MemberData(nameof(Utf8Corpus))]
    public void Utf8_decoding_matches_MultiByteToWideChar_including_malformed_input(byte[] data)
    {
        // Managed UTF-8 alone would differ for overlongs, surrogates and > U+10FFFF (it emits
        // one U+FFFD per byte where Windows emits fewer); TextDecoder routes those to Win32.
        var win32 = new char[Converter.GetCharCount(65001, data)];
        Converter.GetChars(65001, data, win32);
        var decoder = new TextDecoder(Converter);
        var decision = new EncodingDecision(TextEncodingKind.Utf8, 0);
        var ours = new char[decoder.GetCharCount(decision, data)];
        var written = decoder.Decode(decision, data, ours);

        new string(ours, 0, written).Should().Be(new string(win32));
    }

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData("a\uD800b")]
    [InlineData("\uDC00")]
    [InlineData("x\U0001F600\uD83D")]
    public void Managed_utf8_encoding_matches_WideCharToMultiByte(string text)
    {
        var win32 = new byte[Converter.GetByteCount(65001, text, noBestFit: false, out _)];
        Converter.GetBytes(65001, text, win32, noBestFit: false);

        Encoding.UTF8.GetBytes(text).Should().Equal(win32);
    }

    private static readonly Win32CodePageConverter Converter = new();
}
