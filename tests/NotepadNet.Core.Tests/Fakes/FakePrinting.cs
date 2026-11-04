using NotepadNet.Core.Editing;
using NotepadNet.Core.Printing;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>
/// A printer with a fixed-pitch font: every character is <see cref="CharWidth"/> pixels wide and
/// a line is <see cref="LineHeight"/> high. <see cref="DrawBody"/> wraps like a simplified
/// <c>DrawTextExW</c>: lines end at CRLF, LF or CR, long lines break at the page width, and only
/// lines that fit entirely are drawn. Every call is recorded in <see cref="Log"/>.
/// </summary>
internal sealed class FakePrintDevice : IPrintDevice
{
    public const int CharWidth = 10;

    /// <summary>US Letter at 600 DPI with a 0.25 in unprintable border.</summary>
    public static PrinterMetrics Letter600 { get; } = new(4800, 6300, 600, 600, 5100, 6600, 150, 150);

    public PrinterMetrics Metrics { get; init; } = Letter600;

    public int LineHeight { get; init; } = 100;

    public List<string> Log { get; } = [];

    public List<(string Text, int X, int Y)> Lines { get; } = [];

    public List<(string Text, DeviceRect Bounds, bool RightToLeft)> Bodies { get; } = [];

    public (FontSpec Font, int Height)? Font { get; private set; }

    public IPrintProgress? AbortProgress { get; private set; }

    public int SetAbortProcError { get; init; }

    public int StartDocumentError { get; init; }

    public int EndDocumentError { get; init; }

    /// <summary>Page whose <c>StartPage</c> fails, and the error.</summary>
    public (int Page, int Error) StartPageFailure { get; init; }

    public (int Page, int Error) EndPageFailure { get; init; }

    /// <summary>Called after a page's body is drawn (tests cancel from here).</summary>
    public Action<int>? AfterBody { get; init; }

    /// <summary>When set, a page draws nothing at all.</summary>
    public bool DrawsNothing { get; init; }

    public bool Disposed { get; private set; }

    private int _page;

    public PrintFontMetrics SelectFont(FontSpec font, int height)
    {
        Font = (font, height);
        return new(LineHeight);
    }

    public int MeasureWidth(string text) => text.Length * CharWidth;

    public void DrawLine(string text, int x, int y)
    {
        Lines.Add((text, x, y));
        Log.Add($"line {text}@{x},{y}");
    }

    public int DrawBody(ReadOnlySpan<char> text, DeviceRect bounds, bool rightToLeft)
    {
        var maxLines = (bounds.Bottom - bounds.Top) / LineHeight;
        var maxChars = Math.Max(1, (bounds.Right - bounds.Left) / CharWidth);
        var consumed = 0;
        for (var line = 0; line < maxLines && consumed < text.Length && !DrawsNothing; line++)
        {
            var rest = text[consumed..];
            var end = rest.IndexOfAny('\r', '\n');
            var length = end < 0 ? rest.Length : end;
            if (length > maxChars)
            {
                consumed += maxChars;
                continue;
            }

            consumed += length;
            if (consumed < text.Length && text[consumed] == '\r')
            {
                consumed++;
            }

            if (consumed < text.Length && text[consumed] == '\n')
            {
                consumed++;
            }
        }

        Bodies.Add((text[..consumed].ToString(), bounds, rightToLeft));
        Log.Add($"body {consumed}");
        AfterBody?.Invoke(_page);
        return consumed;
    }

    public int SetAbortProc(IPrintProgress progress)
    {
        AbortProgress = progress;
        Log.Add("abortproc");
        return SetAbortProcError;
    }

    public int StartDocument(string name)
    {
        Log.Add($"startdoc {name}");
        return StartDocumentError;
    }

    public int StartPage()
    {
        _page++;
        Log.Add($"startpage {_page}");
        return StartPageFailure.Page == _page ? StartPageFailure.Error : 0;
    }

    public int EndPage()
    {
        Log.Add($"endpage {_page}");
        return EndPageFailure.Page == _page ? EndPageFailure.Error : 0;
    }

    public int EndDocument()
    {
        Log.Add("enddoc");
        return EndDocumentError;
    }

    public void AbortDocument() => Log.Add("abortdoc");

    public void Dispose() => Disposed = true;
}

internal sealed class FakePrintProgress(string fileLabel) : IPrintProgress
{
    public string FileLabel { get; } = fileLabel;

    public List<int> Pages { get; } = [];

    public bool Cancelled { get; set; }

    public bool Disposed { get; private set; }

    public void ShowPage(int page) => Pages.Add(page);

    public void Dispose() => Disposed = true;
}

internal sealed class FakePrinterService : IPrinterService
{
    public PrinterOpenResult? NextOpen { get; set; }

    public FakePrintDevice Device { get; set; } = new();

    /// <summary>Returned by <see cref="Open"/> instead of <see cref="Device"/> when set.</summary>
    public IPrintDevice? DeviceOverride { get; set; }

    public List<(PrinterChoice Choice, string? Name)> Opens { get; } = [];

    public FakePrintProgress? Progress { get; private set; }

    public Queue<PageSetupResult> PageSetupResults { get; } = new();

    public List<PageSetupValues> PageSetupCalls { get; } = [];

    public int Resets { get; private set; }

    public PrinterOpenResult Open(PrinterChoice choice, string? printerName)
    {
        Opens.Add((choice, printerName));
        return NextOpen ?? new(PrinterOpenStatus.Opened, DeviceOverride ?? Device);
    }

    public IPrintProgress ShowProgress(string fileLabel) => Progress = new FakePrintProgress(fileLabel);

    public PageSetupResult ShowPageSetup(PageSetupValues values)
    {
        PageSetupCalls.Add(values);
        return PageSetupResults.Dequeue();
    }

    public void ResetPrinterSelection() => Resets++;
}
