using NotepadNet.Core.IO;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Text;

public sealed class SmallTypesTests
{
    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(1, TextEncodingKind.Ansi)]
    [InlineData(4, TextEncodingKind.Utf8Bom)]
    [InlineData(6, TextEncodingKind.Gb18030)]
    [InlineData(0, TextEncodingKind.Ansi)]
    [InlineData(7, TextEncodingKind.Ansi)]
    [InlineData(-1, TextEncodingKind.Ansi)]
    public void Unknown_encoding_codes_normalize_to_ansi(int value, TextEncodingKind expected)
    {
        TextEncodingKinds.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf16LE, true)]
    [InlineData(TextEncodingKind.Utf16BE, true)]
    [InlineData(TextEncodingKind.Utf8, false)]
    public void IsUtf16(TextEncodingKind kind, bool expected) => kind.IsUtf16().Should().Be(expected);

    [Fact]
    public void Managed_buffer_has_room_for_terminator()
    {
        using var buffer = ManagedTextBufferFactory.Instance.Allocate(3);

        buffer.Capacity.Should().Be(4);
        buffer.Chars.Length.Should().Be(4);
    }

    [Theory]
    [InlineData(FileErrors.AccessDenied, unchecked((int)0x80070005))]
    [InlineData(FileErrors.FileNotFound, unchecked((int)0x80070002))]
    [InlineData(0, 0)]
    public void Win32_error_round_trips_through_hresult(int error, int hresult)
    {
        FileErrors.ToHResult(error).Should().Be(hresult);
        FileErrors.Win32ErrorOf(new IOException("x", hresult)).Should().Be(error);
    }

    [Fact]
    public void Non_win32_hresult_has_no_error_code()
    {
        FileErrors.Win32ErrorOf(new InvalidOperationException()).Should().Be(0);
        var act = () => FileErrors.Win32ErrorOf(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
