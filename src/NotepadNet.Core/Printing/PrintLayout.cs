using NotepadNet.Core.Editing;
using NotepadNet.Core.Settings;

namespace NotepadNet.Core.Printing;

/// <summary>
/// <c>GetDeviceCaps</c> of the printer DC: printable area (<c>HORZRES</c>, <c>VERTRES</c>),
/// resolution, physical page and the offset of the printable area on it, all in device pixels.
/// </summary>
public readonly record struct PrinterMetrics(
    int HorzRes, int VertRes, int LogPixelsX, int LogPixelsY, int PhysicalWidth, int PhysicalHeight, int PhysicalOffsetX, int PhysicalOffsetY);

/// <summary>A rectangle in device pixels of the printable area.</summary>
public readonly record struct DeviceRect(int Left, int Top, int Right, int Bottom);

/// <summary>What the print font measured on the printer DC.</summary>
/// <param name="LineHeight"><c>tmHeight + tmExternalLeading</c>.</param>
public readonly record struct PrintFontMetrics(int LineHeight);

/// <summary>Where one job draws, computed once before the first page.</summary>
/// <param name="Margins">Distance from each edge of the printable area, never negative.</param>
/// <param name="Body">Rectangle handed to <c>DrawTextExW</c>; one line shorter at the top for a
/// header and at the bottom for a footer.</param>
/// <param name="LinesPerPage">Whole lines of the body; only used for the "page too small" check.</param>
public sealed record PrintLayout(DeviceRect Margins, DeviceRect Body, int LineHeight, int HeaderY, int FooterY, int HorzRes, int LinesPerPage)
{
    public bool IsTooSmall => LinesPerPage < 1;

    /// <summary>
    /// x of one header/footer column of width <paramref name="width"/> (<c>PrintHeaderFooter</c>):
    /// left at the left margin, center at the middle of the margins, right
    /// ending at the right margin. Columns are not clipped and may overlap.
    /// </summary>
    public int ColumnX(HeaderFooterColumn column, int width) => column switch
    {
        HeaderFooterColumn.Left => Margins.Left,
        HeaderFooterColumn.Center => (((HorzRes - Margins.Right) + Margins.Left) / 2) - (width / 2),
        _ => HorzRes - Margins.Right - width,
    };
}

public enum HeaderFooterColumn
{
    Left,
    Center,
    Right,
}

/// <summary>Page geometry of <c>PrintDocument</c>.</summary>
public static class PrintLayoutCalculator
{
    /// <summary>
    /// <c>lfHeight</c> of the print font: <c>-(pointSize10 * LOGPIXELSY / 720)</c>, truncated (the
    /// original divides; it does not use <c>MulDiv</c> here).
    /// </summary>
    public static int FontHeight(int pointSize10, int logPixelsY) => (int)-((long)pointSize10 * logPixelsY / 720);

    /// <summary>
    /// The Page Setup margins in device pixels. Deliberate difference (parity.md): the original maps
    /// them with the paper size of the Page Setup structure, which <c>/PT</c> takes from the default
    /// printer, so margins came out scaled when that paper differs from the printer's; the port
    /// converts with the printer's own resolution, which gives the same result when they match.
    /// </summary>
    public static DeviceRect MarginsToDevice(PageMargins margins, bool inches, PrinterMetrics device)
    {
        var unit = inches ? 1000 : 2540;
        return new(
            Win32Math.MulDiv(margins.Left, device.LogPixelsX, unit),
            Win32Math.MulDiv(margins.Top, device.LogPixelsY, unit),
            Win32Math.MulDiv(margins.Right, device.LogPixelsX, unit),
            Win32Math.MulDiv(margins.Bottom, device.LogPixelsY, unit));
    }

    /// <summary>
    /// Margins measured from the physical page become distances from the printable area, clamped
    /// at 0; the header sits at the top margin and the footer one line above the bottom margin,
    /// each taking one line from the body.
    /// </summary>
    public static PrintLayout Compute(PrinterMetrics device, DeviceRect marginsOnPaper, int lineHeight, bool hasHeader, bool hasFooter)
    {
        if (lineHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight), lineHeight, "The print font has no height.");
        }

        var left = Math.Max(0, marginsOnPaper.Left - device.PhysicalOffsetX);
        var right = Math.Max(0, marginsOnPaper.Right + device.PhysicalOffsetX + device.HorzRes - device.PhysicalWidth);
        var top = Math.Max(0, marginsOnPaper.Top - device.PhysicalOffsetY);
        var bottom = Math.Max(0, marginsOnPaper.Bottom + device.PhysicalOffsetY + device.VertRes - device.PhysicalHeight);
        var lines = ((device.VertRes - bottom - top) / lineHeight) - (hasHeader ? 1 : 0) - (hasFooter ? 1 : 0);
        var body = new DeviceRect(
            left,
            hasHeader ? top + lineHeight : top,
            device.HorzRes - right,
            device.VertRes - bottom - (hasFooter ? lineHeight : 0));
        return new(new(left, top, right, bottom), body, lineHeight, top, device.VertRes - lineHeight - bottom, device.HorzRes, lines);
    }
}
