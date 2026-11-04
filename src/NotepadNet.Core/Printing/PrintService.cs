using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;

namespace NotepadNet.Core.Printing;

/// <summary>How <c>PrintCommand</c> gets its printer.</summary>
public enum PrinterChoice
{
    /// <summary>File > Print: <c>PrintDlgExW</c>.</summary>
    Dialog,

    /// <summary><c>/P</c>: the default printer.</summary>
    Default,

    /// <summary><c>/PT "printer"</c>: the printer named on the command line.</summary>
    Named,
}

/// <summary>Result of asking the platform for a printer DC.</summary>
public enum PrinterOpenStatus
{
    Opened,

    /// <summary>Cancelled, or a failure the original does not report.</summary>
    Silent,

    /// <summary><c>CreateDCW</c> failed: string 25, "Cannot access your printer".</summary>
    CannotAccess,
}

public readonly record struct PrinterOpenResult(PrinterOpenStatus Status, IPrintDevice? Device);

/// <summary>
/// A printer DC for one job. Each method returning an <see cref="int"/> gives 0 on success or
/// the Win32 error (<c>GetLastError</c>) of the failing GDI call.
/// </summary>
public interface IPrintDevice : IDisposable
{
    PrinterMetrics Metrics { get; }

    /// <summary>
    /// Creates and selects the print font (the editor's <c>LOGFONTW</c> with <paramref name="height"/>,
    /// <c>lfWidth</c> 0; a raster font is replaced by the driver's default face) and sets
    /// <c>TRANSPARENT</c> background mode.
    /// </summary>
    PrintFontMetrics SelectFont(FontSpec font, int height);

    /// <summary><c>GetTextExtentPoint32W</c> width.</summary>
    int MeasureWidth(string text);

    /// <summary><c>TextOutW</c>.</summary>
    void DrawLine(string text, int x, int y);

    /// <summary>
    /// <c>DrawTextExW</c> with <c>DT_WORDBREAK | DT_EXPANDTABS | DT_NOPREFIX | DT_EDITCONTROL</c>,
    /// plus <c>DT_RTLREADING | DT_RIGHT</c> for right-to-left reading. Returns
    /// <c>uiLengthDrawn</c>, the characters that fit in <paramref name="bounds"/>.
    /// </summary>
    int DrawBody(ReadOnlySpan<char> text, DeviceRect bounds, bool rightToLeft);

    /// <summary><c>SetAbortProc</c> with a procedure that pumps messages until
    /// <paramref name="progress"/> is cancelled.</summary>
    int SetAbortProc(IPrintProgress progress);

    /// <summary><c>StartDocW</c> with the document name shown in the print queue.</summary>
    int StartDocument(string name);

    int StartPage();

    int EndPage();

    int EndDocument();

    void AbortDocument();
}

/// <summary>
/// The modeless "Now Printing" box (dialog 11, <c>PrintAbortDlgProc</c>). It
/// disables the main window while it lives; any command in it (Cancel, Esc) cancels the job.
/// </summary>
public interface IPrintProgress : IDisposable
{
    bool Cancelled { get; }

    /// <summary>Puts "Page N" (string 36) in the box.</summary>
    void ShowPage(int page);
}

/// <summary>What the Page Setup dialog edits.</summary>
public readonly record struct PageSetupValues(string Header, string Footer, PageMargins Margins, bool MarginsInInches);

/// <summary>One <c>PageSetupDlgW</c> call: OK with the new values, or the
/// <c>CommDlgExtendedError</c> code (0 for Cancel).</summary>
public readonly record struct PageSetupResult(bool Accepted, PageSetupValues Values, int Error);

/// <summary>Printer dialogs and DCs: the Win32 side of printing.</summary>
public interface IPrinterService
{
    /// <summary>Opens the printer for one job. <paramref name="printerName"/> is used for
    /// <see cref="PrinterChoice.Named"/> only.</summary>
    PrinterOpenResult Open(PrinterChoice choice, string? printerName);

    /// <summary>Creates the "Now Printing" box showing <paramref name="fileLabel"/>.</summary>
    IPrintProgress ShowProgress(string fileLabel);

    /// <summary>One <c>PageSetupDlgW</c> with the Header/Footer hook and template.</summary>
    PageSetupResult ShowPageSetup(PageSetupValues values);

    /// <summary><c>FreePrinterSettings</c>: forgets the printer and paper chosen so far.</summary>
    void ResetPrinterSelection();
}

/// <summary>What one print job needs from the rest of the app.</summary>
public sealed record PrintServiceDependencies(
    IEditorSurface Editor,
    DocumentService Documents,
    IMainWindow Window,
    NotepadSettings Settings,
    IPrinterService Printers,
    IDialogService Dialogs,
    ILocalizedStrings Strings,
    IClock Clock,
    IDateTimeFormatter Formatter,
    IFileTitleProvider FileTitles,
    ISystemErrorMessages Errors);

