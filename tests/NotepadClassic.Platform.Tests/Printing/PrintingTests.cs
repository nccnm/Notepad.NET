using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Printing;
using NotepadClassic.Core.Services;
using NotepadClassic.Platform.Interop;
using NotepadClassic.Platform.Printing;

namespace NotepadClassic.Platform.Tests.Printing;

/// <summary>
/// GDI printing on "Microsoft Print to PDF" (output to a temporary file), <c>DrawTextExW</c> tab
/// stops, the "Now Printing" box and the Page Setup dialog.
/// </summary>
public sealed unsafe partial class PrintingTests : IDisposable
{
    private const string PdfPrinter = "Microsoft Print to PDF";
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-printing-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Win32PrintDevice OpenPdf() =>
        Win32PrintDevice.Create(null, PdfPrinter) ?? throw new InvalidOperationException("Microsoft Print to PDF is not installed.");

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_job_on_microsoft_print_to_pdf_writes_one_pdf_page_per_page()
    {
        var output = Path.Combine(_dir, "job.pdf");
        using (var device = OpenPdf())
        {
            var metrics = device.Metrics;
            metrics.LogPixelsY.Should().BeGreaterThan(0);
            metrics.PhysicalWidth.Should().BeGreaterThanOrEqualTo(metrics.HorzRes);
            var font = device.SelectFont(FontSpec.Default, PrintLayoutCalculator.FontHeight(100, metrics.LogPixelsY));
            font.LineHeight.Should().BeGreaterThan(0);
            var layout = PrintLayoutCalculator.Compute(
                metrics, PrintLayoutCalculator.MarginsToDevice(Core.Settings.PageMargins.UsDefault, true, metrics), font.LineHeight, true, true);
            var text = string.Join("\r\n", Enumerable.Range(1, layout.LinesPerPage + 5).Select(i => $"line {i}")).AsSpan();

            device.StartDocument("job", output).Should().Be(0);
            var pages = 0;
            while (text.Length > 0)
            {
                device.StartPage().Should().Be(0);
                device.DrawLine("header", layout.ColumnX(HeaderFooterColumn.Center, device.MeasureWidth("header")), layout.HeaderY);
                var drawn = device.DrawBody(text, layout.Body, rightToLeft: false);
                drawn.Should().BePositive();
                text = text[drawn..];
                device.EndPage().Should().Be(0);
                pages++;
            }

            device.EndDocument().Should().Be(0);
            pages.Should().Be(2);
        }

        var bytes = File.ReadAllBytes(output);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        PageObjects().Count(Encoding.Latin1.GetString(bytes)).Should().Be(2);
    }

