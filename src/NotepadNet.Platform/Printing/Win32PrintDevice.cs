using System.Runtime.InteropServices;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Printing;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Printing;

/// <summary>
/// A printer DC (<c>PrintDocument</c>). Owns the DC and the print font; both are
/// released on dispose, and a document still open then is aborted.
/// </summary>
public sealed unsafe class Win32PrintDevice : IPrintDevice
{
    /// <summary><c>DT_WORDBREAK | DT_EXPANDTABS | DT_NOPREFIX | DT_EDITCONTROL</c> (0x2850).</summary>
    public const uint BodyFormat = User32.DT_WORDBREAK | User32.DT_EXPANDTABS | User32.DT_NOPREFIX | User32.DT_EDITCONTROL;

    /// <summary>
    /// <c>DRAWTEXTPARAMS.iTabLength</c>. The original passes <c>tmAveCharWidth * 8</c>, but
    /// <c>DrawTextExW</c> reads it only with <c>DT_TABSTOP</c>, which is not set: tabs are 8 average
    /// characters either way (seen in its PDF output, see parity.md). The port passes 8.
    /// </summary>
    public const int TabLength = 8;

    private nint _dc;
    private nint _font;
    private nint _oldFont;
    private bool _documentOpen;

    /// <summary>Takes ownership of <paramref name="dc"/>.</summary>
    public Win32PrintDevice(nint dc)
    {
        if (dc == 0)
        {
            throw new ArgumentException("No printer DC.", nameof(dc));
        }

        _dc = dc;
        Metrics = new(
            Gdi32.GetDeviceCaps(dc, Gdi32.HORZRES),
            Gdi32.GetDeviceCaps(dc, Gdi32.VERTRES),
            Gdi32.GetDeviceCaps(dc, Gdi32.LOGPIXELSX),
            Gdi32.GetDeviceCaps(dc, Gdi32.LOGPIXELSY),
            Gdi32.GetDeviceCaps(dc, Gdi32.PHYSICALWIDTH),
            Gdi32.GetDeviceCaps(dc, Gdi32.PHYSICALHEIGHT),
            Gdi32.GetDeviceCaps(dc, Gdi32.PHYSICALOFFSETX),
            Gdi32.GetDeviceCaps(dc, Gdi32.PHYSICALOFFSETY));
    }

    /// <summary>Creates a DC for a printer by driver and device name (<c>CreateDCW</c>, no
    /// <c>DEVMODE</c>: the driver's defaults). Null when it fails.</summary>
    public static Win32PrintDevice? Create(string? driver, string device, nint devMode = 0)
    {
        var dc = Gdi32.CreateDC(driver, device, 0, devMode);
        return dc == 0 ? null : new Win32PrintDevice(dc);
    }

    public PrinterMetrics Metrics { get; }

    /// <summary>The printer DC (tests measure with it).</summary>
    internal nint DeviceContext => _dc;

    /// <summary>Average character width of the selected font (tests compare tab stops with it).</summary>
    public int AverageCharWidth { get; private set; }

    public PrintFontMetrics SelectFont(FontSpec font, int height)
    {
        ArgumentNullException.ThrowIfNull(font);
        var logFont = ToLogFont(font, height);
        Replace(Gdi32.CreateFontIndirect(&logFont));
        _ = Gdi32.SetBkMode(_dc, Gdi32.TRANSPARENT);
        _ = Gdi32.GetTextMetrics(_dc, out var metrics);
        if ((metrics.tmPitchAndFamily & (Gdi32.TMPF_VECTOR | Gdi32.TMPF_TRUETYPE)) == 0)
        {
            // A raster font cannot print: the driver's default face instead.
            logFont.lfFaceName[0] = '\0';
            Replace(Gdi32.CreateFontIndirect(&logFont));
            _ = Gdi32.GetTextMetrics(_dc, out metrics);
        }

        AverageCharWidth = metrics.tmAveCharWidth;
        return new(metrics.tmHeight + metrics.tmExternalLeading);
    }

    private static Comdlg32.LOGFONTW ToLogFont(FontSpec font, int height)
    {
        var logFont = new Comdlg32.LOGFONTW
        {
            lfHeight = height,
            lfEscapement = font.Escapement,
            lfOrientation = font.Orientation,
            lfWeight = font.Weight,
            lfItalic = font.Italic ? (byte)1 : (byte)0,
            lfUnderline = font.Underline ? (byte)1 : (byte)0,
            lfStrikeOut = font.StrikeOut ? (byte)1 : (byte)0,
            lfCharSet = font.CharSet,
            lfOutPrecision = font.OutPrecision,
            lfClipPrecision = font.ClipPrecision,
            lfQuality = font.Quality,
            lfPitchAndFamily = font.PitchAndFamily,
        };
        var face = font.FaceName.AsSpan(0, Math.Min(font.FaceName.Length, 31));
        face.CopyTo(new Span<char>(logFont.lfFaceName, 32));
        logFont.lfFaceName[face.Length] = '\0';
        return logFont;
    }

