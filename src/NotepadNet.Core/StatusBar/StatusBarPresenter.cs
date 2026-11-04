using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.StatusBar;

/// <summary>The five status bar parts: 0 messages, 1 Ln/Col, 2 zoom, 3 line endings, 4 encoding.</summary>
public interface IStatusBarView
{
    void SetPartText(int part, string text);
}

/// <summary>
/// Status bar contents: <c>UpdateStatusBar</c>,
/// <c>UpdateLineColPane</c>, <c>UpdateZoomPane</c>, <c>SetMessagePane</c>. Texts are sent even
/// while the bar is hidden, as in the original.
/// </summary>
public sealed class StatusBarPresenter(IStatusBarView view, IEditorSurface editor, ILocalizedStrings strings, Func<TextEncodingKind> currentEncoding)
{
    public const int MessagePart = 0;
    public const int LineColumnPart = 1;
    public const int ZoomPart = 2;
    public const int EndOfLinePart = 3;
    public const int EncodingPart = 4;

    private readonly IStatusBarView _view = view;
    private readonly IEditorSurface _editor = editor;
    private readonly ILocalizedStrings _strings = strings;
    private readonly Func<TextEncodingKind> _currentEncoding = currentEncoding;
    private int _line = -1;
    private int _column = -1;

    public void UpdateAll()
    {
        UpdateLineColumn(force: true);
        _view.SetPartText(EndOfLinePart, EndOfLineText(_editor.EndOfLine));
        _view.SetPartText(EncodingPart, MessageText.Printf(_strings.GetString(StringKeys.EncodingPane), EncodingName(_currentEncoding())));
        UpdateZoom();
    }

    /// <summary>
    /// Ln/Col of the caret on physical lines, 1-based, in UTF-16 units. Sent only when forced or
    /// changed. The text is one space followed by string 37, which starts with a space itself.
    /// </summary>
    public void UpdateLineColumn(bool force)
    {
        var caret = _editor.CaretIndex;
        var line = _editor.FileLineFromChar(caret);
        var column = caret - _editor.FileLineIndex(line) + 1;
        if (!force && line + 1 == _line && column == _column)
        {
            return;
        }

        _line = line + 1;
        _column = column;
        _view.SetPartText(LineColumnPart, " " + MessageText.Printf(_strings.GetString(StringKeys.LineColumn), _line, _column));
    }

    public void UpdateZoom() =>
        _view.SetPartText(ZoomPart, MessageText.Printf(_strings.GetString(StringKeys.ZoomPercent), ZoomPolicy.Percent(_editor.Zoom)));

    public void SetMessage(string text) => _view.SetPartText(MessagePart, text);

    /// <summary>1 CRLF, 2 CR, 3 LF; anything else (0 = detect) shows CRLF.</summary>
    public string EndOfLineText(EndOfLineKind kind) => _strings.GetString(kind switch
    {
        EndOfLineKind.Cr => StringKeys.EolMacintosh,
        EndOfLineKind.Lf => StringKeys.EolUnix,
        _ => StringKeys.EolWindows,
    });

    /// <summary>Encoding names; GB18030 is a fixed string, unknown codes show ANSI.</summary>
    public string EncodingName(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16LE => _strings.GetString(StringKeys.EncodingUtf16LE),
        TextEncodingKind.Utf16BE => _strings.GetString(StringKeys.EncodingUtf16BE),
        TextEncodingKind.Utf8Bom => _strings.GetString(StringKeys.EncodingUtf8Bom),
        TextEncodingKind.Utf8 => _strings.GetString(StringKeys.EncodingUtf8),
        TextEncodingKind.Gb18030 => "GB18030",
        _ => _strings.GetString(StringKeys.EncodingAnsi),
    };
}

/// <summary>Part widths (<c>LayoutStatusBarParts</c>).</summary>
public static class StatusBarLayout
{
    /// <summary>Logical widths of parts 1-4 at 96 DPI; part 0 takes the rest.</summary>
    public static readonly int[] FixedWidths = [140, 50, 120, 120];

    /// <summary>
    /// Right edges of the five parts for <c>SB_SETPARTS</c>: right to left 120, 120, 50, 140
    /// logical pixels scaled by <c>MulDiv(x, dpi, 96)</c>, negative edges clamped to 0, the last
    /// part -1 (to the end).
    /// </summary>
    public static int[] RightEdges(int width, int dpi)
    {
        var s120 = Win32Math.Scale(120, dpi);
        var s50 = Win32Math.Scale(50, dpi);
        var s140 = Win32Math.Scale(140, dpi);
        int[] edges =
        [
            width - s120 - s120 - s50 - s140,
            width - s120 - s120 - s50,
            width - s120 - s120,
            width - s120,
            -1,
        ];
        for (var i = 0; i < 4; i++)
        {
            edges[i] = Math.Max(edges[i], 0);
        }

        return edges;
    }

    /// <summary>Widths of the five parts derived from <see cref="RightEdges"/>.</summary>
    public static int[] Widths(int width, int dpi)
    {
        var edges = RightEdges(width, dpi);
        var widths = new int[5];
        var left = 0;
        for (var i = 0; i < 4; i++)
        {
            widths[i] = Math.Max(edges[i] - left, 0);
            left = Math.Max(left, edges[i]);
        }

        widths[4] = Math.Max(width - left, 0);
        return widths;
    }
}
