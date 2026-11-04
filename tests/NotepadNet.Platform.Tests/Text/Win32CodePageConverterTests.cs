using NotepadNet.Core.Text;
using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Text;

/// <summary>ANSI save warning and best-fit.</summary>
public sealed class Win32CodePageConverterTests
{
    private readonly Win32CodePageConverter _converter = new();

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData("café", false)]
    [InlineData("Ā", true)]
    [InlineData("ư", true)]
    [InlineData("中", true)]
    public void No_best_fit_reports_unmapped_characters_in_1252(string text, bool expected)
    {
        _converter.GetByteCount(1252, text, noBestFit: true, out var used);

        used.Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Best_fit_maps_lookalikes_while_no_best_fit_writes_the_default_char()
    {
        Encode(1252, "Ā", noBestFit: false).Should().Equal((byte)'A');
        Encode(1252, "Ā", noBestFit: true).Should().Equal((byte)'?');
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Gb18030_round_trips_supplementary_characters_without_reporting_defaults()
    {
        var text = "中文" + char.ConvertFromUtf32(0x20000);

        var count = _converter.GetByteCount(TextEncodingKinds.Gb18030CodePage, text, noBestFit: false, out var used);
        var bytes = Encode(TextEncodingKinds.Gb18030CodePage, text, noBestFit: false);
        var chars = new char[_converter.GetCharCount(TextEncodingKinds.Gb18030CodePage, bytes)];
        _converter.GetChars(TextEncodingKinds.Gb18030CodePage, bytes, chars);

        used.Should().BeFalse();
        bytes.Length.Should().Be(count);
        new string(chars).Should().Be(text);
    }

    [Fact]
    public void Empty_inputs_convert_to_nothing()
    {
        _converter.GetCharCount(1252, []).Should().Be(0);
        _converter.GetChars(1252, [], []).Should().Be(0);
        _converter.GetByteCount(1252, [], true, out var used).Should().Be(0);
        used.Should().BeFalse();
        _converter.GetBytes(1252, [], [], true).Should().Be(0);
    }

    [Fact]
    public void Invalid_code_page_throws()
    {
        var act = () => _converter.GetCharCount(12345, "x"u8);

        act.Should().Throw<System.ComponentModel.Win32Exception>();
    }

    [Fact]
    public void Text_encoder_writes_ansi_through_the_converter()
    {
        var sink = new BufferSink();
        new TextEncoder(_converter).WriteBody("café", TextEncodingKind.Gb18030, false, sink);

        sink.Bytes.Should().Equal(Encode(TextEncodingKinds.Gb18030CodePage, "café", false));
    }

    private byte[] Encode(int codePage, string text, bool noBestFit)
    {
        var bytes = new byte[_converter.GetByteCount(codePage, text, noBestFit, out _)];
        _converter.GetBytes(codePage, text, bytes, noBestFit);
        return bytes;
    }
}
