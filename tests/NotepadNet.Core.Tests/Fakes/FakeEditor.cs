using System.Text;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>Editor surface over a string, recording the calls Core makes.</summary>
internal sealed class FakeEditor : IEditorSurface
{
    private readonly StringBuilder _text = new();
    private int _start;
    private int _end;

    public FakeEditor(string text = "")
    {
        _text.Append(text);
    }

    public string Text => _text.ToString();

    public List<string> Calls { get; } = [];

    public bool FailNextLoad { get; set; }

    /// <summary>The next <see cref="ReplaceSelection"/> takes nothing, as out of memory does.</summary>
    public bool FailNextReplace { get; set; }

    public bool FailWordWrap { get; set; }

    public bool FailFont { get; set; }

    public bool Focused { get; set; } = true;

    public int OpenLeases { get; private set; }

    public int TextLength => _text.Length;

    public TextRange Selection => new(_start, _end);

    public int CaretIndex { get; set; }

    public bool Modified { get; set; }

    public bool CanUndo { get; set; }

    public ZoomRatio Zoom { get; set; } = new(0, 0);

    public EndOfLineKind EndOfLine { get; set; } = EndOfLineKind.CrLf;

    public bool WordWrap { get; set; }

    public bool HasFocusInEditorOrMain => Focused;

    public bool RightToLeftReading { get; set; }

    public event EventHandler? TextChanged;

    public event EventHandler? OutOfSpace;

    /// <summary>Simulates typing: replaces the text, sets the modified flag, raises <c>EN_CHANGE</c>.</summary>
    public void Type(string text)
    {
        _text.Clear().Append(text);
        Modified = true;
        CanUndo = true;
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseOutOfSpace() => OutOfSpace?.Invoke(this, EventArgs.Empty);

    public void SetSelection(int start, int finish)
    {
        Calls.Add($"SetSelection({start},{finish})");
        _start = Math.Clamp(start, 0, _text.Length);
        _end = finish < 0 ? _text.Length : Math.Clamp(finish, 0, _text.Length);
        CaretIndex = _end;
    }

    public void ScrollCaret() => Calls.Add("ScrollCaret");

    public TextLease AcquireText()
    {
        OpenLeases++;
        return new Lease(this);
    }

    public void ReplaceSelection(string text, bool canUndo)
    {
        Calls.Add($"ReplaceSelection({text.Replace("\r\n", "\\r\\n", StringComparison.Ordinal)},{canUndo})");
        if (FailNextReplace)
        {
            FailNextReplace = false;
            RaiseOutOfSpace();
            return;
        }

        _text.Remove(_start, _end - _start).Insert(_start, text);
        _start = _end = _start + text.Length;
        CaretIndex = _end;
        Modified = true;
        CanUndo |= canUndo;
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool LoadBuffer(ITextBuffer buffer, int length)
    {
        Calls.Add("LoadBuffer");
        using (buffer)
        {
            if (FailNextLoad)
            {
                FailNextLoad = false;
                return false;
            }

            _text.Clear().Append(buffer.Chars[..length]);
        }

        _start = _end = CaretIndex = 0;
        Modified = false;
        CanUndo = false;
        return true;
    }

    public void Clear()
    {
        Calls.Add("Clear");
        _text.Clear();
        _start = _end = CaretIndex = 0;
        Modified = false;
        CanUndo = false;
    }

    public int FileLineFromChar(int index)
    {
        var line = 0;
        for (var i = 0; i < Math.Min(index, _text.Length); i++)
        {
            if (_text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    public int FileLineIndex(int line)
    {
        var current = 0;
        for (var i = 0; i < _text.Length && current < line; i++)
        {
            if (_text[i] == '\n')
            {
                current++;
                if (current == line)
                {
                    return i + 1;
                }
            }
        }

        return line == 0 ? 0 : -1;
    }

    /// <summary>With <see cref="WordWrap"/> on, displayed lines break every this many characters.</summary>
    public int WrapColumn { get; set; }

    public int LineCount => DisplayedLineStarts().Count;

    public int LineFromChar(int index)
    {
        var starts = DisplayedLineStarts();
        var line = 0;
        while (line + 1 < starts.Count && starts[line + 1] <= index)
        {
            line++;
        }

        return line;
    }

    public int LineIndex(int line)
    {
        var starts = DisplayedLineStarts();
        return line >= 0 && line < starts.Count ? starts[line] : -1;
    }

    private List<int> DisplayedLineStarts()
    {
        List<int> starts = [0];
        var column = 0;
        for (var i = 0; i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                starts.Add(i + 1);
                column = 0;
            }
            else if (WordWrap && WrapColumn > 0 && ++column == WrapColumn && i + 1 < _text.Length && _text[i + 1] is not ('\r' or '\n'))
            {
                starts.Add(i + 1);
                column = 0;
            }
        }

        return starts;
    }

    public void Undo() => Calls.Add("Undo");

    public void Cut() => Calls.Add("Cut");

    public void Copy() => Calls.Add("Copy");

    public void Paste() => Calls.Add("Paste");

    public void DeleteSelection() => Calls.Add("Delete");

    /// <summary>What <see cref="SearchWeb"/> returns; false simulates an Edit control without web search.</summary>
    public bool SearchWebHandled { get; set; } = true;

    public bool SearchWeb()
    {
        Calls.Add("SearchWeb");
        return SearchWebHandled;
    }

    public bool TrySetWordWrap(bool wordWrap)
    {
        Calls.Add($"TrySetWordWrap({wordWrap})");
        if (FailWordWrap)
        {
            return false;
        }

        WordWrap = wordWrap;
        return true;
    }

    public bool SetFont(FontSpec font)
    {
        Calls.Add($"SetFont({font.FaceName},{font.PointSize10})");
        return !FailFont;
    }

    private sealed class Lease(FakeEditor owner) : TextLease
    {
        private readonly string _snapshot = owner.Text;

        public override ReadOnlySpan<char> Text => _snapshot;

        protected override void Release() => owner.OpenLeases--;
    }
}
