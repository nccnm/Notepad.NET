using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Settings;
using NotepadNet.Core.StatusBar;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Editing;

/// <summary>Zoom, MulDiv, time/date, message formatting, status bar and settings defaults.</summary>
public sealed class SmallRulesTests
{
    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(0, 0, 100)]
    [InlineData(0, 5, 100)]
    [InlineData(3, 0, 100)]
    [InlineData(1, 1, 100)]
    [InlineData(3, 2, 150)]
    [InlineData(1, 3, 33)]
    [InlineData(500, 100, 500)]
    public void Zoom_percent(int numerator, int denominator, int percent) =>
        ZoomPolicy.Percent(new(numerator, denominator)).Should().Be(percent);

    [Theory]
    [Trait("Doc", "parity.md#lenh")]
    [InlineData(100, 10, 110)]
    [InlineData(100, -10, 90)]
    [InlineData(495, 10, 500)]
    [InlineData(500, 10, 500)]
    [InlineData(15, -10, 10)]
    [InlineData(5, -10, 10)]
    public void Zoom_steps_are_clamped(int percent, int delta, int expected) =>
        ZoomPolicy.StepBy(new(percent, 100), delta).Should().Be(new ZoomRatio(expected, 100));

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(10, 96, 72, 13)]
    [InlineData(10, 144, 72, 20)]
    [InlineData(-7, 96, 7, -96)]
    [InlineData(7, -96, 7, -96)]
    [InlineData(3, 1, 2, 2)]
    [InlineData(-3, 1, 2, -2)]
    [InlineData(5, 5, 0, -1)]
    [InlineData(int.MaxValue, 2, 1, -1)]
    public void MulDiv_rounds_half_away_from_zero(int a, int b, int c, int expected) =>
        Win32Math.MulDiv(a, b, c).Should().Be(expected);

    [Theory]
    [Trait("Doc", "parity.md#lenh")]
    [InlineData(100, 96, -13)]
    [InlineData(100, 144, -20)]
    [InlineData(120, 120, -20)]
    public void Font_height_for_dpi(int pointSize10, int dpi, int height) =>
        new FontSpec("Lucida Console", pointSize10).HeightForDpi(dpi).Should().Be(height);

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Time_comes_before_date_and_log_adds_crlf_on_both_sides()
    {
        var inserter = new TimeDateInserter(new FixedClock(new DateTime(2026, 10, 29, 14, 44, 59)), new FakeDateTimeFormatter());

        inserter.Stamp(false).Should().Be("2:44 PM 10/29/2026");
        inserter.Stamp(true).Should().Be("\r\n2:44 PM 10/29/2026\r\n");
    }

    [Theory]
    [Trait("Doc", "parity.md#lenh")]
    [InlineData(false, 1, false, 0x1u)]
    [InlineData(true, 1, true, 0x21u)]
    [InlineData(true, 1, false, 0x11u)]
    [InlineData(true, 6, false, 0x21u)]
    [InlineData(true, 8, false, 0x21u)]
    [InlineData(true, 10, false, 0x21u)]
    [InlineData(true, 23, false, 0x21u)]
    [InlineData(true, 2, true, 0x11u)]
    public void Date_reading_order_flags(bool userRtl, int calendar, bool uiRtl, uint flags)
    {
        var uiAsked = false;

        var result = DateFlagRules.Select(userRtl, () => calendar, () =>
        {
            uiAsked = true;
            return uiRtl;
        });

        result.Should().Be(flags);
        uiAsked.Should().Be(userRtl && calendar == 1, "the UI language is only consulted for Gregorian");
    }

    [Theory]
    [Trait("Doc", "parity.md#hop-thoai-he-thong")]
    [InlineData("The %% file is large", "a.txt", "The a.txt file is large")]
    [InlineData("%%\nNo permission", "C:\\x", "C:\\x\nNo permission")]
    [InlineData("No marker", "x", "No marker")]
    [InlineData("%% and %%", "x", "x and %%")]
    [InlineData("ends with %", "x", "ends with x")]
    public void Insert_argument_replaces_the_first_placeholder(string format, string argument, string expected) =>
        MessageText.InsertArgument(format, argument).Should().Be(expected);

    [Fact]
    [Trait("Doc", "parity.md#hop-thoai-he-thong")]
    public void Insert_argument_cuts_inputs_to_512_characters()
    {
        var result = MessageText.InsertArgument("%%" + new string('f', 600), new string('a', 600));

        result.Should().Be(new string('a', 512) + new string('f', 510));
    }

    [Theory]
    [InlineData("%1%2 - App", new[] { "*", "a.txt" }, "*a.txt - App")]
    [InlineData("100%% %1", new[] { "x" }, "100% x")]
    [InlineData("%3 stays", new[] { "x" }, "%3 stays")]
    [InlineData("trailing %", new string[0], "trailing %")]
    public void Format_inserts(string format, string[] arguments, string expected) =>
        MessageText.FormatInserts(format, arguments).Should().Be(expected);

    [Theory]
    [InlineData(" Ln %d, Col %d", new object[] { 3, 14 }, " Ln 3, Col 14")]
    [InlineData(" %d%%", new object[] { 120 }, " 120%")]
    [InlineData(" %s", new object[] { "UTF-8" }, " UTF-8")]
    [InlineData("odd %q and %", new object[0], "odd %q and %")]
    [InlineData("Common Dialog error (0x%04x)", new object[] { 0x1009 }, "Common Dialog error (0x1009)")]
    [InlineData("Common Dialog error (0x%04x)", new object[] { 9 }, "Common Dialog error (0x0009)")]
    [InlineData("[%5d]", new object[] { 42 }, "[   42]")]
    [InlineData("%%%", new object[0], "%%")]
    [InlineData("end %5", new object[0], "end %5")]
    [InlineData("%5%", new object[0], "%5%")]
    public void Printf_subset(string format, object[] arguments, string expected) =>
        MessageText.Printf(format, arguments).Should().Be(expected);

    [Fact]
    public void Missing_strings_throw()
    {
        FluentActions.Invoking(() => ResxStrings.Instance.GetString("NoSuchString")).Should().Throw<KeyNotFoundException>();
    }

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(StringKeys.TitleFormat, "%1%2 - Notepad.NET")]
    [InlineData(StringKeys.AppName, "Notepad.NET")]
    [InlineData(StringKeys.LineColumn, " Ln %d, Col %d")]
    [InlineData(StringKeys.AllFilesFilter, "All Files ")]
    [InlineData(StringKeys.SaveButton, "&Save")]
    [InlineData(StringKeys.DontSaveButton, "Do&n't Save")]
    public void Resource_strings_keep_the_original_text(string key, string text) =>
        ResxStrings.Instance.GetString(key).Should().Be(text);

    [Fact]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    public void Status_bar_texts()
    {
        var h = new Harness("line one\r\nline two");
        h.Editor.CaretIndex = 13;
        h.Editor.Zoom = new(120, 100);
        h.Editor.EndOfLine = EndOfLineKind.Lf;
        h.State.CurrentEncoding = TextEncodingKind.Gb18030;

        h.StatusBar.UpdateAll();

        h.StatusView.Parts.Should().Equal("", "  Ln 2, Col 4", " 120%", " Unix (LF)", " GB18030");
    }

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(EndOfLineKind.CrLf, " Windows (CRLF)")]
    [InlineData(EndOfLineKind.Cr, " Macintosh (CR)")]
    [InlineData(EndOfLineKind.Lf, " Unix (LF)")]
    [InlineData(EndOfLineKind.Detect, " Windows (CRLF)")]
    [InlineData((EndOfLineKind)9, " Windows (CRLF)")]
    public void End_of_line_names(EndOfLineKind kind, string text) =>
        new Harness().StatusBar.EndOfLineText(kind).Should().Be(text);

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(TextEncodingKind.Ansi, "ANSI")]
    [InlineData(TextEncodingKind.Utf16LE, "UTF-16 LE")]
    [InlineData(TextEncodingKind.Utf16BE, "UTF-16 BE")]
    [InlineData(TextEncodingKind.Utf8Bom, "UTF-8 with BOM")]
    [InlineData(TextEncodingKind.Utf8, "UTF-8")]
    [InlineData(TextEncodingKind.Gb18030, "GB18030")]
    [InlineData(TextEncodingKind.Detect, "ANSI")]
    [InlineData((TextEncodingKind)42, "ANSI")]
    public void Encoding_names(TextEncodingKind kind, string name) =>
        new Harness().StatusBar.EncodingName(kind).Should().Be(name);

    [Fact]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    public void Line_column_is_only_resent_when_it_changes_or_is_forced()
    {
        var h = new Harness("abc");
        h.StatusBar.UpdateLineColumn(force: true);
        var updates = h.StatusView.Updates;

        h.StatusBar.UpdateLineColumn(force: false);
        h.StatusView.Updates.Should().Be(updates);

        h.Editor.CaretIndex = 2;
        h.StatusBar.UpdateLineColumn(force: false);
        h.StatusView.Parts[1].Should().Be("  Ln 1, Col 3");

        h.StatusBar.UpdateLineColumn(force: true);
        h.StatusView.Updates.Should().Be(updates + 2);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Message_part_is_free_text()
    {
        var h = new Harness();

        h.StatusBar.SetMessage(" Found next from the top");

        h.StatusView.Parts[0].Should().Be(" Found next from the top");
    }

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(1000, 96, new[] { 570, 710, 760, 880, -1 })]
    [InlineData(1000, 144, new[] { 355, 565, 640, 820, -1 })]
    [InlineData(300, 96, new[] { 0, 10, 60, 180, -1 })]
    [InlineData(100, 96, new[] { 0, 0, 0, 0, -1 })]
    public void Status_bar_part_edges(int width, int dpi, int[] edges) =>
        StatusBarLayout.RightEdges(width, dpi).Should().Equal(edges);

    [Theory]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    [InlineData(1000, 96, new[] { 570, 140, 50, 120, 120 })]
    [InlineData(300, 96, new[] { 0, 10, 50, 120, 120 })]
    [InlineData(100, 96, new[] { 0, 0, 0, 0, 100 })]
    public void Status_bar_part_widths(int width, int dpi, int[] widths) =>
        StatusBarLayout.Widths(width, dpi).Should().Equal(widths);

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Settings_defaults_match_the_registry_table()
    {
        var store = new InMemorySettingsStore();
        var settings = store.Load();

        settings.Font.Should().Be(new FontSpec("Lucida Console", 100));
        settings.WordWrap.Should().BeFalse();
        settings.StatusBar.Should().BeTrue();
        settings.DefaultEncoding.Should().Be(TextEncodingKind.Utf8);
        settings.WindowsOnlyEol.Should().BeFalse();
        settings.PasteOriginalEol.Should().BeFalse();
        settings.FontChanged.Should().BeFalse();
        settings.ViewChanged.Should().BeFalse();

        store.Save(settings, new WindowRect(1, 2, 3, 4));
        store.LastSaved.Should().BeSameAs(settings);
        store.LastWindow.Should().Be(new WindowRect(1, 2, 3, 4));
    }
}
