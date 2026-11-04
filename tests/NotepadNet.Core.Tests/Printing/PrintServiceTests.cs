using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Printing;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Printing;

/// <summary>
/// Print (<c>PrintCommand</c>, <c>PrintDocument</c>) and Page Setup, against a fake printer whose characters are 10 pixels wide and lines 100 high.
/// </summary>
public sealed class PrintServiceTests
{
    private static Harness Create(string text, FakePrintDevice? device = null)
    {
        var h = new Harness(text);
        h.Documents.UpdateTitle();
        if (device is not null)
        {
            h.Printers.Device = device;
        }

        return h;
    }

    private static string Lines(int count) => string.Join("\r\n", Enumerable.Range(1, count).Select(i => $"line {i}"));

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_job_prints_every_page_with_header_and_footer_and_closes_everything()
    {
        var h = Create(Lines(100));
        h.Settings.Font = new FontSpec("Consolas", 110, Weight: 700);

        h.Printing.Print(PrinterChoice.Dialog);

        var device = h.Printers.Device;
        h.Printers.Opens.Should().Equal((PrinterChoice.Dialog, null));
        device.Font!.Value.Font.Should().Be(h.Settings.Font);
        device.Font!.Value.Height.Should().Be(-91);
        device.Log[0].Should().Be("abortproc");
        device.Log[1].Should().Be("startdoc Untitled - Notepad.NET");
        device.Log.Should().EndWith("enddoc");
        device.Disposed.Should().BeTrue();
        h.Printers.Progress!.FileLabel.Should().Be("Untitled");
        h.Printers.Progress.Disposed.Should().BeTrue();

        // Letter at 600 DPI, 0.25 in border: margins 300/450 px from the printable area, 100 px lines.
        // Body: 6300 - 450 - 450 = 5400 px, minus header and footer = 52 lines per page.
        device.Bodies.Select(b => b.Bounds).Distinct().Should().Equal(new DeviceRect(300, 550, 4500, 5750));
        device.Bodies.Count.Should().Be(2);
        h.Printers.Progress.Pages.Should().Equal(1, 2);
        string.Concat(device.Bodies.Select(b => b.Text)).Should().Be(Lines(100));
        device.Lines.Should().Equal(
            ("Untitled", ((4500 + 300) / 2) - 40, 450), ("Page 1", ((4500 + 300) / 2) - 30, 6300 - 100 - 450),
            ("Untitled", ((4500 + 300) / 2) - 40, 450), ("Page 2", ((4500 + 300) / 2) - 30, 6300 - 100 - 450));
        h.Dialogs.Messages.Should().BeEmpty();
        h.Window.WaitCursorsShowing.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void The_header_is_drawn_after_start_page()
    {
        var h = Create("text");

        h.Printing.Print(PrinterChoice.Default);

        h.Printers.Device.Log.Should().ContainInOrder("startpage 1", "line Untitled@2360,450", "body 4", "line Page 1@2370,5750", "endpage 1");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void The_progress_box_shows_the_name_after_the_last_separator_and_the_queue_gets_the_window_title()
    {
        var h = Create("");
        h.Files.Files[@"C:\docs\notes.txt"] = "x"u8.ToArray();
        h.Documents.Load(@"C:\docs\notes.txt", TextEncodingKind.Detect);
        h.Settings.Header = "&l&f";

        h.Printing.Print(PrinterChoice.Named, "PDF");

        h.Printers.Opens.Should().Equal((PrinterChoice.Named, "PDF"));
        h.Printers.Progress!.FileLabel.Should().Be("notes.txt");
        h.Printers.Device.Log.Should().Contain("startdoc notes.txt - Notepad.NET");
        h.Printers.Device.Lines[0].Should().Be(("notes.txt", 300, 450));
    }

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData(@"C:\a\b.txt", "b.txt")]
    [InlineData(@"C:b.txt", "b.txt")]
    [InlineData(@"\\server\share\c.txt", "c.txt")]
    [InlineData(@"\top", @"\top")]
    [InlineData("plain", "plain")]
    [InlineData("C:", "")]
    public void Progress_label_rules(string path, string label) => PrintService.PrintFileLabel(path).Should().Be(label);

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Empty_header_and_footer_give_the_body_the_whole_area()
    {
        var h = Create("a\tb");
        h.Settings.Header = "";
        h.Settings.Trailer = "";
        h.Editor.RightToLeftReading = true;

        h.Printing.Print(PrinterChoice.Dialog);

        h.Printers.Device.Lines.Should().BeEmpty();
        h.Printers.Device.Bodies.Should().ContainSingle().Which.Should().Be(("a\tb", new DeviceRect(300, 450, 4500, 5850), true));
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void An_empty_document_starts_and_ends_a_job_without_pages()
    {
        var h = Create("");

        h.Printing.Print(PrinterChoice.Dialog);

        h.Printers.Device.Log.Should().Equal("abortproc", "startdoc Untitled - Notepad.NET", "enddoc");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Header_and_footer_values_use_the_page_and_the_start_time()
    {
        var h = Create(Lines(60));
        h.Settings.Header = "&l&d&r&t";
        h.Settings.Trailer = "&p+10 of &f";

        h.Printing.Print(PrinterChoice.Dialog);

        h.Printers.Device.Lines.Select(l => l.Text).Should().Equal(
            "Thursday, October 29, 2026", "2:44:00 PM", "11 of Untitled", "Thursday, October 29, 2026", "2:44:00 PM", "12 of Untitled");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_page_too_small_for_one_line_is_reported_before_the_job_starts()
    {
        var h = Create("text", new FakePrintDevice { LineHeight = 3000 });

        h.Printing.Print(PrinterChoice.Dialog);

        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(ResxStrings.Instance.GetString(StringKeys.PageTooSmall));
        h.Printers.Device.Log.Should().BeEmpty();
        h.Printers.Progress.Should().BeNull();
        h.Printers.Device.Disposed.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_page_that_takes_no_character_ends_the_job_with_the_same_message()
    {
        var h = Create("text", new FakePrintDevice { DrawsNothing = true });

        h.Printing.Print(PrinterChoice.Dialog);

        h.Printers.Device.Log.Should().ContainInOrder("startpage 1", "endpage 1", "enddoc");
        h.Printers.Device.Log.Should().NotContain("startpage 2");
        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(ResxStrings.Instance.GetString(StringKeys.PageTooSmall));
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Cancel_aborts_the_document_silently()
    {
        var h = Create(Lines(200));
        h.Printers.Device = new FakePrintDevice { AfterBody = page => h.Printers.Progress!.Cancelled = page == 2 };

        h.Printing.Print(PrinterChoice.Dialog);

        h.Printers.Device.Log.Should().ContainInOrder("endpage 2", "abortdoc");
        h.Printers.Device.Log.Should().NotContain("startpage 3").And.NotContain("enddoc");
        h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Cancel_while_the_document_starts_is_silent()
    {
        var h = Create("text");
        h.SystemMessages.Messages[1223] = "The operation was canceled by the user.";
        h.Printers.DeviceOverride = new CancellingDevice(h);

        h.Printing.Print(PrinterChoice.Dialog);

        h.Dialogs.Messages.Should().BeEmpty();
    }

    /// <summary>The user presses Cancel in the progress box while <c>StartDocW</c> waits.</summary>
    private sealed class CancellingDevice : FakePrintDeviceBase
    {
        public CancellingDevice(Harness h)
            : base(h)
        {
        }

        public override int StartDocument(string name)
        {
            Harness.Printers.Progress!.Cancelled = true;
            return 1223;
        }
    }

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData(1223, "The operation was canceled by the user.")]
    [InlineData(0x70, null)]
    public void A_failed_job_reports_the_system_message_or_string_12(int error, string? systemText)
    {
        var h = Create("text", new FakePrintDevice { StartDocumentError = error });
        if (systemText is not null)
        {
            h.SystemMessages.Messages[error] = systemText;
        }

        h.Printing.Print(PrinterChoice.Dialog);

        var expected = systemText ?? MessageText.InsertArgument(ResxStrings.Instance.GetString(StringKeys.CannotPrint), "Untitled");
        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(expected);
        h.Dialogs.Messages.Select(m => m.Kind).Should().Equal(MessageKind.Warning);
        h.Printers.Device.Log.Should().NotContain("enddoc", "the job never started");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void The_error_names_the_full_path_of_a_saved_document()
    {
        var h = Create("");
        h.Files.Files[@"C:\docs\notes.txt"] = "x"u8.ToArray();
        h.Documents.Load(@"C:\docs\notes.txt", TextEncodingKind.Detect);
        h.Printers.Device = new FakePrintDevice { StartPageFailure = (1, 0x70) };

        h.Printing.Print(PrinterChoice.Dialog);

        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(MessageText.InsertArgument(ResxStrings.Instance.GetString(StringKeys.CannotPrint), @"C:\docs\notes.txt"));
        h.Printers.Device.Log.Should().EndWith("enddoc", "a started document is still ended");
    }

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("abortproc")]
    [InlineData("endpage")]
    [InlineData("enddoc")]
    public void Errors_of_each_gdi_step_are_reported(string step)
    {
        var device = step switch
        {
            "abortproc" => new FakePrintDevice { SetAbortProcError = 5 },
            "endpage" => new FakePrintDevice { EndPageFailure = (1, 5) },
            _ => new FakePrintDevice { EndDocumentError = 5 },
        };
        var h = Create("text", device);
        h.SystemMessages.Messages[5] = "Access is denied.";

        h.Printing.Print(PrinterChoice.Dialog);

        h.Dialogs.Messages.Select(m => m.Text).Should().Equal("Access is denied.");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void A_printer_that_cannot_be_opened_shows_string_25_and_a_silent_failure_shows_nothing()
    {
        var h = Create("text");
        h.Printers.NextOpen = new(PrinterOpenStatus.CannotAccess, null);
        h.Printing.Print(PrinterChoice.Default);
        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(ResxStrings.Instance.GetString(StringKeys.CannotAccessPrinter));

        h.Printers.NextOpen = new(PrinterOpenStatus.Silent, null);
        h.Printing.Print(PrinterChoice.Dialog);
        h.Dialogs.Messages.Should().ContainSingle();
        h.Window.WaitCursors.Should().Be(2);
        h.Window.WaitCursorsShowing.Should().Be(0);
    }

    // ---- Page Setup ----

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_setup_ok_stores_header_footer_and_margins()
    {
        var h = Create("");
        h.Printers.PageSetupResults.Enqueue(new(true, new("&lA" + new string('x', 50), "", new(1, 2, 3, 4), true), 0));

        h.Printing.PageSetup();

        h.Printers.PageSetupCalls.Should().Equal(new PageSetupValues("&f", "Page &p", PageMargins.UsDefault, true));
        h.Settings.Header.Should().Be("&lA" + new string('x', 36), "the buffer holds 39 characters");
        h.Settings.Trailer.Should().BeEmpty();
        h.Settings.Margins.Should().Be(new PageMargins(1, 2, 3, 4));
        h.Settings.PageSetupChanged.Should().BeTrue();
    }

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData(CommonDialogErrors.DevNamesDevModeMismatch)]
    [InlineData(CommonDialogErrors.PrinterNotFound)]
    [InlineData(CommonDialogErrors.DefaultDifferent)]
    public void Page_setup_resets_the_printer_and_opens_again_after_a_printer_error(int error)
    {
        var h = Create("");
        h.Printers.PageSetupResults.Enqueue(new(false, default, error));
        h.Printers.PageSetupResults.Enqueue(new(false, default, 0));

        h.Printing.PageSetup();

        h.Printers.PageSetupCalls.Should().HaveCount(2);
        h.Printers.Resets.Should().Be(1);
        h.Dialogs.Messages.Should().BeEmpty();
        h.Settings.PageSetupChanged.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_setup_reports_other_errors_once()
    {
        var h = Create("");
        h.Printers.PageSetupResults.Enqueue(new(false, default, 0x1004));

        h.Printing.PageSetup();

        h.Dialogs.Messages.Select(m => m.Text).Should().Equal(ResxStrings.Instance.GetString(StringKeys.CannotAccessPrinter));
        h.Dialogs.Messages.Select(m => m.Kind).Should().Equal(MessageKind.SystemError);
    }

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData(0, null)]
    [InlineData(0x1008, null)]
    [InlineData(0xFFFF, StringKeys.NotEnoughMemory)]
    [InlineData(5, StringKeys.NotEnoughMemory)]
    [InlineData(6, StringKeys.NotEnoughMemory)]
    [InlineData(0x1005, StringKeys.NotEnoughMemory)]
    [InlineData(2, StringKeys.FileDialogsFailed)]
    [InlineData(0xA, StringKeys.PrintDialogsFailed)]
    [InlineData(0x1004, StringKeys.CannotAccessPrinter)]
    public void Common_dialog_error_table(int error, string? key) =>
        CommonDialogErrors.MessageFor(error, ResxStrings.Instance).Should().Be(key is null ? null : ResxStrings.Instance.GetString(key));

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Other_common_dialog_errors_show_their_code() =>
        CommonDialogErrors.MessageFor(CommonDialogErrors.MemoryAllocationFailure, ResxStrings.Instance).Should().Be("Common Dialog error (0x0009)");
}

/// <summary>A <see cref="FakePrintDevice"/> whose steps a test can override.</summary>
internal abstract class FakePrintDeviceBase(Harness harness) : IPrintDevice
{
    private readonly FakePrintDevice _inner = new();

    protected Harness Harness { get; } = harness;

    public PrinterMetrics Metrics => _inner.Metrics;

    public PrintFontMetrics SelectFont(FontSpec font, int height) => _inner.SelectFont(font, height);

    public int MeasureWidth(string text) => _inner.MeasureWidth(text);

    public void DrawLine(string text, int x, int y) => _inner.DrawLine(text, x, y);

    public int DrawBody(ReadOnlySpan<char> text, DeviceRect bounds, bool rightToLeft) => _inner.DrawBody(text, bounds, rightToLeft);

    public int SetAbortProc(IPrintProgress progress) => _inner.SetAbortProc(progress);

    public virtual int StartDocument(string name) => _inner.StartDocument(name);

    public int StartPage() => _inner.StartPage();

    public int EndPage() => _inner.EndPage();

    public int EndDocument() => _inner.EndDocument();

    public void AbortDocument() => _inner.AbortDocument();

    public void Dispose() => _inner.Dispose();
}
