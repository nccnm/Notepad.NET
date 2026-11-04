using System.Text;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Text;

/// <summary>File load: encoding, side effects (NUL to space, size limit) and the <c>.LOG</c>
/// marker.</summary>
public sealed class DocumentLoaderTests
{
    private readonly ManagedCodePageConverter _converter = new(1252);

    private DocumentLoader Loader(bool isUnicode = false, UnicodeTests passed = UnicodeTests.None) =>
        new(new EncodingDetector(new FakeUnicodeHeuristic(isUnicode, passed)), new TextDecoder(_converter));

    private static string TextOf(DecodedDocument document) => new(document.Buffer.Chars[..document.Length]);

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf8")]
    public void Round_trips_text_for_each_detected_encoding(string kind)
    {
        const string text = "Xin chào, 世界 \U0001F600";
        var (data, expected) = kind switch
        {
            "utf8bom" => (new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray(), TextEncodingKind.Utf8Bom),
            "utf16le" => (new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray(), TextEncodingKind.Utf16LE),
            "utf16be" => (new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray(), TextEncodingKind.Utf16BE),
            _ => (Encoding.UTF8.GetBytes(text), TextEncodingKind.Utf8),
        };

        var document = Loader().Load(data, TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance);

        document.Encoding.Should().Be(expected);
        TextOf(document).Should().Be(text);
        document.Buffer.Chars[document.Length].Should().Be('\0', "the buffer is NUL-terminated like the Edit buffer");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Utf16_without_bom_detected_by_heuristic()
    {
        var data = Encoding.Unicode.GetBytes("abc");

        var document = Loader(true, UnicodeTests.Ascii16).Load(data, TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance);

        document.Encoding.Should().Be(TextEncodingKind.Utf16LE);
        TextOf(document).Should().Be("abc");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Odd_trailing_byte_of_utf16_is_dropped()
    {
        var document = Loader().Load([0xFF, 0xFE, 0x41, 0x00, 0x42], TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance);

        TextOf(document).Should().Be("A");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Forced_utf16_be_on_file_without_bom_is_byte_swapped()
    {
        var document = Loader().Load([0x00, 0x41, 0x00, 0x42], TextEncodingKind.Utf16BE, false, ManagedTextBufferFactory.Instance);

        document.Encoding.Should().Be(TextEncodingKind.Utf16BE);
        TextOf(document).Should().Be("AB");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Ansi_decodes_with_the_ansi_code_page()
    {
        var document = Loader().Load([0x63, 0x61, 0x66, 0xE9], TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance);

        document.Encoding.Should().Be(TextEncodingKind.Ansi);
        TextOf(document).Should().Be("café");
        _converter.DecodeCodePages.Should().Equal(1252);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Malformed_utf8_goes_through_the_code_page_converter_and_valid_utf8_does_not()
    {
        Loader().Load([0x41, 0xE9], TextEncodingKind.Utf8, false, ManagedTextBufferFactory.Instance);
        _converter.DecodeCodePages.Should().Equal(65001);

        _converter.DecodeCodePages.Clear();
        Loader().Load("ok"u8, TextEncodingKind.Utf8, false, ManagedTextBufferFactory.Instance);
        _converter.DecodeCodePages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Gb18030_decodes_with_code_page_54936()
    {
        var data = Encoding.GetEncoding(54936).GetBytes("中文");

        var document = Loader().Load(data, TextEncodingKind.Gb18030, false, ManagedTextBufferFactory.Instance);

        TextOf(document).Should().Be("中文");
        _converter.DecodeCodePages.Should().Equal(54936);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Nul_characters_become_spaces()
    {
        var document = Loader().Load("a\0b\0\0c"u8, TextEncodingKind.Utf8, false, ManagedTextBufferFactory.Instance);

        TextOf(document).Should().Be("a b  c");
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Keep_nulls_keeps_nul_and_skips_log_check()
    {
        var document = Loader().Load(".LOG\0"u8, TextEncodingKind.Utf8, keepNulls: true, ManagedTextBufferFactory.Instance);

        TextOf(document).Should().Be(".LOG\0");
        document.StartsWithLogMarker.Should().BeFalse();
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(".LOG\r\nghi chu cu\r\n", true)]
    [InlineData(".LOGbook", true)]
    [InlineData(".LOG", true)]
    [InlineData(".log\r\n", false)]
    [InlineData(".Log", false)]
    [InlineData(" .LOG", false)]
    [InlineData("\r\n.LOG", false)]
    [InlineData(".LO", false)]
    [InlineData("", false)]
    public void Log_marker_is_case_sensitive_and_must_start_the_text(string text, bool expected)
    {
        var document = Loader().Load(Encoding.UTF8.GetBytes(text), TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance);

        document.StartsWithLogMarker.Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Log_marker_after_bom_still_counts()
    {
        var data = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(".LOG"u8.ToArray()).ToArray();

        Loader().Load(data, TextEncodingKind.Detect, false, ManagedTextBufferFactory.Instance).StartsWithLogMarker.Should().BeTrue();
    }

    [Theory]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    [InlineData(0x3FFF_FFFFL, false)]
    [InlineData(0x4000_0000L, true)]
    [InlineData(0x1_0000_0000L, true)]
    public void Files_of_one_gibibyte_or_more_are_too_large(long length, bool expected)
    {
        DocumentLoader.IsTooLarge(length).Should().Be(expected);
    }

    [Fact]
    public void Requires_a_buffer_factory()
    {
        var act = () => Loader().Load([], TextEncodingKind.Detect, false, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Disposes_the_buffer_when_decoding_fails()
    {
        var factory = new TrackingFactory();
        var loader = new DocumentLoader(new EncodingDetector(new FakeUnicodeHeuristic()), new TextDecoder(new ThrowingConverter()));

        var act = () => loader.Load([0xE9], TextEncodingKind.Ansi, false, factory);

        act.Should().Throw<InvalidOperationException>();
        factory.Disposed.Should().BeTrue();
    }

    private sealed class TrackingFactory : ITextBufferFactory
    {
        public bool Disposed { get; private set; }

        public ITextBuffer Allocate(int length) => new Buffer(this, length + 1);

        private sealed class Buffer(TrackingFactory owner, int capacity) : ITextBuffer
        {
            private readonly char[] _chars = new char[capacity];

            public int Capacity => _chars.Length;

            public Span<char> Chars => _chars;

            public void Dispose() => owner.Disposed = true;
        }
    }

    private sealed class ThrowingConverter : ICodePageConverter
    {
        public int AnsiCodePage => 1252;

        public int GetCharCount(int codePage, ReadOnlySpan<byte> bytes) => bytes.Length;

        public int GetChars(int codePage, ReadOnlySpan<byte> bytes, Span<char> chars) => throw new InvalidOperationException();

        public int GetByteCount(int codePage, ReadOnlySpan<char> chars, bool noBestFit, out bool usedDefaultChar) =>
            throw new NotSupportedException();

        public int GetBytes(int codePage, ReadOnlySpan<char> chars, Span<byte> bytes, bool noBestFit) => throw new NotSupportedException();
    }
}
