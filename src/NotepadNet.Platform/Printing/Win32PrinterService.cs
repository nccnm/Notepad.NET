using System.Runtime.InteropServices;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Printing;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Printing;

/// <summary>
/// Printer dialogs and DCs: <c>PrintDlgExW</c>, the default printer for
/// <c>/P</c>, a named printer for <c>/PT</c>, and <c>PageSetupDlgW</c> with the Header/Footer hook.
/// The <c>DEVMODE</c>/<c>DEVNAMES</c> chosen in Page Setup or Print are kept for the next call.
/// </summary>
public sealed unsafe class Win32PrinterService(
    Func<nint> owner, ILocalizedStrings strings, IShell shell, string? pageSetupHelpUrl, Func<int>? clientTopOffset = null) : IPrinterService, IDisposable
{
    /// <summary><c>PD_NOCURRENTPAGE | PD_USEDEVMODECOPIESANDCOLLATE | PD_RETURNDC | PD_NOPAGENUMS |
    /// PD_NOSELECTION</c> (0x84010C): the whole document, copies left to the driver.</summary>
    public const uint PrintDialogFlags = Comdlg32.PD_NOCURRENTPAGE | Comdlg32.PD_USEDEVMODECOPIESANDCOLLATE | Comdlg32.PD_RETURNDC
        | Comdlg32.PD_NOPAGENUMS | Comdlg32.PD_NOSELECTION;

    private readonly Func<int> _clientTopOffset = clientTopOffset ?? (() => 0);
    private nint _devMode;
    private nint _devNames;

    public PrinterOpenResult Open(PrinterChoice choice, string? printerName) => choice switch
    {
        PrinterChoice.Dialog => FromDialog(),
        PrinterChoice.Named => Named(printerName ?? ""),
        _ => Default(),
    };

    /// <summary>
    /// File > Print runs in <c>DPI_AWARENESS_CONTEXT_SYSTEM_AWARE</c> like the original's menu
    /// handler; the context is restored when the job's device is disposed.
    /// </summary>
    private PrinterOpenResult FromDialog()
    {
        var previous = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE);
        var dialog = new Comdlg32.PRINTDLGEXW
        {
            lStructSize = (uint)sizeof(Comdlg32.PRINTDLGEXW),
            hwndOwner = owner(),
            hDevMode = _devMode,
            hDevNames = _devNames,
            Flags = PrintDialogFlags,
            nStartPage = Comdlg32.START_PAGE_GENERAL,
        };
        var hr = Comdlg32.PrintDlgEx(&dialog);
        if (hr >= 0)
        {
            Keep(dialog.hDevMode, dialog.hDevNames);
        }

        if (hr >= 0 && dialog.dwResultAction == Comdlg32.PD_RESULT_PRINT && dialog.hDC != 0)
        {
            return new(PrinterOpenStatus.Opened, new ScopedDevice(dialog.hDC, previous));
        }

        if (dialog.hDC != 0)
        {
            _ = Gdi32.DeleteDC(dialog.hDC);
        }

        _ = User32.SetThreadDpiAwarenessContext(previous);
        return new(PrinterOpenStatus.Silent, null);
    }

    /// <summary><c>GetDefaultPrinterDc</c>: the printer chosen so far, else the default printer
    /// (<c>PSD_RETURNDEFAULT</c>), with its <c>DEVMODE</c>.</summary>
    private PrinterOpenResult Default()
    {
        if (_devNames == 0)
        {
            var defaults = new Comdlg32.PAGESETUPDLGW
            {
                lStructSize = (uint)sizeof(Comdlg32.PAGESETUPDLGW),
                hwndOwner = owner(),
                Flags = Comdlg32.PSD_RETURNDEFAULT,
            };
            bool found;
            using (SystemAware())
            {
                found = Comdlg32.PageSetupDlg(&defaults);
            }

            if (found)
            {
                Keep(defaults.hDevMode, defaults.hDevNames);
            }
        }

        if (_devNames == 0)
        {
            return new(PrinterOpenStatus.CannotAccess, null);
        }

        var names = (Comdlg32.DEVNAMES*)Kernel32Ui.GlobalLock(_devNames);
        var devMode = _devMode == 0 ? 0 : Kernel32Ui.GlobalLock(_devMode);
        try
        {
            var driver = new string((char*)names + names->wDriverOffset);
            var device = new string((char*)names + names->wDeviceOffset);
            var created = Win32PrintDevice.Create(driver, device, devMode);
            return created is null ? new(PrinterOpenStatus.CannotAccess, null) : new(PrinterOpenStatus.Opened, created);
        }
        finally
        {
            if (devMode != 0)
            {
                _ = Kernel32Ui.GlobalUnlock(_devMode);
            }

            _ = Kernel32Ui.GlobalUnlock(_devNames);
        }
    }

    /// <summary>
    /// <c>GetNamedPrinterDc</c>: the printer's driver from <c>GetPrinterDriverW</c> level 1, then
    /// <c>CreateDCW</c> without a <c>DEVMODE</c>. Only a failed <c>CreateDCW</c> is reported.
    /// </summary>
    private static PrinterOpenResult Named(string printerName)
    {
        if (!WinSpool.OpenPrinter(printerName, out var printer, 0))
        {
            return new(PrinterOpenStatus.Silent, null);
        }

        string? driver;
        try
        {
            driver = DriverName(printer);
        }
        finally
        {
            _ = WinSpool.ClosePrinter(printer);
        }

        if (driver is null)
        {
            return new(PrinterOpenStatus.Silent, null);
        }

        var created = Win32PrintDevice.Create(driver, printerName);
        return created is null ? new(PrinterOpenStatus.CannotAccess, null) : new(PrinterOpenStatus.Opened, created);
    }

    private static string? DriverName(nint printer)
    {
        _ = WinSpool.GetPrinterDriver(printer, null, 1, null, 0, out var needed);
        if (needed == 0)
        {
            return null;
        }

        var buffer = new byte[needed];
        fixed (byte* p = buffer)
        {
            return WinSpool.GetPrinterDriver(printer, null, 1, p, needed, out _)
                ? Marshal.PtrToStringUni(((WinSpool.DRIVER_INFO_1W*)p)->pName)
                : null;
        }
    }

    public IPrintProgress ShowProgress(string fileLabel) => new Win32PrintProgress(owner(), strings, fileLabel, _clientTopOffset());

    public PageSetupResult ShowPageSetup(PageSetupValues values)
    {
        // The link needs the SysLink class of comctl32 version 6; without it the dialog would fail.
        var hasLink = !string.IsNullOrEmpty(pageSetupHelpUrl)
            && Comctl32.InitCommonControlsEx(new() { dwSize = (uint)sizeof(Comctl32.INITCOMMONCONTROLSEX), dwICC = Comctl32.ICC_LINK_CLASS });

        var template = Win32PageSetupDialog.BuildTemplate(strings, hasLink);
        // Fixed memory: comdlg32 uses the template handle as a pointer, as it would a resource's.
        var memory = Kernel32Ui.GlobalAlloc(Kernel32Ui.GMEM_FIXED, (nuint)template.Length);
        if (memory == 0)
        {
            return new(false, values, CommonDialogErrors.MemoryAllocationFailure);
        }

        try
        {
            Marshal.Copy(template, 0, memory, template.Length);
            var dialog = new Comdlg32.PAGESETUPDLGW
            {
                lStructSize = (uint)sizeof(Comdlg32.PAGESETUPDLGW),
                hwndOwner = owner(),
                hDevMode = _devMode,
                hDevNames = _devNames,
                Flags = Win32PageSetupDialog.Flags(values.MarginsInInches),
                rtMargin = new() { Left = values.Margins.Left, Top = values.Margins.Top, Right = values.Margins.Right, Bottom = values.Margins.Bottom },
                hInstance = Kernel32Ui.GetModuleHandle(null),
                lpfnPageSetupHook = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Win32PageSetupDialog.Hook,
                hPageSetupTemplate = memory,
            };
            var state = new Win32PageSetupDialog.State(values.Header, values.Footer, pageSetupHelpUrl, shell, _clientTopOffset());
            bool accepted;
            using (SystemAware())
            using (Win32PageSetupDialog.Enter(state))
            {
                accepted = Comdlg32.PageSetupDlg(&dialog);
            }

            var error = accepted ? 0 : Comdlg32.CommDlgExtendedError();
            Keep(dialog.hDevMode, dialog.hDevNames);
            if (!accepted)
            {
                return new(false, values, error);
            }

            var margins = new PageMargins(dialog.rtMargin.Left, dialog.rtMargin.Top, dialog.rtMargin.Right, dialog.rtMargin.Bottom);
            return new(true, values with { Header = state.HeaderResult ?? values.Header, Footer = state.FooterResult ?? values.Footer, Margins = margins }, 0);
        }
        finally
        {
            _ = Kernel32Ui.GlobalFree(memory);
        }
    }

    public void ResetPrinterSelection() => Keep(0, 0);

    /// <summary>Keeps the handles a dialog returned, freeing the ones it replaced.</summary>
    private void Keep(nint devMode, nint devNames)
    {
        if (_devMode != 0 && _devMode != devMode)
        {
            _ = Kernel32Ui.GlobalFree(_devMode);
        }

        if (_devNames != 0 && _devNames != devNames)
        {
            _ = Kernel32Ui.GlobalFree(_devNames);
        }

        _devMode = devMode;
        _devNames = devNames;
    }

    /// <summary><c>PageSetupDlgSystemAware</c>: the thread runs system-DPI
    /// aware until the scope ends.</summary>
    private static DpiScope SystemAware() => new(User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE));

    private readonly struct DpiScope(nint previous) : IDisposable
    {
        public void Dispose() => _ = User32.SetThreadDpiAwarenessContext(previous);
    }

    public void Dispose() => Keep(0, 0);

    /// <summary>A device that restores the thread's DPI context once the job is over.</summary>
    private sealed class ScopedDevice(nint dc, nint previousContext) : IPrintDevice
    {
        private readonly Win32PrintDevice _device = new(dc);

        public PrinterMetrics Metrics => _device.Metrics;

        public PrintFontMetrics SelectFont(FontSpec font, int height) => _device.SelectFont(font, height);

        public int MeasureWidth(string text) => _device.MeasureWidth(text);

        public void DrawLine(string text, int x, int y) => _device.DrawLine(text, x, y);

        public int DrawBody(ReadOnlySpan<char> text, DeviceRect bounds, bool rightToLeft) => _device.DrawBody(text, bounds, rightToLeft);

        public int SetAbortProc(IPrintProgress progress) => _device.SetAbortProc(progress);

        public int StartDocument(string name) => _device.StartDocument(name);

        public int StartPage() => _device.StartPage();

        public int EndPage() => _device.EndPage();

        public int EndDocument() => _device.EndDocument();

        public void AbortDocument() => _device.AbortDocument();

        public void Dispose()
        {
            _device.Dispose();
            _ = User32.SetThreadDpiAwarenessContext(previousContext);
        }
    }
}