/// <summary>
/// File > Page Setup and File > Print, and <c>/P</c>, <c>/PT</c> at startup.
/// </summary>
public sealed class PrintService(PrintServiceDependencies dependencies)
{
    private readonly PrintServiceDependencies _d = dependencies;

    /// <summary>
    /// Page Setup (<c>CommandHandler</c> 0x5): the dialog is shown again after the printer settings
    /// are reset when it fails with <c>PDERR_DNDMMISMATCH</c>, <c>PDERR_PRINTERNOTFOUND</c> or
    /// <c>PDERR_DEFAULTDIFFERENT</c>; another failure is reported by <c>ShowCommDlgError</c>. OK
    /// stores header, footer and margins and marks the Page Setup group as changed.
    /// </summary>
    public void PageSetup()
    {
        var settings = _d.Settings;
        while (true)
        {
            var result = _d.Printers.ShowPageSetup(new(settings.Header, settings.Trailer, settings.Margins, settings.MarginsInInches));
            if (result.Accepted)
            {
                settings.Header = Cut(result.Values.Header);
                settings.Trailer = Cut(result.Values.Footer);
                settings.Margins = result.Values.Margins;
                settings.PageSetupChanged = true;
                return;
            }

            if (result.Error is CommonDialogErrors.DevNamesDevModeMismatch or CommonDialogErrors.PrinterNotFound or CommonDialogErrors.DefaultDifferent)
            {
                _d.Printers.ResetPrinterSelection();
                continue;
            }

            if (CommonDialogErrors.MessageFor(result.Error, _d.Strings) is { } message)
            {
                _d.Dialogs.ShowMessage(message, MessageKind.SystemError);
            }

            return;
        }
    }

    /// <summary>The header and footer buffers hold 40 WCHAR.</summary>
    private static string Cut(string text) =>
        text.Length < SettingsSchema.HeaderCapacity ? text : text[..(SettingsSchema.HeaderCapacity - 1)];

    /// <summary>
    /// <c>PrintCommand</c>: opens the printer, prints the whole document, then reports a failure
    /// with the system message for the error (string 12 when there is none) and the full path or
    /// "Untitled". Cancel by the user, and errors without a code, are silent.
    /// </summary>
    public void Print(PrinterChoice choice, string? printerName = null)
    {
        int error;
        using (_d.Window.ShowWaitCursor())
        {
            var opened = _d.Printers.Open(choice, printerName);
            if (opened.Status == PrinterOpenStatus.CannotAccess)
            {
                _d.Dialogs.ShowMessage(_d.Strings.GetString(StringKeys.CannotAccessPrinter), MessageKind.Warning);
                return;
            }

            if (opened.Device is not { } device)
            {
                return;
            }

            using (device)
            {
                error = PrintDocument(device);
            }
        }

        if (error is 0 or PrintErrors.Cancelled)
        {
            return;
        }

        var text = _d.Errors.Find(error) ?? _d.Strings.GetString(StringKeys.CannotPrint);
        var state = _d.Documents.State;
        var name = state.IsUntitled || state.Path is null ? _d.Strings.GetString(StringKeys.Untitled) : state.Path;
        _d.Dialogs.ShowMessage(MessageText.InsertArgument(text, name), MessageKind.Warning);
    }

    /// <summary>
    /// <c>PrintDocument</c>. Returns 0, <see cref="PrintErrors.Cancelled"/>,
    /// or the Win32 error of the GDI call that failed.
    /// </summary>
    private int PrintDocument(IPrintDevice device)
    {
        var settings = _d.Settings;
        var metrics = device.Metrics;
        var now = _d.Clock.LocalNow;
        var time = _d.Formatter.FormatTime(now);
        var date = _d.Formatter.FormatLongDate(now);
        var font = device.SelectFont(settings.Font, PrintLayoutCalculator.FontHeight(settings.Font.PointSize10, metrics.LogPixelsY));
        var hasHeader = settings.Header.Length != 0;
        var hasFooter = settings.Trailer.Length != 0;
        var layout = PrintLayoutCalculator.Compute(
            metrics, PrintLayoutCalculator.MarginsToDevice(settings.Margins, settings.MarginsInInches, metrics), font.LineHeight, hasHeader, hasFooter);
        if (layout.IsTooSmall)
        {
            ShowPageTooSmall();
            return 0;
        }

        var state = _d.Documents.State;
        var untitled = state.IsUntitled || state.Path is null;
        var fileTitle = untitled ? _d.Strings.GetString(StringKeys.Untitled) : _d.FileTitles.GetTitle(state.Path!);
        var codes = _d.Strings.GetString(StringKeys.HeaderFooterCodes);
        using var progress = _d.Printers.ShowProgress(untitled ? _d.Strings.GetString(StringKeys.Untitled) : PrintFileLabel(state.Path!));
        var error = device.SetAbortProc(progress);
        if (error != 0)
        {
            return error;
        }

        error = device.StartDocument(_d.Window.Title);
        if (error != 0)
        {
            return progress.Cancelled ? PrintErrors.Cancelled : error;
        }

        using var lease = _d.Editor.AcquireText();
        error = PrintPages(device, progress, layout, lease.Text, new(fileTitle, 1, time, date), codes);
        if (progress.Cancelled)
        {
            device.AbortDocument();
            return PrintErrors.Cancelled;
        }

        var end = device.EndDocument();
        return error != 0 ? error : end;
    }

