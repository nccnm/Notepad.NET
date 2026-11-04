using System.Text;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Text;

/// <summary>Save-side encoding: BOM, ANSI loss warning, best-fit.</summary>
public sealed class TextEncoderTests
{
    private readonly ManagedCodePageConverter _converter = new(1252);

    private TextEncoder Encoder => new(_converter);

    private byte[] Body(string text, TextEncodingKind kind, bool allowBestFit = false)
    {
        var sink = new MemorySink();
        Encoder.WriteBody(text, kind, allowBestFit, sink);
        return sink.Bytes;
    }

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData(TextEncodingKind.Utf16LE, new byte[] { 0xFF, 0xFE })]
    [InlineData(TextEncodingKind.Utf16BE, new byte[] { 0xFE, 0xFF })]
    [InlineData(TextEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(TextEncodingKind.Utf8, new byte[] { })]
    [InlineData(TextEncodingKind.Ansi, new byte[] { })]
    [InlineData(TextEncodingKind.Gb18030, new byte[] { })]
    public void Preamble_per_encoding(TextEncodingKind kind, byte[] expected)
    {
        TextEncoder.GetPreamble(kind).ToArray().Should().Equal(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Utf16_le_body_is_the_raw_buffer()
    {
        Body("Ab\U0001F600", TextEncodingKind.Utf16LE).Should().Equal(Encoding.Unicode.GetBytes("Ab\U0001F600"));
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Utf16_be_body_is_byte_swapped_across_chunks()
    {
        var text = new string('x', 40_000) + "Ωz";

        Body(text, TextEncodingKind.Utf16BE).Should().Equal(Encoding.BigEndianUnicode.GetBytes(text));
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Utf8_body_keeps_surrogate_pair_split_by_chunk_boundary()
    {
        var text = new string('x', 32 * 1024 - 1) + "\U0001F600" + new string('y', 10);

        Body(text, TextEncodingKind.Utf8).Should().Equal(Encoding.UTF8.GetBytes(text));
        Body(text, TextEncodingKind.Utf8Bom).Should().Equal(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Utf8_replaces_lone_surrogates_like_windows()
    {
        Body("a\uD800b", TextEncodingKind.Utf8).Should().Equal(0x61, 0xEF, 0xBF, 0xBD, 0x62);
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Ansi_converts_with_the_current_ansi_code_page_and_no_best_fit()
    {
        Body("café", TextEncodingKind.Ansi).Should().Equal(0x63, 0x61, 0x66, 0xE9);
        _converter.EncodeCalls.Should().AllSatisfy(call => call.Should().Be((1252, true)));
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Accepted_loss_converts_ansi_again_with_best_fit()
    {
        Body("ư", TextEncodingKind.Ansi, allowBestFit: true);

        _converter.EncodeCalls.Should().AllSatisfy(call => call.NoBestFit.Should().BeFalse());
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Gb18030_never_uses_no_best_fit()
    {
        Body("中文", TextEncodingKind.Gb18030, allowBestFit: false).Should().Equal(Encoding.GetEncoding(54936).GetBytes("中文"));
        _converter.EncodeCalls.Should().AllSatisfy(call => call.Should().Be((54936, false)));
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf16LE)]
    [InlineData(TextEncodingKind.Utf16BE)]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.Ansi)]
    public void Empty_text_writes_nothing(TextEncodingKind kind)
    {
        var sink = new MemorySink();
        Encoder.WriteBody([], kind, false, sink);

        sink.Writes.Should().Be(0);
    }

    [Fact]
    public void Zero_byte_conversion_writes_nothing()
    {
        _converter.ForcedByteCount = 0;
        var sink = new MemorySink();

        Encoder.WriteBody("x", TextEncodingKind.Ansi, true, sink);

        sink.Writes.Should().Be(0);
    }

    [Fact]
    public void Requires_a_sink()
    {
        var act = () => Encoder.WriteBody("x", TextEncodingKind.Utf8, false, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData("café", TextEncodingKind.Ansi, false)]
    [InlineData("Tiếng Việt ư", TextEncodingKind.Ansi, true)]
    [InlineData("中文", TextEncodingKind.Ansi, true)]
    [InlineData("中文", TextEncodingKind.Utf8, false)]
    [InlineData("中文", TextEncodingKind.Utf8Bom, false)]
    [InlineData("中文", TextEncodingKind.Utf16LE, false)]
    [InlineData("中文", TextEncodingKind.Utf16BE, false)]
    [InlineData("中文", TextEncodingKind.Gb18030, false)]
    [InlineData("", TextEncodingKind.Ansi, false)]
    public void Loss_warning_rules(string text, TextEncodingKind kind, bool expected)
    {
        Encoder.RequiresLossConfirmation(text, kind).Should().Be(expected);
    }

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData(TextEncodingKind.Ansi)]
    [InlineData(TextEncodingKind.Gb18030)]
    public void Zero_byte_result_for_non_empty_text_warns(TextEncodingKind kind)
    {
        _converter.ForcedByteCount = 0;

        Encoder.RequiresLossConfirmation("x", kind).Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Gb18030_ignores_unmapped_default_char_report()
    {
        // GB18030 cannot report lpUsedDefaultChar; only a zero byte count may warn.
        var converter = new UsedDefaultConverter();
        new TextEncoder(converter).RequiresLossConfirmation("x", TextEncodingKind.Gb18030).Should().BeFalse();
        new TextEncoder(converter).RequiresLossConfirmation("x", TextEncodingKind.Ansi).Should().BeTrue();
    }

    private sealed class UsedDefaultConverter : ICodePageConverter
    {
        public int AnsiCodePage => 1252;

        public int GetCharCount(int codePage, ReadOnlySpan<byte> bytes) => throw new NotSupportedException();

        public int GetChars(int codePage, ReadOnlySpan<byte> bytes, Span<char> chars) => throw new NotSupportedException();

        public int GetByteCount(int codePage, ReadOnlySpan<char> chars, bool noBestFit, out bool usedDefaultChar)
        {
            usedDefaultChar = true;
            return chars.Length;
        }

        public int GetBytes(int codePage, ReadOnlySpan<char> chars, Span<byte> bytes, bool noBestFit) => throw new NotSupportedException();
    }
}
