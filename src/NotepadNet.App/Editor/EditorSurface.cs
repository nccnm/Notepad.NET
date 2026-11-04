using System.Runtime.InteropServices;
using NotepadNet.App.Interop;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Settings;
using NotepadNet.Core.Text;
using NotepadNet.Platform.Text;

namespace NotepadNet.App.Editor;

/// <summary>
/// <see cref="IEditorSurface"/> over the current <see cref="EditHost"/>. Word wrap replaces the
/// host, so everything else holds this object instead of the control.
/// </summary>
internal sealed unsafe class EditorSurface : IEditorSurface, IDisposable
{
    private readonly Control _parent;
    private readonly NotepadSettings _settings;
    private readonly string _accessibleName;
    private nint _font;

    public EditorSurface(Control parent, NotepadSettings settings, string accessibleName)
    {
        _parent = parent;
        _settings = settings;
        _accessibleName = accessibleName;
        Host = CreateHost(settings.WordWrap);
    }

    public EditHost Host { get; private set; }

    /// <summary>The host was replaced (word wrap); the window lays it out and focuses it.</summary>
    public event EventHandler? HostReplaced;

    public event EventHandler? TextChanged;

    public event EventHandler? OutOfSpace;

    /// <summary>The zoom may have changed through Ctrl+wheel.</summary>
    public event EventHandler? ZoomMayHaveChanged;

    public int TextLength => (int)Host.Send(NativeMethods.WM_GETTEXTLENGTH);

    public TextRange Selection
    {
        get
        {
            int start, end;
            Host.Send(NativeMethods.EM_GETSEL, (nint)(&start), (nint)(&end));
            return new(start, end);
        }
    }

    public int CaretIndex => (int)Host.Send(NativeMethods.EM_GETCARETINDEX);

    public bool Modified
    {
        get => Host.Send(NativeMethods.EM_GETMODIFY) != 0;
        set => Host.Send(NativeMethods.EM_SETMODIFY, value ? 1 : 0);
    }

    public bool CanUndo => Host.Send(NativeMethods.EM_CANUNDO) != 0;

    public ZoomRatio Zoom
    {
        get
        {
            int numerator = 0, denominator = 0;
            Host.Send(NativeMethods.EM_GETZOOM, (nint)(&numerator), (nint)(&denominator));
            return new(numerator, denominator);
        }

        set => Host.Send(NativeMethods.EM_SETZOOM, value.Numerator, value.Denominator);
    }

    public EndOfLineKind EndOfLine => (EndOfLineKind)(int)Host.Send(NativeMethods.EM_GETENDOFLINE);

    public bool WordWrap => Host.WordWrap;

    public bool RightToLeftReading =>
        ((long)NativeMethods.GetWindowLongPtr(Host.Handle, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_RTLREADING) != 0;

    public bool HasFocusInEditorOrMain
    {
        get
        {
            var focus = NativeMethods.GetFocus();
            return focus != 0 && (focus == Host.Handle || focus == _parent.Handle);
        }
    }

    /// <summary>DPI the font is created for: the window's DPI (<c>GetDpiForWindow</c>).</summary>
    public int Dpi => _parent.IsHandleCreated ? (int)NativeMethods.GetDpiForWindow(_parent.Handle) : 96;

    public void SetSelection(int start, int finish) => Host.Send(NativeMethods.EM_SETSEL, start, finish);

    public void ScrollCaret() => Host.Send(NativeMethods.EM_SCROLLCARET);

    public TextLease AcquireText() => new Lease(Host);

    public void ReplaceSelection(string text, bool canUndo)
    {
        fixed (char* chars = text)
        {
            Host.Send(NativeMethods.EM_REPLACESEL, canUndo ? 1 : 0, (nint)chars);
        }
    }

    /// <summary>
    /// <c>EM_SETHANDLE</c> with the out-of-space handshake; the old buffer is freed after the
    /// hand-over. Deliberate difference (parity.md): the original reallocates the Edit's own block
    /// in place; the port allocates a new block and frees the old one.
    /// </summary>
    public bool LoadBuffer(ITextBuffer buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not LocalTextBuffer local)
        {
            using (buffer)
            {
                fixed (char* chars = buffer.Chars[..(length + 1)])
                {
                    return Host.Send(NativeMethods.WM_SETTEXT, 0, (nint)chars) != 0;
                }
            }
        }

        Host.Send(NativeMethods.WM_SETREDRAW, 0);
        Host.Send(NativeMethods.EM_SETSEL, 0, 0);
        Host.Send(NativeMethods.EM_SCROLLCARET);
        var old = Host.Send(NativeMethods.EM_GETHANDLE);
        var handle = local.Detach();
        Host.OutOfSpaceWhileLoading = false;
        Host.Loading = true;
        try
        {
            Host.Send(NativeMethods.EM_SETHANDLE, handle);
        }
        finally
        {
            Host.Loading = false;
        }

        if (old != 0 && old != handle)
        {
            NativeMethods.LocalFree(old);
        }

        Host.Post(NativeMethods.EM_LIMITTEXT, 0);
        Host.Send(NativeMethods.WM_SETREDRAW, 1);
        Host.Invalidate();
        return !Host.OutOfSpaceWhileLoading;
    }

