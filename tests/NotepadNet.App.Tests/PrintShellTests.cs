using Microsoft.Extensions.DependencyInjection;
using NotepadNet.App.Interop;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Printing;

namespace NotepadNet.App.Tests;

/// <summary>
/// Phase e in the real shell: File > Page Setup and File > Print reach the printer service, and
/// <c>/P</c>, <c>/PT</c> print the file at startup and then close the window. The printer itself
/// is a fake here; Platform tests print to "Microsoft Print to PDF".
/// </summary>
public sealed class PrintShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-print-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class RecordingPrinters : IPrinterService
    {
        public List<(PrinterChoice Choice, string? Name)> Opens { get; } = [];

        public List<PageSetupValues> PageSetups { get; } = [];

        public PrinterOpenResult Open(PrinterChoice choice, string? printerName)
        {
            Opens.Add((choice, printerName));
            return new(PrinterOpenStatus.Silent, null);
        }

        public IPrintProgress ShowProgress(string fileLabel) => throw new InvalidOperationException();

        public PageSetupResult ShowPageSetup(PageSetupValues values)
        {
            PageSetups.Add(values);
            return new(true, values with { Header = "&lH", Footer = "" }, 0);
        }

        public void ResetPrinterSelection()
        {
        }
    }

    private static TestApp Create(RecordingPrinters printers) =>
        new(configure: services => services.AddSingleton<IPrinterService>(printers));

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Menu_print_opens_the_print_dialog_and_page_setup_stores_the_result() => Sta.Run(() =>
    {
        var printers = new RecordingPrinters();
        using var app = Create(printers);
        var context = app.Host.Context;

        app.Host.Registry.Find(CommandId.Print)!.CanExecute(context).Should().BeTrue();
        app.Host.Registry.Find(CommandId.Print)!.Execute(context);
        app.Host.Registry.Find(CommandId.PageSetup)!.Execute(context);

        printers.Opens.Should().Equal((PrinterChoice.Dialog, null));
        printers.PageSetups.Should().ContainSingle().Which.Header.Should().Be("&f");
        app.Host.Settings.Header.Should().Be("&lH");
        app.Host.Settings.Trailer.Should().BeEmpty();
        app.Host.Settings.PageSetupChanged.Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Print_to_a_named_printer_loads_the_file_prints_and_closes() => Sta.Run(() =>
    {
        var printers = new RecordingPrinters();
        using var app = Create(printers);
        var closes = 0;
        app.Host.Window.CloseQuery = () =>
        {
            closes++;
            return false;
        };
        var path = Path.Combine(_dir, "a.txt");
        File.WriteAllText(path, "abc");

        app.Host.RunCommandLine($"/PT \"{path}\" \"Some Printer\"", untrustedSource: false);
        Application.DoEvents();

        app.Text.Should().Be("abc");
        printers.Opens.Should().Equal((PrinterChoice.Named, "Some Printer"));
        closes.Should().Be(1);
    });

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Print_uses_the_default_printer() => Sta.Run(() =>
    {
        var printers = new RecordingPrinters();
        using var app = Create(printers);
        app.Host.Window.CloseQuery = () => false;
        var path = Path.Combine(_dir, "b.txt");
        File.WriteAllText(path, "b");

        app.Host.RunCommandLine($"/P {path}", untrustedSource: false);

        printers.Opens.Should().Equal((PrinterChoice.Default, null));
    });

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void The_editor_reports_right_to_left_reading() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var edit = app.Editor.Host.Handle;
        app.Editor.RightToLeftReading.Should().BeFalse();

        var exStyle = NativeMethods.GetWindowLongPtr(edit, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLongPtr(edit, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_RTLREADING);

        app.Editor.RightToLeftReading.Should().BeTrue();
        app.Host.Window.Title.Should().Be("Untitled - Notepad.NET");
    });
}