    [GeneratedRegex(@"/Type\s*/Page(?!s)")]
    private static partial Regex PageObjects();

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_raster_font_is_replaced_by_the_drivers_default_face()
    {
        using var device = OpenPdf();
        var font = device.SelectFont(new FontSpec("Terminal", 100), PrintLayoutCalculator.FontHeight(100, device.Metrics.LogPixelsY));
        font.LineHeight.Should().BeGreaterThan(0);
        device.MeasureWidth("abc").Should().BePositive();
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Tabs_are_eight_average_characters_and_tab_length_is_ignored_without_dt_tabstop()
    {
        using var device = OpenPdf();
        device.SelectFont(FontSpec.Default, PrintLayoutCalculator.FontHeight(100, device.Metrics.LogPixelsY));
        var dc = DcOf(device);

        int Width(string text, uint extra, int tabLength)
        {
            var rect = new User32.RECT { Right = 100_000, Bottom = 100_000 };
            var parameters = new User32.DRAWTEXTPARAMS { cbSize = (uint)sizeof(User32.DRAWTEXTPARAMS), iTabLength = tabLength };
            fixed (char* p = text)
            {
                _ = User32.DrawTextEx(dc, p, text.Length, &rect, Win32PrintDevice.BodyFormat | DT_CALCRECT | extra, &parameters);
            }

            return rect.Right;
        }

        var x = Width("X", 0, 8);
        var original = Width("\tX", 0, device.AverageCharWidth * 8);
        Width("\tX", 0, Win32PrintDevice.TabLength).Should().Be(original);
        original.Should().Be((device.AverageCharWidth * 8) + x);
        Width("abcdefgh\tX", 0, 8).Should().Be((device.AverageCharWidth * 16) + x, "Lucida Console is fixed pitch");
        Width("\tX", DT_TABSTOP, 2).Should().Be((device.AverageCharWidth * 2) + x, "with DT_TABSTOP the length counts");
    }

    private const uint DT_CALCRECT = 0x400;
    private const uint DT_TABSTOP = 0x80;

    private static nint DcOf(Win32PrintDevice device) => device.DeviceContext;

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Named_printers_open_and_unknown_names_fail_silently()
    {
        using var service = new Win32PrinterService(() => 0, ResxStrings.Instance, new RecordingShell(), null);

        var opened = service.Open(PrinterChoice.Named, PdfPrinter);
        using (opened.Device)
        {
            opened.Status.Should().Be(PrinterOpenStatus.Opened);
            opened.Device!.Metrics.LogPixelsX.Should().BePositive();
        }

        service.Open(PrinterChoice.Named, "No Such Printer " + Guid.NewGuid()).Should().Be(new PrinterOpenResult(PrinterOpenStatus.Silent, null));
    }

    // ---- "Now Printing" ----

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Progress_template_follows_dialog_11()
    {
        var bytes = Win32PrintProgress.BuildTemplate(ResxStrings.Instance);

        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)).Should().Be(0x90C800C0);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16)).Should().Be(4);
        Encoding.Unicode.GetString(bytes).Should().Contain("Now Printing").And.Contain("Cancel").And.Contain("Notepad Classic");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Progress_box_disables_the_owner_shows_name_and_page_and_any_command_cancels() => StaThread.Run(() =>
    {
        var owner = Native.CreateOwner();
        try
        {
            using var progress = new Win32PrintProgress(owner, ResxStrings.Instance, "notes.txt", clientTopOffset: 0);
            var dialog = progress.Handle;
            dialog.Should().NotBe(0);
            User32.IsWindowEnabled(owner).Should().BeFalse();
            Native.Text(Native.GetDlgItem(dialog, Win32PrintProgress.FileNameId)).Should().Be("notes.txt");
            progress.ShowPage(3);
            Native.Text(Native.GetDlgItem(dialog, Win32PrintProgress.PageId)).Should().Be("Page 3");
            Native.Text(dialog).Should().Be("Notepad Classic");

            Native.SendMessage(dialog, User32.WM_INITMENU, User32.GetSystemMenu(dialog, false), 0);
            (User32.GetMenuState(User32.GetSystemMenu(dialog, false), User32.SC_CLOSE, User32.MF_BYCOMMAND) & User32.MF_GRAYED).Should().NotBe(0);

            progress.Cancelled.Should().BeFalse();
            Native.SendMessage(dialog, Native.WM_COMMAND, User32.IDCANCEL, 0);
            progress.Cancelled.Should().BeTrue();
            progress.Handle.Should().Be(0);
            Native.IsWindow(dialog).Should().BeFalse();
            User32.IsWindowEnabled(owner).Should().BeTrue();
            progress.ShowPage(4);
        }
        finally
        {
            Native.DestroyOwner(owner);
        }
    });

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Abort_proc_pumps_messages_and_stops_once_cancelled() => StaThread.Run(() =>
    {
        var owner = Native.CreateOwner();
        try
        {
            using var device = OpenPdf();
            using var progress = new Win32PrintProgress(owner, ResxStrings.Instance, "x", 0);
            device.SetAbortProc(progress).Should().Be(0);
            var dialog = progress.Handle;

            Native.PostMessage(dialog, Native.WM_COMMAND, User32.IDCANCEL, 0);
            CallAbortProc().Should().Be(0, "the posted Cancel is dispatched by the abort proc, which then stops the job");
            progress.Cancelled.Should().BeTrue();
            CallAbortProc().Should().Be(0);
        }
        finally
        {
            Native.DestroyOwner(owner);
        }

        static int CallAbortProc()
        {
            delegate* unmanaged<nint, int, int> proc = &PrintAbort.Proc;
            return proc(0, 0);
        }
    });

    // ---- Page Setup ----

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_setup_template_uses_the_standard_ids_and_adds_header_footer_and_link()
    {
        var withLink = Win32PageSetupDialog.BuildTemplate(ResxStrings.Instance, includeHelpLink: true);
        var withoutLink = Win32PageSetupDialog.BuildTemplate(ResxStrings.Instance, includeHelpLink: false);

        BinaryPrimitives.ReadUInt32LittleEndian(withLink.AsSpan(12)).Should().Be(0x90C800C4);
        BinaryPrimitives.ReadUInt16LittleEndian(withLink.AsSpan(16)).Should().Be(29);
        BinaryPrimitives.ReadUInt16LittleEndian(withoutLink.AsSpan(16)).Should().Be(28);
        Encoding.Unicode.GetString(withLink).Should().Contain("SysLink").And.Contain("<A>Input Values</A>").And.Contain("&Header:");
        Encoding.Unicode.GetString(withoutLink).Should().NotContain("SysLink");
        Win32PageSetupDialog.Flags(inches: true).Should().Be(0x22006u);
        Win32PageSetupDialog.Flags(inches: false).Should().Be(0x2200Au);
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_setup_shows_header_and_footer_limits_them_and_returns_the_edited_values() => StaThread.Run(() =>
    {
        var shell = new RecordingShell();
        using var service = new Win32PrinterService(() => 0, ResxStrings.Instance, shell, "https://example.invalid/codes");
        var observed = new Dictionary<string, string>();
        var driver = DialogDriver.Run("Page Setup", (_, dialog) =>
        {
            var header = Native.GetDlgItem(dialog, Win32PageSetupDialog.HeaderId);
            var footer = Native.GetDlgItem(dialog, Win32PageSetupDialog.FooterId);
            observed["header"] = Native.Text(header);
            observed["footer"] = Native.Text(footer);
            observed["limit"] = Native.SendMessage(header, EM_GETLIMITTEXT, 0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Native.SendMessage(header, Native.EM_GETSEL, out var start, out var end);
            observed["selection"] = $"{start},{end}";
            observed["margins"] = Native.Text(Native.GetDlgItem(dialog, 0x433));

            var notify = new User32.NMHDR { hwndFrom = Native.GetDlgItem(dialog, Win32PageSetupDialog.HelpLinkId), idFrom = Win32PageSetupDialog.HelpLinkId, code = User32.NM_CLICK };
            Native.SendMessage(dialog, User32.WM_NOTIFY, Win32PageSetupDialog.HelpLinkId, (nint)(&notify));

            Native.SendMessage(header, Native.WM_SETTEXT, 0, "&l&f");
            Native.SendMessage(footer, Native.WM_SETTEXT, 0, "");
            Native.PostMessage(dialog, Native.WM_COMMAND, User32.IDOK, 0);
        });

        var result = service.ShowPageSetup(new("&f", "Page &p", Core.Settings.PageMargins.UsDefault, true));
        driver.Join();

        observed["header"].Should().Be("&f");
        observed["footer"].Should().Be("Page &p");
        observed["limit"].Should().Be("39");
        observed["selection"].Should().Be("0,2");
        observed["margins"].Should().Be("Margins (inches)");
        shell.Opened.Should().Equal("https://example.invalid/codes");
        result.Accepted.Should().BeTrue();
        result.Values.Header.Should().Be("&l&f");
        result.Values.Footer.Should().BeEmpty();
        result.Values.Margins.Should().Be(Core.Settings.PageMargins.UsDefault);
    });

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_setup_cancel_returns_no_error_and_metric_margins_are_shown_in_millimeters() => StaThread.Run(() =>
    {
        using var service = new Win32PrinterService(() => 0, ResxStrings.Instance, new RecordingShell(), null);
        var margins = "";
        var hasLink = true;
        var driver = DialogDriver.Run("Page Setup", (_, dialog) =>
        {
            margins = Native.Text(Native.GetDlgItem(dialog, 0x433));
            hasLink = Native.GetDlgItem(dialog, Win32PageSetupDialog.HelpLinkId) != 0;
            Native.PostMessage(dialog, Native.WM_COMMAND, User32.IDCANCEL, 0);
        });

        var result = service.ShowPageSetup(new("h", "f", Core.Settings.PageMargins.MetricDefault, false));
        driver.Join();

        result.Should().Be(new PageSetupResult(false, new("h", "f", Core.Settings.PageMargins.MetricDefault, false), 0));
        margins.Should().Be("Margins (millimeters)");
        hasLink.Should().BeFalse();
    });

    private const int EM_GETLIMITTEXT = 0x00D5;

    private sealed class RecordingShell : IShell
    {
        public List<string> Opened { get; } = [];

        public void OpenUri(string uri) => Opened.Add(uri);

        public void StartNewWindow()
        {
        }

        public void StartNextNoteWindow()
        {
        }

        public void AddToRecentDocuments(string path)
        {
        }

        public bool ShowInFolder(string path) => true;
    }
}
