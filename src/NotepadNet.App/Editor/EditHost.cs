using System.ComponentModel;
using NotepadNet.App.Interop;

namespace NotepadNet.App.Editor;

/// <summary>
/// The USER32/comctl32 <c>Edit</c> window class hosted as a WinForms control, created the way the
/// original's <c>CreateEditControl</c> does: no client edge, control ID
/// <c>0xF</c>, style <c>0x50200104</c> with word wrap and <c>0x50300104</c> (adds
/// <c>WS_HSCROLL</c>) without. The style cannot change after creation, so toggling word wrap
/// creates a new host (see <see cref="EditorSurface"/>).
/// </summary>
internal sealed class EditHost : Control
{
    public const int ControlId = 0xF;
    public const int WrapStyle = 0x50200104;
    public const int NoWrapStyle = 0x50300104;

    public EditHost(bool wordWrap)
    {
        WordWrap = wordWrap;
        // As TextBoxBase does: the native control paints and handles clicks itself.
        SetStyle(ControlStyles.FixedHeight | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick
            | ControlStyles.UseTextForAccessibility | ControlStyles.UserPaint, false);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        // WinForms answers the parent's WM_CTLCOLOREDIT with these colors (it does not reflect
        // the message), so they must be the system colors an Edit uses by default.
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
    }

    public bool WordWrap { get; }

    /// <summary>Set while a buffer is being handed over; <c>EN_ERRSPACE</c>/<c>EN_MAXTEXT</c> then
    /// set <see cref="OutOfSpaceWhileLoading"/> instead of raising <see cref="OutOfSpace"/>
    /// (the original's <c>g_loadState</c> handshake).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Loading { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool OutOfSpaceWhileLoading { get; set; }

    /// <summary><c>EN_CHANGE</c>.</summary>
    public event EventHandler? EditChanged;

    /// <summary><c>EN_ERRSPACE</c>/<c>EN_MAXTEXT</c> outside a load.</summary>
    public event EventHandler? OutOfSpace;

    /// <summary>After the control processed a wheel message (Ctrl+wheel can change the zoom).</summary>
    public event EventHandler? WheelProcessed;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassName = "EDIT";
            cp.Style = WordWrap ? WrapStyle : NoWrapStyle;
            cp.ExStyle = 0;
            return cp;
        }
    }

    public nint Send(int message, nint wParam = 0, nint lParam = 0) => NativeMethods.SendMessage(Handle, message, wParam, lParam);

    public void Post(int message, nint wParam = 0, nint lParam = 0) => NativeMethods.PostMessage(Handle, message, wParam, lParam);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_ID, ControlId);
    }

    /// <summary>The font is the HFONT <see cref="EditorSurface"/> creates from the settings; an
    /// ambient WinForms font change (DPI, parent font) must not replace it with WM_SETFONT.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
    }

    /// <summary>Every key goes to the Edit control, as in a dialog-less Win32 window.</summary>
    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool IsInputChar(char charCode) => true;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_REFLECT | NativeMethods.WM_COMMAND:
                OnNotification(NativeMethods.HighWord(m.WParam));
                return;
            case NativeMethods.WM_MOUSEWHEEL:
            case NativeMethods.WM_MOUSEHWHEEL:
                base.WndProc(ref m);
                WheelProcessed?.Invoke(this, EventArgs.Empty);
                return;
            default:
                base.WndProc(ref m);
                return;
        }
    }

    private void OnNotification(int code)
    {
        switch (code)
        {
            case NativeMethods.EN_CHANGE:
                EditChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.EN_ERRSPACE:
            case NativeMethods.EN_MAXTEXT:
                if (Loading)
                {
                    OutOfSpaceWhileLoading = true;
                }
                else
                {
                    OutOfSpace?.Invoke(this, EventArgs.Empty);
                }

                break;
        }
    }
}
