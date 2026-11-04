using System.Text;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Text;

/// <summary>Encoding detection: BOM, <c>IsTextUnicode</c> and the 100-byte rule, strict UTF-8,
/// forced encodings, and the 1 KiB sniff of the Open dialog (<c>SniffFileEncoding</c>).</summary>
public sealed class EncodingDetectorTests
{
    private static EncodingDetector Detector(bool isUnicode = false, UnicodeTests passed = UnicodeTests.None) =>
        new(new FakeUnicodeHeuristic(isUnicode, passed));

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, TextEncodingKind.Utf8Bom, 3)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, TextEncodingKind.Utf8Bom, 3)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, TextEncodingKind.Utf16LE, 2)]
    [InlineData(new byte[] { 0xFF, 0xFE }, TextEncodingKind.Utf16LE, 2)]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, TextEncodingKind.Utf16BE, 2)]
    public void Detect_recognizes_byte_order_marks(byte[] data, TextEncodingKind expected, int offset)
    {
        var heuristic = new FakeUnicodeHeuristic(isUnicode: true, UnicodeTests.Ascii16);
        var decision = new EncodingDetector(heuristic).Detect(data);

        decision.Should().Be(new EncodingDecision(expected, offset));
        heuristic.Calls.Should().Be(0, "a BOM decides before IsTextUnicode runs");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Detect_treats_EF_BB_without_BF_as_no_BOM()
    {
        var heuristic = new FakeUnicodeHeuristic();
        var decision = new EncodingDetector(heuristic).Detect([0xEF, 0xBB, 0x41]);

        heuristic.Calls.Should().Be(1);
        decision.Should().Be(new EncodingDecision(TextEncodingKind.Ansi, 0), "EF BB 41 is not valid UTF-8");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Statistics_only_match_on_short_input_is_ignored()
    {
        var data = "Bush hid the facts"u8.ToArray();

        Detector(true, UnicodeTests.Statistics).Detect(data)
            .Should().Be(new EncodingDecision(TextEncodingKind.Utf8, 0));
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(99, TextEncodingKind.Utf8)]
    [InlineData(100, TextEncodingKind.Utf16LE)]
    public void Statistics_only_match_is_trusted_from_100_bytes(int length, TextEncodingKind expected)
    {
        var data = Enumerable.Repeat((byte)'a', length).ToArray();

        Detector(true, UnicodeTests.Statistics).Detect(data).Encoding.Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Match_with_more_than_statistics_is_trusted_on_short_input()
    {
        Detector(true, UnicodeTests.Statistics | UnicodeTests.Ascii16).Detect("a\0b\0"u8)
            .Should().Be(new EncodingDecision(TextEncodingKind.Utf16LE, 0));
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Valid_utf8_and_plain_ascii_open_as_utf8()
    {
        Detector().Detect(Encoding.UTF8.GetBytes("Tiếng Việt")).Encoding.Should().Be(TextEncodingKind.Utf8);
        Detector().Detect("plain ascii"u8).Encoding.Should().Be(TextEncodingKind.Utf8);
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(new byte[] { 0x41, 0xE9, 0x42 })]             // lone Latin-1 byte
    [InlineData(new byte[] { 0xC0, 0xAF })]                   // overlong
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]             // UTF-16 surrogate encoded
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]       // above U+10FFFF
    [InlineData(new byte[] { 0xE2, 0x82 })]                   // truncated at end of file
    public void Invalid_utf8_falls_back_to_ansi(byte[] data)
    {
        Detector().Detect(data).Encoding.Should().Be(TextEncodingKind.Ansi);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Empty_file_is_utf8()
    {
        Detector().Detect([]).Should().Be(new EncodingDecision(TextEncodingKind.Utf8, 0));
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(TextEncodingKind.Ansi, new byte[] { 0xFF, 0xFE }, TextEncodingKind.Ansi, 0)]
    [InlineData(TextEncodingKind.Utf16LE, new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, TextEncodingKind.Utf16LE, 2)]
    [InlineData(TextEncodingKind.Utf16LE, new byte[] { 0x41, 0x00 }, TextEncodingKind.Utf16LE, 0)]
    [InlineData(TextEncodingKind.Utf16LE, new byte[] { 0xFE, 0xFF }, TextEncodingKind.Utf16LE, 0)]
    [InlineData(TextEncodingKind.Utf16BE, new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, TextEncodingKind.Utf16BE, 2)]
    [InlineData(TextEncodingKind.Utf16BE, new byte[] { 0x00, 0x41 }, TextEncodingKind.Utf16BE, 0)]
    [InlineData(TextEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, TextEncodingKind.Utf8Bom, 3)]
    [InlineData(TextEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB, 0xBF }, TextEncodingKind.Utf8Bom, 3)]
    [InlineData(TextEncodingKind.Utf8Bom, new byte[] { 0xEF, 0xBB }, TextEncodingKind.Utf8Bom, 0)]
    [InlineData(TextEncodingKind.Utf8Bom, new byte[] { 0x41 }, TextEncodingKind.Utf8Bom, 0)]
    [InlineData(TextEncodingKind.Utf8, new byte[] { 0xEF, 0xBB, 0xBF }, TextEncodingKind.Utf8, 0)]
    [InlineData(TextEncodingKind.Gb18030, new byte[] { 0x41 }, TextEncodingKind.Gb18030, 0)]
    [InlineData((TextEncodingKind)9, new byte[] { 0x41 }, TextEncodingKind.Ansi, 0)]
    public void Forced_encoding_table(TextEncodingKind forced, byte[] data, TextEncodingKind expected, int offset)
    {
        EncodingDetector.Forced(forced, data).Should().Be(new EncodingDecision(expected, offset));
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(new byte[] { }, TextEncodingKind.Utf16BE)]
    [InlineData(new byte[] { 0xFF }, TextEncodingKind.Utf16BE)]
    [InlineData(new byte[] { 0xEF, 0xBB }, TextEncodingKind.Utf16BE)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0x41 }, TextEncodingKind.Utf16BE)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, TextEncodingKind.Utf8Bom)]
    [InlineData(new byte[] { 0xFF, 0xFE }, TextEncodingKind.Utf16LE)]
    [InlineData(new byte[] { 0xFE, 0xFF }, TextEncodingKind.Utf16BE)]
    [InlineData(new byte[] { 0x41, 0x42 }, TextEncodingKind.Utf8)]
    [InlineData(new byte[] { 0x41, 0xE9 }, TextEncodingKind.Ansi)]
    public void Sniff_rules(byte[] prefix, TextEncodingKind expected)
    {
        // Default chosen as UTF-16 BE only to make "returns the default" visible.
        Detector().Sniff(prefix, fileIsLonger: false, defaultEncoding: TextEncodingKind.Utf16BE).Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Sniff_applies_the_statistics_rule()
    {
        var shortText = "Bush hid the facts"u8.ToArray();
        Detector(true, UnicodeTests.Statistics).Sniff(shortText, false, TextEncodingKind.Utf8).Should().Be(TextEncodingKind.Utf8);
        Detector(true, UnicodeTests.Ascii16).Sniff(shortText, false, TextEncodingKind.Utf8).Should().Be(TextEncodingKind.Utf16LE);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Sniff_accepts_a_sequence_cut_by_the_prefix_boundary_only_when_the_file_continues()
    {
        var prefix = new byte[EncodingDetector.SniffLength];
        prefix.AsSpan().Fill((byte)'a');
        prefix[^2] = 0xE1; // first two bytes of a three-byte sequence
        prefix[^1] = 0x80;

        Detector().Sniff(prefix, fileIsLonger: true, TextEncodingKind.Utf8).Should().Be(TextEncodingKind.Utf8);
        Detector().Sniff(prefix, fileIsLonger: false, TextEncodingKind.Utf8).Should().Be(TextEncodingKind.Ansi);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Sniff_still_rejects_invalid_bytes_when_the_file_continues()
    {
        Detector().Sniff([0x41, 0xE9, 0x41], fileIsLonger: true, TextEncodingKind.Utf8).Should().Be(TextEncodingKind.Ansi);
    }
}
