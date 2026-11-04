using NotepadNet.Core.Printing;
using NotepadNet.Core.Settings;

namespace NotepadNet.Core.Tests.Printing;

/// <summary>Print layout: font size, margins, body rectangle, header and footer.</summary>
public sealed class PrintLayoutTests
{
    /// <summary>Microsoft Print to PDF, Letter: 600 DPI, the printable area is the whole page.</summary>
    private static readonly PrinterMetrics Pdf = new(5100, 6600, 600, 600, 5100, 6600, 0, 0);

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData(100, 600, -83)]
    [InlineData(110, 600, -91)]
    [InlineData(100, 300, -41)]
    [InlineData(105, 96, -14)]
    [InlineData(99, 720, -99)]
    public void Font_height_truncates_point_size_times_resolution_over_720(int pointSize10, int dpi, int height) =>
        PrintLayoutCalculator.FontHeight(pointSize10, dpi).Should().Be(height);

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Margins_convert_with_the_printer_resolution()
    {
        PrintLayoutCalculator.MarginsToDevice(PageMargins.UsDefault, inches: true, Pdf).Should().Be(new DeviceRect(450, 600, 450, 600));
        PrintLayoutCalculator.MarginsToDevice(PageMargins.MetricDefault, inches: false, Pdf).Should().Be(new DeviceRect(472, 591, 472, 591));
        PrintLayoutCalculator.MarginsToDevice(new(1, 3, 0, 0), inches: true, Pdf with { LogPixelsX = 500, LogPixelsY = 500 })
            .Should().Be(new DeviceRect(1, 2, 0, 0), "rounded like MulDiv: 0.5 -> 1, 1.5 -> 2");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Body_takes_a_line_from_each_side_for_header_and_footer()
    {
        var layout = PrintLayoutCalculator.Compute(Pdf, new(450, 600, 450, 600), lineHeight: 107, hasHeader: true, hasFooter: true);

        layout.Margins.Should().Be(new DeviceRect(450, 600, 450, 600));
        layout.Body.Should().Be(new DeviceRect(450, 707, 4650, 5893));
        layout.HeaderY.Should().Be(600);
        layout.FooterY.Should().Be(6600 - 107 - 600);
        layout.LinesPerPage.Should().Be((5400 / 107) - 2);
        layout.IsTooSmall.Should().BeFalse();

        var plain = PrintLayoutCalculator.Compute(Pdf, new(450, 600, 450, 600), lineHeight: 107, hasHeader: false, hasFooter: false);
        plain.Body.Should().Be(new DeviceRect(450, 600, 4650, 6000));
        plain.LinesPerPage.Should().Be(5400 / 107);
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Margins_are_measured_from_the_printable_area_and_never_negative()
    {
        var printer = new PrinterMetrics(4800, 6300, 600, 600, 5100, 6600, 150, 100);

        var layout = PrintLayoutCalculator.Compute(printer, new(450, 600, 450, 600), 100, false, false);
        layout.Margins.Should().Be(new DeviceRect(300, 500, 450 + 150 + 4800 - 5100, 600 + 100 + 6300 - 6600));

        var tiny = PrintLayoutCalculator.Compute(printer, new(100, 50, 0, 0), 100, false, false);
        tiny.Margins.Should().Be(new DeviceRect(0, 0, 0, 0));
        tiny.Body.Should().Be(new DeviceRect(0, 0, 4800, 6300));
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_page_without_room_for_one_body_line_is_too_small()
    {
        var page = Pdf with { VertRes = 1500, PhysicalHeight = 1500 };

        PrintLayoutCalculator.Compute(page, new(0, 600, 0, 600), 100, true, true).LinesPerPage.Should().Be(1);
        PrintLayoutCalculator.Compute(page, new(0, 650, 0, 650), 100, true, true).IsTooSmall.Should().BeTrue();
        var act = () => PrintLayoutCalculator.Compute(page, default, 0, false, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Columns_sit_at_the_left_margin_the_middle_and_the_right_margin()
    {
        var layout = PrintLayoutCalculator.Compute(Pdf, new(450, 600, 650, 600), 100, true, true);

        layout.ColumnX(HeaderFooterColumn.Left, 300).Should().Be(450);
        layout.ColumnX(HeaderFooterColumn.Center, 301).Should().Be(((5100 - 650 + 450) / 2) - 150);
        layout.ColumnX(HeaderFooterColumn.Right, 300).Should().Be(5100 - 650 - 300);
        layout.ColumnX(HeaderFooterColumn.Right, 6000).Should().BeNegative("columns are not clipped");
    }
}