    /// <summary>
    /// One page per <c>DrawTextExW</c>: the body is always wrapped to the page width, and a page
    /// ends where <c>DrawTextExW</c> stops. A page that takes no character ends the job with
    /// string 28. Deliberate difference (parity.md): the header is drawn after <c>StartPage</c>
    /// (the original draws it before, which happens to work on the drivers tested).
    /// </summary>
    private int PrintPages(IPrintDevice device, IPrintProgress progress, PrintLayout layout, ReadOnlySpan<char> text, HeaderFooterValues values, string codes)
    {
        var settings = _d.Settings;
        var rightToLeft = _d.Editor.RightToLeftReading;
        var page = 1;
        while (!progress.Cancelled && text.Length > 0)
        {
            progress.ShowPage(page);
            var error = device.StartPage();
            if (error != 0)
            {
                return error;
            }

            var pageValues = values with { Page = page };
            DrawHeaderFooter(device, layout, settings.Header, layout.HeaderY, pageValues, codes);
            var drawn = device.DrawBody(text, layout.Body, rightToLeft);
            DrawHeaderFooter(device, layout, settings.Trailer, layout.FooterY, pageValues, codes);
            error = device.EndPage();
            if (error != 0)
            {
                return error;
            }

            page++;
            if (drawn == 0)
            {
                ShowPageTooSmall();
                return 0;
            }

            text = text[Math.Min(drawn, text.Length)..];
        }

        return 0;
    }

    private static void DrawHeaderFooter(IPrintDevice device, PrintLayout layout, string pattern, int y, HeaderFooterValues values, string codes)
    {
        if (pattern.Length == 0)
        {
            return;
        }

        var columns = HeaderFooterExpander.Expand(pattern, codes, values);
        Draw(columns.Left, HeaderFooterColumn.Left);
        Draw(columns.Center, HeaderFooterColumn.Center);
        Draw(columns.Right, HeaderFooterColumn.Right);

        void Draw(string text, HeaderFooterColumn column)
        {
            if (text.Length != 0)
            {
                device.DrawLine(text, layout.ColumnX(column, device.MeasureWidth(text)), y);
            }
        }
    }

    private void ShowPageTooSmall() =>
        _d.Dialogs.ShowMessage(_d.Strings.GetString(StringKeys.PageTooSmall), MessageKind.Warning);

    /// <summary>
    /// Name in the "Now Printing" box: what follows the last <c>:</c> or <c>\</c> of the path, or
    /// the whole path when that separator is its first character or there is none.
    /// </summary>
    public static string PrintFileLabel(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var last = path.AsSpan().LastIndexOfAny(':', '\\');
        return last <= 0 ? path : path[(last + 1)..];
    }
}

/// <summary>Return codes of a print job.</summary>
public static class PrintErrors
{
    /// <summary><c>SP_USERABORT</c>: the user cancelled; nothing is reported.</summary>
    public const int Cancelled = -3;
}

/// <summary><c>ShowCommDlgError</c> and the Page Setup retry codes.</summary>
public static class CommonDialogErrors
{
    public const int DevNamesDevModeMismatch = 0x1009;
    public const int PrinterNotFound = 0x100B;
    public const int DefaultDifferent = 0x100C;

    /// <summary><c>CDERR_MEMALLOCFAILURE</c>.</summary>
    public const int MemoryAllocationFailure = 0x9;

    /// <summary>
    /// The text for a <c>CommDlgExtendedError</c> code, or null when nothing is shown: 0 (Cancel)
    /// and <c>PDERR_NODEFAULTPRN</c>, whose entry in the original's table is an empty string.
    /// Codes outside the table read "Common Dialog error (0x%04x)" (string 29).
    /// </summary>
    public static string? MessageFor(int error, ILocalizedStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        string? key = error switch
        {
            0 or 0x1008 => null,
            0xFFFF or 5 or 6 or 0x1005 => StringKeys.NotEnoughMemory,
            2 => StringKeys.FileDialogsFailed,
            0xA => StringKeys.PrintDialogsFailed,
            0x1004 => StringKeys.CannotAccessPrinter,
            _ => StringKeys.CommonDialogError,
        };
        return key switch
        {
            null => null,
            StringKeys.CommonDialogError => MessageText.Printf(strings.GetString(key), error),
            _ => strings.GetString(key),
        };
    }
}