    /// <summary>File > New: empty text, then a fresh two-byte buffer holding only the NUL.</summary>
    public void Clear()
    {
        fixed (char* empty = "")
        {
            Host.Send(NativeMethods.WM_SETTEXT, 0, (nint)empty);
        }

        var buffer = new LocalTextBuffer(1);
        buffer.Chars[0] = '\0';
        var old = Host.Send(NativeMethods.EM_GETHANDLE);
        var handle = buffer.Detach();
        Host.Send(NativeMethods.EM_SETHANDLE, handle);
        if (old != 0 && old != handle)
        {
            NativeMethods.LocalFree(old);
        }
    }

    public int FileLineFromChar(int index) => (int)Host.Send(NativeMethods.EM_FILELINEFROMCHAR, index);

    public int FileLineIndex(int line) => (int)Host.Send(NativeMethods.EM_FILELINEINDEX, line);

    public int LineFromChar(int index) => (int)Host.Send(NativeMethods.EM_LINEFROMCHAR, index);

    public int LineIndex(int line) => (int)Host.Send(NativeMethods.EM_LINEINDEX, line);

    public int LineCount => (int)Host.Send(NativeMethods.EM_GETLINECOUNT);

    public void Undo() => Host.Send(NativeMethods.EM_UNDO);

    public void Cut() => Host.Post(NativeMethods.WM_CUT);

    public void Copy() => Host.Post(NativeMethods.WM_COPY);

    public void Paste() => Host.Post(NativeMethods.WM_PASTE);

    public void DeleteSelection() => Host.Post(NativeMethods.WM_CLEAR);

    /// <summary>
    /// The original posts <c>EM_SEARCHWEB</c>; it is sent here so the result is known. On this
    /// machine (2026-10-29) the control returned 1 after searching a non-empty selection and 0 for
    /// an empty one, and notified the parent with <c>EN_SEARCHWEB</c> (-1520).
    /// </summary>
    public bool SearchWeb() => Host.Send(NativeMethods.EM_SEARCHWEB) != 0;

