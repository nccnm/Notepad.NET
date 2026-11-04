using System.Text;
using NotepadNet.Core.Text;
using NotepadNet.Platform.IO;
using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Text;

/// <summary>
/// Golden files through the real Win32 heuristic, converter and file system
/// (TestData/encoding/README.md). Results of IsTextUnicode below were
/// observed on Windows 11 26300 on 2026-10-29.
/// </summary>
public sealed class GoldenEncodingTests
{
    private const string Vietnamese = "Tiếng Việt có dấu: Xin chào thế giới.\r\n";
    private const string Chinese = "中文测试，你好世界。\r\n";

    private static readonly Win32CodePageConverter Converter = new();
    private static readonly EncodingDetector Detector = new(new Win32UnicodeHeuristic());
    private static readonly DocumentLoader Loader = new(Detector, new TextDecoder(Converter));

    private static (TextEncodingKind Encoding, string Text, bool IsLog) Load(string name, TextEncodingKind forced = TextEncodingKind.Detect)
    {
        using var file = new Win32FileSystem().OpenRead(GoldenFiles.PathOf(name));
        var document = Loader.Load(file.GetAllBytes(), forced, keepNulls: false, ManagedTextBufferFactory.Instance);
        return (document.Encoding, new string(document.Buffer.Chars[..document.Length]), document.StartsWithLogMarker);
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData("utf8-bom.txt", TextEncodingKind.Utf8Bom, Vietnamese)]
    [InlineData("utf8-nobom.txt", TextEncodingKind.Utf8, Vietnamese)]
    [InlineData("utf16le-bom.txt", TextEncodingKind.Utf16LE, Vietnamese)]
    [InlineData("utf16be-bom.txt", TextEncodingKind.Utf16BE, Vietnamese)]
    [InlineData("utf16le-nobom.txt", TextEncodingKind.Utf16LE, "Hello world, this is UTF-16 LE text without a byte order mark.\r\n")]
    [InlineData("empty.txt", TextEncodingKind.Utf8, "")]
    [InlineData("nul-chars.txt", TextEncodingKind.Utf8, "ab c  d")]
    [InlineData("bush.txt", TextEncodingKind.Utf8, "Bush hid the facts")]
    [InlineData("utf16-odd.txt", TextEncodingKind.Utf16LE, "A")]
    [InlineData("log.txt", TextEncodingKind.Utf8, ".LOG\r\nold note\r\n")]
    public void Detects_and_decodes(string name, TextEncodingKind expected, string text)
    {
        var (encoding, decoded, _) = Load(name);

        encoding.Should().Be(expected);
        decoded.Should().Be(text);
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData("ansi-vi-1258.txt")]
    [InlineData("ansi-zh-936.txt")]
    [InlineData("gb18030.txt")]
    [InlineData("utf8-invalid-after-1k.txt")]
    public void Legacy_code_page_files_detect_as_ansi(string name)
    {
        Load(name).Encoding.Should().Be(TextEncodingKind.Ansi);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Bush_hid_the_facts_passes_only_the_statistics_test_and_the_rule_rejects_it()
    {
        var result = new Win32UnicodeHeuristic().Test(GoldenFiles.Read("bush.txt"));

        result.Should().Be(new UnicodeTestResult(true, UnicodeTests.Statistics));
        Load("bush.txt").Encoding.Should().Be(TextEncodingKind.Utf8);
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData("ansi-vi-1258.txt", 1258, Vietnamese)]
    [InlineData("ansi-zh-936.txt", 936, Chinese)]
    public void Ansi_files_decode_with_their_code_page(string name, int codePage, string expected)
    {
        var bytes = GoldenFiles.Read(name);
        var chars = new char[Converter.GetCharCount(codePage, bytes)];

        Converter.GetChars(codePage, bytes, chars);

        // CP1258 stores tone marks as combining characters, and Windows decodes them that way
        // (e.g. "ế" arrives as "ê" + U+0301), so compare canonically.
        new string(chars).Normalize(NormalizationForm.FormC).Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Cp1258_decodes_tone_marks_as_combining_characters()
    {
        var bytes = GoldenFiles.Read("ansi-vi-1258.txt");
        var chars = new char[Converter.GetCharCount(1258, bytes)];
        Converter.GetChars(1258, bytes, chars);

        new string(chars).Should().Contain("́").And.NotBe(Vietnamese);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Forced_gb18030_decodes_supplementary_characters()
    {
        Load("gb18030.txt", TextEncodingKind.Gb18030).Text.Should().Be(Chinese + char.ConvertFromUtf32(0x20000));
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Log_file_is_flagged()
    {
        Load("log.txt").IsLog.Should().BeTrue();
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData("utf8-invalid-after-1k.txt", TextEncodingKind.Utf8, TextEncodingKind.Ansi)]
    [InlineData("utf8-split-at-1k.txt", TextEncodingKind.Utf8, TextEncodingKind.Utf8)]
    [InlineData("utf16le-bom.txt", TextEncodingKind.Utf16LE, TextEncodingKind.Utf16LE)]
    [InlineData("bush.txt", TextEncodingKind.Utf8, TextEncodingKind.Utf8)]
    public void Sniff_of_first_kibibyte_can_disagree_with_full_load(string name, TextEncodingKind sniffed, TextEncodingKind loaded)
    {
        using var file = new Win32FileSystem().OpenRead(GoldenFiles.PathOf(name));
        var prefix = new byte[EncodingDetector.SniffLength];
        var read = file.Read(0, prefix);

        Detector.Sniff(prefix.AsSpan(0, read), fileIsLonger: file.Length > read, TextEncodingKind.Utf8).Should().Be(sniffed);
        Load(name).Encoding.Should().Be(loaded);
    }

    [Fact]
    public void Utf8_golden_file_matches_its_source_text()
    {
        Encoding.UTF8.GetString(GoldenFiles.Read("utf8-nobom.txt")).Should().Be(Vietnamese);
    }
}
