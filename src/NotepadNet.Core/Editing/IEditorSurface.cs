using NotepadNet.Core.Text;

namespace NotepadNet.Core.Editing;

/// <summary>Selection <c>[Start, End)</c> as <c>EM_GETSEL</c> reports it.</summary>
public readonly record struct TextRange(int Start, int End)
{
    public bool IsEmpty => Start == End;

    public int Length => End - Start;
}

/// <summary><c>EM_GETZOOM</c> numerator and denominator.</summary>
public readonly record struct ZoomRatio(int Numerator, int Denominator);

/// <summary><c>EM_GETENDOFLINE</c> values (status bar).</summary>
public enum EndOfLineKind
{
    Detect = 0,
    CrLf = 1,
    Cr = 2,
    Lf = 3,
}

/// <summary>
/// Read access to the editor text without copying it. The implementation keeps the editor
/// buffer locked until disposal.
/// </summary>
public abstract class TextLease : IDisposable
{
    public abstract ReadOnlySpan<char> Text { get; }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    protected abstract void Release();
}

/// <summary>
/// The editing surface: the USER32 Edit control in the app, a fake in tests. Members map to
/// the Edit messages the original sends; Core holds the rules around them.
/// </summary>
public interface IEditorSurface
{
    /// <summary><c>WM_GETTEXTLENGTH</c>.</summary>
    int TextLength { get; }

    /// <summary><c>EM_GETSEL</c>.</summary>
    TextRange Selection { get; }

    /// <summary><c>EM_SETSEL</c>; <paramref name="finish"/> -1 selects to the end.</summary>
    void SetSelection(int start, int finish);

    /// <summary><c>EM_SCROLLCARET</c>.</summary>
    void ScrollCaret();

    TextLease AcquireText();

    /// <summary><c>EM_REPLACESEL</c>.</summary>
    void ReplaceSelection(string text, bool canUndo);

    /// <summary>
    /// Hands a decoded buffer to the editor (<c>EM_SETHANDLE</c>), which takes ownership in every
    /// case. False when the editor reported <c>EN_ERRSPACE</c>/<c>EN_MAXTEXT</c> while taking it.
    /// </summary>
    bool LoadBuffer(ITextBuffer buffer, int length);

    /// <summary>Empties the editor and gives it a fresh empty buffer (File > New).</summary>
    void Clear();

    /// <summary><c>EM_GETCARETINDEX</c>.</summary>
    int CaretIndex { get; }

    /// <summary><c>EM_FILELINEFROMCHAR</c>: physical line (word-wrap breaks do not count).</summary>
    int FileLineFromChar(int index);

    /// <summary><c>EM_FILELINEINDEX</c>.</summary>
    int FileLineIndex(int line);

    /// <summary><c>EM_LINEFROMCHAR</c>: displayed line, counting word-wrap breaks.</summary>
    int LineFromChar(int index);

    /// <summary><c>EM_LINEINDEX</c>: first character of a displayed line, -1 when there is none.</summary>
    int LineIndex(int line);

    /// <summary><c>EM_GETLINECOUNT</c>: number of displayed lines.</summary>
    int LineCount { get; }

    /// <summary><c>EM_GETMODIFY</c> / <c>EM_SETMODIFY</c>.</summary>
    bool Modified { get; set; }

    /// <summary><c>EM_CANUNDO</c>.</summary>
    bool CanUndo { get; }

    /// <summary><c>EM_UNDO</c>.</summary>
    void Undo();

    /// <summary>Posts <c>WM_CUT</c>.</summary>
    void Cut();

    /// <summary>Posts <c>WM_COPY</c>.</summary>
    void Copy();

    /// <summary>Posts <c>WM_PASTE</c>.</summary>
    void Paste();

    /// <summary>Posts <c>WM_CLEAR</c>.</summary>
    void DeleteSelection();

    /// <summary>
    /// Asks the Edit control to search the web for the selection (<c>EM_SEARCHWEB</c>).
    /// Returns false when the control did not handle it (no search performed).
    /// </summary>
    bool SearchWeb();

    /// <summary><c>EM_GETZOOM</c> / <c>EM_SETZOOM</c>.</summary>
    ZoomRatio Zoom { get; set; }

    /// <summary><c>EM_GETENDOFLINE</c>.</summary>
    EndOfLineKind EndOfLine { get; }

    bool WordWrap { get; }

    /// <summary>Recreates the editor with or without word wrap. False when the new control could
    /// not be created; the old one stays.</summary>
    bool TrySetWordWrap(bool wordWrap);

    /// <summary>Creates the font for the current DPI and sends <c>WM_SETFONT</c>. False when
    /// <c>CreateFontIndirectW</c> fails.</summary>
    bool SetFont(FontSpec font);

    /// <summary>Focus is on the editor or on the main window (clipboard commands check this).</summary>
    bool HasFocusInEditorOrMain { get; }

    /// <summary><c>WS_EX_RTLREADING</c> of the Edit control (its context menu toggles it).</summary>
    bool RightToLeftReading { get; }

    /// <summary><c>EN_CHANGE</c>.</summary>
    event EventHandler? TextChanged;

    /// <summary><c>EN_ERRSPACE</c>/<c>EN_MAXTEXT</c> outside a load.</summary>
    event EventHandler? OutOfSpace;
}
