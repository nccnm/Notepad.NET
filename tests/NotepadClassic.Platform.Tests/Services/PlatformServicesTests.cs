using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Interop;
using NotepadClassic.Platform.Services;
using NotepadClassic.Platform.Text;

namespace NotepadClassic.Platform.Tests.Services;

/// <summary>Win32 services of phase b, checked against the APIs they wrap.</summary>
public sealed class PlatformServicesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-svc-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- LocalAlloc buffer (EM_SETHANDLE hand-off) ----

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public unsafe void Local_buffer_is_a_moveable_block_that_keeps_its_text_after_detach()
    {
        var buffer = (LocalTextBuffer)LocalTextBufferFactory.Instance.Allocate(5);
        buffer.Capacity.Should().Be(6, "one extra character for the terminating NUL");
        "hello\0".AsSpan().CopyTo(buffer.Chars);

        var handle = buffer.Detach();
        try
        {
            Kernel32Ui.LocalSize(handle).Should().BeGreaterThanOrEqualTo(12);
            var text = (char*)Kernel32Ui.LocalLock(handle);
            new string(text).Should().Be("hello");
            Kernel32Ui.LocalUnlock(handle);
            FluentActions.Invoking(() => buffer.Chars.Length).Should().Throw<ObjectDisposedException>();
            FluentActions.Invoking(buffer.Detach).Should().Throw<ObjectDisposedException>();
            buffer.Dispose();
        }
        finally
        {
            Kernel32Ui.LocalFree(handle).Should().Be(0);
        }
    }

    [Fact]
    public void Disposed_local_buffer_frees_its_block_and_rejects_bad_sizes()
    {
        var buffer = new LocalTextBuffer(3);
        buffer.Dispose();
        buffer.Dispose();

        FluentActions.Invoking(() => buffer.Chars.Length).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => new LocalTextBuffer(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Local_buffer_works_with_the_document_loader()
    {
        var detector = new EncodingDetector(new Win32UnicodeHeuristic());
        var loader = new DocumentLoader(detector, new TextDecoder(new Win32CodePageConverter()));

        using var document = loader.Load("Xin chào"u8, TextEncodingKind.Detect, false, LocalTextBufferFactory.Instance).Buffer;

        new string(document.Chars[..8]).Should().Be("Xin chào");
        document.Chars[8].Should().Be('\0');
    }

    // ---- MulDiv ----

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(10, 96, 72)]
    [InlineData(3, 1, 2)]
    [InlineData(-3, 1, 2)]
    [InlineData(5, 1, -2)]
    [InlineData(-5, -1, 2)]
    [InlineData(7, 3, 2)]
    [InlineData(-7, 3, 2)]
    [InlineData(100, 144, 720)]
    [InlineData(230, 120, 96)]
    [InlineData(5, 5, 0)]
    [InlineData(int.MaxValue, 2, 1)]
    [InlineData(int.MinValue, 1, -1)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void Managed_MulDiv_matches_kernel32(int a, int b, int c) =>
        Win32Math.MulDiv(a, b, c).Should().Be(Kernel32Ui.MulDiv(a, b, c));

    // ---- Time and date ----

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Short_time_has_no_seconds_and_short_date_has_the_year()
    {
        var formatter = new Win32DateTimeFormatter();
        var moment = new DateTime(2026, 10, 29, 14, 44, 59);

        var time = formatter.FormatShortTime(moment);
        var date = formatter.FormatShortDate(moment);

        time.Should().NotBeEmpty().And.NotContain("59");
        date.Should().Contain("26");
        new SystemClock().LocalNow.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Date_flags_follow_the_rtl_rules()
    {
        var flags = Win32DateTimeFormatter.DateFlags();

        if (Win32DateTimeFormatter.IsRtlLocale(Kernel32Ui.LOCALE_USER_DEFAULT))
        {
            flags.Should().BeOneOf(0x11u, 0x21u);
        }
        else
        {
            flags.Should().Be(DateFlagRules.ShortDate);
        }

        Win32DateTimeFormatter.IsRtlLocale(0x0409).Should().BeFalse("en-US is left to right");
        Win32DateTimeFormatter.IsRtlLocale(0x0401).Should().BeTrue("ar-SA is right to left");
    }

    // ---- Show in folder (only the failure: success opens an Explorer window, see the end-to-end tests) ----

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_missing_folder_is_reported_as_a_failure() =>
        new Win32Shell().ShowInFolder(Path.Combine(_dir, "gone", "note.txt")).Should().BeFalse();

    // ---- Clipboard (read only: the user's clipboard is never changed) ----

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Clipboard_check_agrees_with_cf_text_availability()
    {
        var expected = User32.IsClipboardFormatAvailable(User32.CF_TEXT);

        new Win32Clipboard(() => 0).ContainsText().Should().Be(expected);
    }

    // ---- File title ----

    [Fact]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    public unsafe void File_title_uses_the_on_disk_spelling()
    {
        var path = Path.Combine(_dir, "MixedCase.TXT");
        File.WriteAllText(path, "x");
        var expected = FileTitleOf("MixedCase.TXT");

        new Win32FileTitleProvider().GetTitle(path.ToLowerInvariant()).Should().Be(expected);
        expected.Should().StartWith("MixedCase");
    }

    [Fact]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    public void Missing_file_title_comes_from_the_path()
    {
        var path = Path.Combine(_dir, "gone.txt");

        new Win32FileTitleProvider().GetTitle(path).Should().Be(FileTitleOf(path));
    }

    private static unsafe string FileTitleOf(string name)
    {
        var buffer = stackalloc char[260];
        Comdlg32.GetFileTitle(name, buffer, 260).Should().Be(0);
        return new string(buffer);
    }

    // ---- System messages ----

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void System_messages_come_from_FormatMessage()
    {
        var messages = new Win32SystemErrorMessages();

        messages.Find(2).Should().NotBeNullOrWhiteSpace();
        messages.Find(0x3FFF_FFF0).Should().BeNull();
    }

    // ---- Font conversion ----

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Font_spec_round_trips_through_LOGFONT()
    {
        var spec = new FontSpec("Consolas", 115, 700, true, true, true, 1, 2, 3, 4, 49, 5, 6);

        var font = Win32DialogService.ToLogFont(spec, -15);

        font.lfHeight.Should().Be(-15);
        font.lfWidth.Should().Be(0);
        Win32DialogService.FromLogFont(font, 115).Should().Be(spec);
        Win32DialogService.ChooseFontFlags.Should().Be(0x01010041u);
    }

    [Fact]
    public void Long_face_names_are_cut_to_31_characters()
    {
        var font = Win32DialogService.ToLogFont(new FontSpec(new string('a', 40), 100), -13);

        Win32DialogService.FromLogFont(font, 100).FaceName.Should().Be(new string('a', 31));
    }

    // ---- File dialog customization (no dialog is shown) ----

    [Theory]
    [Trait("Doc", "parity.md#hop-thoai-he-thong")]
    [InlineData(1252, true, TextEncodingKind.Detect, TextEncodingKind.Detect)]
    [InlineData(1252, false, TextEncodingKind.Utf16BE, TextEncodingKind.Utf16BE)]
    [InlineData(1252, false, TextEncodingKind.Gb18030, TextEncodingKind.Utf8)]
    [InlineData(936, false, TextEncodingKind.Gb18030, TextEncodingKind.Gb18030)]
    [InlineData(936, true, TextEncodingKind.Utf8Bom, TextEncodingKind.Utf8Bom)]
    public void Encoding_combo_items_and_selection(int ansiCodePage, bool forOpen, TextEncodingKind select, TextEncodingKind selected)
    {
        StaThread.Run(() =>
        {
            var service = new Win32DialogService(() => 0, ResxStrings.Instance, () => ansiCodePage);
            var clsid = forOpen ? FileDialogIds.CLSID_FileOpenDialog : FileDialogIds.CLSID_FileSaveDialog;
            Win32DialogService.CreateDialog<IFileDialog>(clsid, out var dialog).Should().Be(0);
            var customize = (IFileDialogCustomize)dialog;

            service.AddEncodingCombo(customize, forOpen).Should().Be(0);
            Win32DialogService.SelectSaveEncoding(customize, select);

            customize.GetSelectedControlItem(Win32DialogService.EncodingComboId, out var item).Should().Be(0);
            ((TextEncodingKind)item).Should().Be(selected);
            customize.SetSelectedControlItem(Win32DialogService.EncodingComboId, (uint)TextEncodingKind.Detect).Should().Be(forOpen ? 0 : unchecked((int)0x80070057), "only Open has Auto-Detect");
        });
    }
}