    private void Replace(nint font)
    {
        if (font == 0)
        {
            return;
        }

        var previous = Gdi32.SelectObject(_dc, font);
        if (_font == 0)
        {
            _oldFont = previous;
        }
        else
        {
            _ = Gdi32.DeleteObject(_font);
        }

        _font = font;
    }

    public int MeasureWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        fixed (char* p = text)
        {
            return Gdi32.GetTextExtentPoint32(_dc, p, text.Length, out var size) ? size.cx : 0;
        }
    }

    public void DrawLine(string text, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(text);
        fixed (char* p = text)
        {
            _ = Gdi32.TextOut(_dc, x, y, p, text.Length);
        }
    }

    public int DrawBody(ReadOnlySpan<char> text, DeviceRect bounds, bool rightToLeft)
    {
        var rect = new User32.RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var parameters = new User32.DRAWTEXTPARAMS { cbSize = (uint)sizeof(User32.DRAWTEXTPARAMS), iTabLength = TabLength };
        var format = BodyFormat | (rightToLeft ? User32.DT_RTLREADING | User32.DT_RIGHT : 0);
        fixed (char* p = text)
        {
            _ = User32.DrawTextEx(_dc, p, text.Length, &rect, format, &parameters);
        }

        return (int)parameters.uiLengthDrawn;
    }

    public int SetAbortProc(IPrintProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        PrintAbort.Current = progress;
        return Gdi32.SetAbortProc(_dc, &PrintAbort.Proc) > 0 ? 0 : LastError();
    }

    public int StartDocument(string name) => StartDocument(name, null);

    /// <summary><c>StartDocW</c>; <paramref name="output"/> sends the job to a file (tests).</summary>
    public int StartDocument(string name, string? output)
    {
        ArgumentNullException.ThrowIfNull(name);
        fixed (char* docName = name)
        fixed (char* file = output)
        {
            var info = new Gdi32.DOCINFOW { cbSize = sizeof(Gdi32.DOCINFOW), lpszDocName = (nint)docName, lpszOutput = (nint)file };
            Marshal.SetLastPInvokeError(0);
            if (Gdi32.StartDoc(_dc, &info) <= 0)
            {
                return LastError();
            }
        }

        _documentOpen = true;
        return 0;
    }

    public int StartPage() => Gdi32.StartPage(_dc) > 0 ? 0 : LastError();

    public int EndPage() => Gdi32.EndPage(_dc) > 0 ? 0 : LastError();

    public int EndDocument()
    {
        _documentOpen = false;
        return Gdi32.EndDoc(_dc) > 0 ? 0 : LastError();
    }

    public void AbortDocument()
    {
        _documentOpen = false;
        _ = Gdi32.AbortDoc(_dc);
    }

    private static int LastError() => Marshal.GetLastPInvokeError();

    public void Dispose()
    {
        if (_dc == 0)
        {
            return;
        }

        if (_documentOpen)
        {
            AbortDocument();
        }

        if (_font != 0)
        {
            _ = Gdi32.SelectObject(_dc, _oldFont);
            _ = Gdi32.DeleteObject(_font);
            _font = 0;
        }

        _ = Gdi32.DeleteDC(_dc);
        _dc = 0;
        PrintAbort.Current = null;
    }
}

/// <summary>
/// <c>PrintAbortProc</c>: while the job is not cancelled, pumps every waiting
/// message (the "Now Printing" box first gets <c>IsDialogMessageW</c>), then tells GDI whether to
/// go on.
/// </summary>
internal static unsafe class PrintAbort
{
    [ThreadStatic]
    private static IPrintProgress? t_current;

    public static IPrintProgress? Current
    {
        get => t_current;
        set => t_current = value;
    }

    [UnmanagedCallersOnly]
    public static int Proc(nint dc, int error)
    {
        var progress = t_current;
        if (progress is null)
        {
            return 1;
        }

        var dialog = (progress as Win32PrintProgress)?.Handle ?? 0;
        while (!progress.Cancelled && User32.PeekMessage(out var msg, 0, 0, 0, User32.PM_REMOVE))
        {
            if (dialog != 0 && User32.IsDialogMessage(dialog, &msg))
            {
                continue;
            }

            _ = User32.TranslateMessage(msg);
            _ = User32.DispatchMessage(msg);
        }

        return progress.Cancelled ? 0 : 1;
    }
}