    /// <summary>
    /// <c>RecreateEditForWordWrap</c>: turning wrap off first puts the caret at the start of line 1.
    /// The new control gets the text, zoom, modified flag, window ex-style and font; undo history
    /// and selection are lost. On failure the old control stays and false is returned.
    /// </summary>
    public bool TrySetWordWrap(bool wordWrap)
    {
        if (wordWrap == WordWrap)
        {
            return true;
        }

        var old = Host;
        if (!wordWrap)
        {
            var first = (int)old.Send(NativeMethods.EM_FILELINEINDEX, 0);
            if (first != -1)
            {
                old.Send(NativeMethods.EM_SETSEL, first, first);
                old.Send(NativeMethods.EM_SCROLLCARET);
            }
        }

        var cursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            var zoom = Zoom;
            var modified = Modified;
            var replacement = CreateHost(wordWrap, old.Bounds);
            NativeMethods.SetWindowLongPtr(replacement.Handle, NativeMethods.GWL_EXSTYLE, NativeMethods.GetWindowLongPtr(old.Handle, NativeMethods.GWL_EXSTYLE));
            if (!CopyText(old, replacement))
            {
                _parent.Controls.Remove(replacement);
                replacement.Dispose();
                return false;
            }

            Host = replacement;
            replacement.Send(NativeMethods.EM_SETMODIFY, modified ? 1 : 0);
            Zoom = zoom;
            _parent.Controls.Remove(old);
            old.Dispose();
            HostReplaced?.Invoke(this, EventArgs.Empty);
            replacement.Focus();
            return true;
        }
        finally
        {
            Cursor.Current = cursor;
        }
    }

    public bool SetFont(FontSpec font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var handle = CreateFont(font, Dpi);
        if (handle == 0)
        {
            return false;
        }

        Host.Send(NativeMethods.WM_SETFONT, handle, 1);
        if (_font != 0)
        {
            NativeMethods.DeleteObject(_font);
        }

        _font = handle;
        return true;
    }

    /// <summary><c>WM_DPICHANGED</c>: recreate the font for the new DPI.</summary>
    public void ApplyDpi() => SetFont(_settings.Font);

    public void Dispose()
    {
        Host.Dispose();
        if (_font != 0)
        {
            NativeMethods.DeleteObject(_font);
            _font = 0;
        }
    }

    /// <summary>
    /// Step 16 of <c>InitApplication</c>: when GDI picks another face than the
    /// saved one (<c>GetTextFaceW</c>, compared without case), the character set of the first
    /// installed font with the saved name replaces the saved one (<c>EnumFontsW</c>, whose callback
    /// copies <c>lfCharSet</c> and stops). A face that is not installed enumerates nothing and
    /// keeps the font as it was; GDI substitutes it.
    /// </summary>
    internal static FontSpec WithInstalledCharSet(FontSpec font, int dpi)
    {
        ArgumentNullException.ThrowIfNull(font);
        var handle = CreateFont(font, dpi);
        if (handle == 0)
        {
            return font;
        }

        var dc = NativeMethods.GetDC(0);
        try
        {
            var previous = NativeMethods.SelectObject(dc, handle);
            var face = stackalloc char[32];
            var length = NativeMethods.GetTextFace(dc, 32, face);
            NativeMethods.SelectObject(dc, previous);
            var selected = length > 0 ? new string(face) : "";
            var saved = font.FaceName[..Math.Min(font.FaceName.Length, 31)];
            if (string.Equals(selected, saved, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            var charSet = font.CharSet;
            fixed (char* name = saved)
            {
                _ = NativeMethods.EnumFonts(dc, name, &CopyCharSet, (nint)(&charSet));
            }

            return font with { CharSet = charSet };
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
            _ = NativeMethods.ReleaseDC(0, dc);
        }
    }

    [UnmanagedCallersOnly]
    private static int CopyCharSet(NativeMethods.LOGFONTW* font, nint metrics, uint type, nint charSet)
    {
        *(byte*)charSet = font->lfCharSet;
        return 0;
    }

    internal static nint CreateFont(FontSpec font, int dpi)
    {
        var logFont = new NativeMethods.LOGFONTW
        {
            lfHeight = font.HeightForDpi(dpi),
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
        return NativeMethods.CreateFontIndirect(&logFont);
    }

    private static bool CopyText(EditHost from, EditHost to)
    {
        using var lease = new Lease(from);
        var text = lease.Text;
        // WM_SETTEXT needs a NUL-terminated string; the Edit buffer always has one at the end.
        fixed (char* chars = text)
        {
            return to.Send(NativeMethods.WM_SETTEXT, 0, (nint)chars) != 0;
        }
    }

    private EditHost CreateHost(bool wordWrap, Rectangle? bounds = null)
    {
        var host = new EditHost(wordWrap) { AccessibleName = _accessibleName };
        if (bounds is { } b)
        {
            host.Bounds = b;
        }

        _parent.Controls.Add(host);
        host.CreateControl();
        Configure(host);
        host.EditChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        host.OutOfSpace += (_, _) => OutOfSpace?.Invoke(this, EventArgs.Empty);
        host.WheelProcessed += (_, _) => ZoomMayHaveChanged?.Invoke(this, EventArgs.Empty);
        if (_font != 0)
        {
            host.Send(NativeMethods.WM_SETFONT, _font, 0);
        }

        return host;
    }

    /// <summary><c>AttachEditControl</c>: no text limit, line-ending and zoom extended styles, web search.</summary>
    private void Configure(EditHost host)
    {
        host.Send(NativeMethods.EM_LIMITTEXT, 0);
        host.Send(NativeMethods.EM_SETEXTENDEDSTYLE, NativeMethods.ES_EX_ALLOWEOL_CR | NativeMethods.ES_EX_ALLOWEOL_LF,
            _settings.WindowsOnlyEol ? 0 : NativeMethods.ES_EX_ALLOWEOL_CR | NativeMethods.ES_EX_ALLOWEOL_LF);
        host.Send(NativeMethods.EM_SETEXTENDEDSTYLE, NativeMethods.ES_EX_CONVERT_EOL_ON_PASTE,
            _settings.PasteOriginalEol ? 0 : NativeMethods.ES_EX_CONVERT_EOL_ON_PASTE);
        host.Send(NativeMethods.EM_SETEXTENDEDSTYLE, NativeMethods.ES_EX_ZOOMABLE, NativeMethods.ES_EX_ZOOMABLE);
        // Meaning of 0x8 not verified; set as the original does.
        host.Send(NativeMethods.EM_SETEXTENDEDSTYLE, 0x8, 0x8);
        host.Send(NativeMethods.EM_ENABLESEARCHWEB, 1);
    }

    /// <summary>Locks the Edit's own buffer (<c>EM_GETHANDLE</c> + <c>LocalLock</c>) without copying.</summary>
    private sealed class Lease : TextLease
    {
        private readonly nint _handle;
        private readonly char* _chars;
        private readonly int _length;

        public Lease(EditHost host)
        {
            _length = (int)host.Send(NativeMethods.WM_GETTEXTLENGTH);
            _handle = host.Send(NativeMethods.EM_GETHANDLE);
            _chars = _handle == 0 ? null : (char*)NativeMethods.LocalLock(_handle);
        }

        public override ReadOnlySpan<char> Text => _chars == null ? [] : new(_chars, _length);

        protected override void Release()
        {
            if (_chars != null)
            {
                NativeMethods.LocalUnlock(_handle);
            }
        }
    }
}
