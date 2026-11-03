using System.ComponentModel;
using System.Runtime.InteropServices;
using NotepadClassic.App.Editor;
using NotepadClassic.App.Interop;
using NotepadClassic.Core.Documents;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Settings;
using NotepadClassic.Core.StatusBar;

namespace NotepadClassic.App.Shell;

/// <summary>
/// The main window: menu, Edit control and a
/// five-part status bar laid out by hand on <c>WM_SIZE</c>, minimum width, focus forwarding,
/// zoom pane after mouse wheel, font recreation on <c>WM_DPICHANGED</c>.
/// </summary>
internal sealed class MainForm : Form, IMainWindow, IStatusBarView
{
    private readonly StatusStrip _status;
    private readonly StatusBarRenderer _statusRenderer;
    private readonly ToolStripStatusLabel[] _parts = new ToolStripStatusLabel[5];
    private MenuStrip? _menu;
    private (int Dpi, int Height) _nativeStatusHeight;

    /// <summary>DPI the bar fonts and menu item widths were last set for (0: not yet).</summary>
    private int _barDpi;
    private Font? _menuFont;
    private Font? _statusFont;

    /// <summary>Whether the status bar should show. <c>Control.Visible</c> reads false while the
    /// window itself is hidden, so layout uses this instead.</summary>
    private bool _statusBarVisible;

    public MainForm(NotepadSettings settings, ILocalizedStrings strings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(strings);
        StartupTrace.Mark("Form base constructed");
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.WindowsDefaultBounds;
        using (var icon = typeof(MainForm).Assembly.GetManifestResourceStream("NotepadClassic.App.app.ico"))
        {
            Icon = icon is null ? null : new Icon(icon);
        }
        Text = strings.GetString(StringKeys.AppName);
        SetOpacity(settings.OpacityPercent);
        SetAlwaysOnTop(settings.AlwaysOnTop);

        _status = new StatusStrip
        {
            Dock = DockStyle.None,
            // Sized by LayoutChildren like the original's status window, not by its items.
            AutoSize = false,
            Padding = Padding.Empty,
            // CreateStatusWindowW(0x44800000) has no SBARS_SIZEGRIP, but comctl32 still draws the
            // gripper under a sizable parent (seen on the original, 2026-10-29).
            SizingGrip = true,
            CanOverflow = false,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            AccessibleName = strings.GetString(StringKeys.StatusBarAccessibleName),
            Visible = settings.StatusBar,
        };
        _statusBarVisible = settings.StatusBar;
        _statusRenderer = new StatusBarRenderer(() => IsHandleCreated ? (int)NativeMethods.GetDpiForWindow(Handle) : DeviceDpi);
        _status.Renderer = _statusRenderer;
        for (var i = 0; i < _parts.Length; i++)
        {
            _parts[i] = new ToolStripStatusLabel
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
                BorderSides = i == 0 ? ToolStripStatusLabelBorderSides.None : ToolStripStatusLabelBorderSides.Left,
                BorderStyle = Border3DStyle.Etched,
            };
            _status.Items.Add(_parts[i]);
        }

        StartupTrace.Mark("Status strip built");
        // The Edit control is a native child created right away (the original creates it in
        // InitApplication); without the main window handle WinForms would park it elsewhere.
        CreateHandle();
        StartupTrace.Mark("Main window handle created");
        _editor = new EditorSurface(this, settings, strings.GetString(StringKeys.EditorAccessibleName));
        StartupTrace.Mark("Edit control created");
        Editor.HostReplaced += (_, _) => LayoutChildren();
        Controls.Add(_status);
        NativeMethods.DragAcceptFiles(Handle, true);
    }

    /// <summary><c>WM_DROPFILES</c>: gets the first dropped file only (<c>OnDropFiles</c>).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? FileDropped { get; set; }

    /// <summary><c>/.SETUP</c> mode.</summary>
    public bool SetupMode { get; private set; }

    private nint _systemMenu;

    /// <summary>
    /// <c>HandleSetupArg</c>: keeps the system menu handle and removes the minimize box; minimize
    /// and window cycling from the system menu are then swallowed. Deliberate difference
    /// (parity.md): the original sets the style to <c>0x00FD0000</c> after the window is shown,
    /// which also clears <c>WS_VISIBLE</c> (observed 2026-10-30: style reads <c>0x04FD0000</c>,
    /// <c>IsWindowVisible</c> is FALSE, the window stays on screen as a stale image without status
    /// bar or scroll bars and takes no input). The port only drops <c>WS_MINIMIZEBOX</c>.
    /// </summary>
    public void EnterSetupMode()
    {
        SetupMode = true;
        _systemMenu = NativeMethods.GetSystemMenu(Handle, false);
        var style = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_STYLE, style & ~(nint)WS_MINIMIZEBOX);
        NativeMethods.SetWindowPos(
            Handle, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>The style the original sets in <c>/.SETUP</c> mode.</summary>
    public const int OriginalSetupWindowStyle = 0x00FD0000;

    public const int WS_MINIMIZEBOX = 0x00020000;

    /// <summary>The window's normal position in DPI-unaware coordinates (<c>SaveSettings</c>).</summary>
    public Core.Settings.WindowRect? NormalPositionUnaware() => WindowPlacement.GetUnaware(Handle);

    public EditorSurface Editor => _editor ?? throw new InvalidOperationException("The editor is created in the constructor.");

    /// <summary>Null only while the constructor creates the window handle.</summary>
    private readonly EditorSurface? _editor;

    /// <summary><c>WM_CLOSE</c>: settings are saved first, then the save-changes question
    /// decides whether the window closes.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool>? CloseQuery { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? BeforeCloseQuery { get; set; }

    /// <summary><c>WM_QUERYENDSESSION</c>: gets <c>lParam &amp; ENDSESSION_CLOSEAPP</c>, returns
    /// whether the session may end. WinForms' own handling (closing every form) is bypassed.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool, bool>? QueryEndSession { get; set; }

    /// <summary><c>WM_ENDSESSION</c>: gets <c>wParam != 0</c> and <c>lParam &amp; ENDSESSION_CLOSEAPP</c>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<bool, bool>? EndSession { get; set; }

    /// <summary>Ctrl+wheel may have changed the zoom (<c>UpdateZoomPane</c> after <c>WM_MOUSEWHEEL</c>).</summary>
    public event EventHandler? ZoomMayHaveChanged;

    /// <summary>
    /// Registered messages (<c>FINDMSGSTRING</c> of the Find/Replace dialog): gets the message and
    /// <c>lParam</c>, returns whether it handled it. Handled messages return 0.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int, nint, bool>? RegisteredMessage { get; set; }

    /// <summary><c>WM_APPCOMMAND</c> with <c>APPCOMMAND_BROWSER_SEARCH</c>: runs Find without the
    /// menu's enable rule, as the original calls its command handler directly.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? BrowserSearch { get; set; }

    public string PartText(int part) => _parts[part].Text ?? "";

    public int PartWidth(int part) => _parts[part].Width;

    public int MonitorDpiForLayout => MonitorDpi();

    /// <summary>
    /// Height of the menu bar, which sits inside the client area here but outside it in the
    /// original: dialogs placed relative to the client area are moved down by this much.
    /// </summary>
    public int MenuBarHeight => _menu?.Height ?? 0;

    public void SetMenu(MenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        _menu = menu;
        menu.AutoSize = false;
        var renderer = new MenuBarRenderer();
        menu.Renderer = renderer;
        // Entered with the mouse when a button is down, otherwise with Alt or F10.
        menu.MenuActivate += (_, _) =>
        {
            renderer.KeyboardMode = NativeMethods.GetKeyState(NativeMethods.VK_LBUTTON) >= 0
                && NativeMethods.GetKeyState(NativeMethods.VK_RBUTTON) >= 0
                && NativeMethods.GetKeyState(NativeMethods.VK_MBUTTON) >= 0;
            menu.Invalidate();
        };
        menu.MenuDeactivate += (_, _) =>
        {
            renderer.KeyboardMode = false;
            menu.Invalidate();
        };
        Controls.Add(menu);
        MainMenuStrip = menu;
        _barDpi = 0;
        LayoutChildren();
    }

    public void SetTitle(string title) => Text = title;

    public string Title => Text;

    public void RequestClose() => NativeMethods.PostMessage(Handle, NativeMethods.WM_CLOSE, 0, 0);

    public void SetStatusBarVisible(bool visible)
    {
        _statusBarVisible = visible;
        _status.Visible = visible;
        LayoutChildren();
    }

    /// <summary><c>WS_EX_LAYERED</c> with <c>LWA_ALPHA</c> through <see cref="Form.Opacity"/>; the
    /// window has no layered style at 100%. The alpha covers the native Edit child as well; menu
    /// drop-downs and dialogs are top-level windows of their own and stay opaque.</summary>
    public void SetOpacity(int percent) => Opacity = percent / 100.0;

    /// <summary><c>WS_EX_TOPMOST</c> through <see cref="Form.TopMost"/>. Windows moves owned windows
    /// (dialogs) with their owner, so they stay above it.</summary>
    public void SetAlwaysOnTop(bool onTop) => TopMost = onTop;

    public void SetPartText(int part, string text) => _parts[part].Text = text;

    /// <summary><c>SetCursor(IDC_WAIT)</c> until disposed.</summary>
    public IDisposable ShowWaitCursor()
    {
        var previous = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        return new CursorRestore(previous);
    }

    private sealed class CursorRestore(Cursor? previous) : IDisposable
    {
        public void Dispose() => Cursor.Current = previous;
    }

    /// <summary><c>LayoutChildren</c>: menu on top, Edit in the middle, status bar at the bottom.</summary>
    public void LayoutChildren()
    {
        if (_editor is null)
        {
            return;
        }

        // WM_SIZE arrives inside WM_WINDOWPOSCHANGED, before WinForms refreshes ClientSize, so the
        // size comes from the window itself.
        var client = NativeMethods.GetClientRect(Handle, out var rect)
            ? new Size(rect.Right - rect.Left, rect.Bottom - rect.Top)
            : ClientSize;
        var dpi = (int)NativeMethods.GetDpiForWindow(Handle);
        if (dpi != _barDpi)
        {
            ApplyBarMetrics(dpi);
        }

        var menuHeight = 0;
        if (_menu is not null)
        {
            // The original's menu bar is SM_CYMENU tall.
            menuHeight = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYMENU, (uint)dpi);
            _menu.SetBounds(0, 0, client.Width, menuHeight);
        }

        var statusHeight = 0;
        if (_statusBarVisible)
        {
            statusHeight = NativeStatusBarHeight(dpi);
            _status.SetBounds(0, client.Height - statusHeight, client.Width, statusHeight);
            var widths = StatusBarLayout.Widths(client.Width, MonitorDpi());
            // msctls_statusbar32 draws each part's separator one pixel left of the part edge and
            // its text from there; a label's border and text start at the label's left edge, so
            // every label after the first starts one pixel early.
            if (widths[0] > 0)
            {
                widths[0]--;
                widths[^1]++;
            }

            for (var i = 0; i < _parts.Length; i++)
            {
                _parts[i].Width = widths[i];
            }
        }

        _editor.Host.SetBounds(0, menuHeight, client.Width, Math.Max(0, client.Height - menuHeight - statusHeight));
    }

    /// <summary>
    /// Fonts and menu item geometry for <paramref name="dpi"/>: the menu and status fonts of the
    /// non-client metrics, and each menu item exactly as wide as a Win32 menu bar item with the
    /// same text, its text centered (USER32 pads both sides equally). WinForms does not rescale
    /// either font on <c>WM_DPICHANGED</c>.
    /// </summary>
    private void ApplyBarMetrics(int dpi)
    {
        _barDpi = dpi;
        if (NativeBarMetrics.Fonts(dpi) is var (menuFont, statusFont))
        {
            StartupTrace.Mark("Bar fonts read");
            _status.Font = statusFont;
            _statusFont?.Dispose();
            _statusFont = statusFont;
            _statusRenderer.TextOrigin = NativeBarMetrics.StatusTextOrigin(Handle, statusFont);
            StartupTrace.Mark("Status text origin measured");
            if (_menu is not null)
            {
                _menu.Font = menuFont;
                _menuFont?.Dispose();
                _menuFont = menuFont;
            }
            else
            {
                menuFont.Dispose();
            }
        }

        if (_menu is null)
        {
            return;
        }

        var items = _menu.Items.Cast<ToolStripItem>().ToList();
        NativeMethods.GetWindowRect(Handle, out var window);
        var native = NativeBarMetrics.MenuItems(
            items.Select(i => i.Text ?? "").ToList(),
            Rectangle.FromLTRB(window.Left, window.Top, window.Right, window.Bottom));
        StartupTrace.Mark("Menu items measured");
        _menu.Padding = new Padding(native?[0].Left ?? 0, 0, 0, 0);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.Margin = Padding.Empty;
            item.Padding = Padding.Empty;
            item.AutoSize = native is null;
            if (native is not null)
            {
                // The Win32 item is one pixel shorter than the bar (SM_CYMENU), from its top.
                item.Size = new Size(native[i].Width, native[i].Height);
            }
        }
    }

    /// <summary>
    /// Height of a real <c>msctls_statusbar32</c> at this DPI: a hidden one is created, sized by
    /// comctl32 itself and destroyed. Cached per DPI.
    /// </summary>
    private int NativeStatusBarHeight(int dpi)
    {
        if (_nativeStatusHeight.Dpi == dpi)
        {
            return _nativeStatusHeight.Height;
        }

        var height = _status.GetPreferredSize(Size.Empty).Height;
        var probe = NativeMethods.CreateWindowEx(0, "msctls_statusbar32", null, NativeMethods.WS_CHILD, 0, 0, 0, 0, Handle, 0, 0, 0);
        if (probe != 0)
        {
            NativeMethods.SendMessage(probe, NativeMethods.WM_SIZE, 0, 0);
            if (NativeMethods.GetWindowRect(probe, out var rect) && rect.Bottom > rect.Top)
            {
                height = rect.Bottom - rect.Top;
            }

            NativeMethods.DestroyWindow(probe);
        }

        _nativeStatusHeight = (dpi, height);
        return height;
    }

    /// <summary><c>GetDpiForMonitor(MonitorFromWindow(MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI)</c>.</summary>
    private int MonitorDpi()
    {
        if (!IsHandleCreated)
        {
            return DeviceDpi;
        }

        var monitor = NativeMethods.MonitorFromWindow(Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) >= 0 ? (int)dpi : DeviceDpi;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        BeforeCloseQuery?.Invoke();
        if (CloseQuery is not null && !CloseQuery())
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_SIZE:
                base.WndProc(ref m);
                // Only restored or maximized sizes lay out; minimized and the MAXSHOW/MAXHIDE
                // notifications do nothing.
                if ((int)m.WParam is NativeMethods.SIZE_RESTORED or NativeMethods.SIZE_MAXIMIZED)
                {
                    LayoutChildren();
                }

                return;
            case NativeMethods.WM_GETMINMAXINFO:
                base.WndProc(ref m);
                // ptMinTrackSize.x = MulDiv(230, dpi, 96); the minimum height is left alone.
                Marshal.WriteInt32(m.LParam, 24, Win32Math.Scale(230, (int)NativeMethods.GetDpiForWindow(m.HWnd)));
                return;
            case NativeMethods.WM_ACTIVATE:
                base.WndProc(ref m);
                if (NativeMethods.LowWord(m.WParam) is NativeMethods.WA_ACTIVE or NativeMethods.WA_CLICKACTIVE
                    && !NativeMethods.IsIconic(Handle) && NativeMethods.GetForegroundWindow() == Handle)
                {
                    _editor?.Host.Focus();
                }

                return;
            case NativeMethods.WM_SETFOCUS:
                base.WndProc(ref m);
                if (!NativeMethods.IsIconic(Handle))
                {
                    _editor?.Host.Focus();
                }

                return;
            case NativeMethods.WM_MOUSEWHEEL:
            case NativeMethods.WM_MOUSEHWHEEL:
                base.WndProc(ref m);
                ZoomMayHaveChanged?.Invoke(this, EventArgs.Empty);
                return;
            case NativeMethods.WM_DPICHANGED:
                base.WndProc(ref m);
                _editor?.ApplyDpi();
                LayoutChildren();
                // The menu and status strips rescale their fonts again on
                // WM_DPICHANGED_AFTERPARENT, which comes after this message returns; the bar
                // metrics are applied once more after it.
                BeginInvoke(() =>
                {
                    _barDpi = 0;
                    LayoutChildren();
                    Invalidate(invalidateChildren: true);
                });
                Invalidate(invalidateChildren: true);
                return;
            case NativeMethods.WM_APPCOMMAND:
                // GET_APPCOMMAND_LPARAM; other commands go to DefWindowProc (the original lets
                // them fall into its WM_COMMAND branch, which is not ported).
                if ((NativeMethods.HighWord(m.LParam) & 0xFFF) == NativeMethods.APPCOMMAND_BROWSER_SEARCH && BrowserSearch is not null)
                {
                    BrowserSearch();
                    m.Result = 0;
                    return;
                }

                base.WndProc(ref m);
                return;
            case NativeMethods.WM_QUERYENDSESSION:
                m.Result = QueryEndSession is null || QueryEndSession(IsCloseApp(m.LParam)) ? 1 : 0;
                return;
            case NativeMethods.WM_ENDSESSION:
                EndSession?.Invoke(m.WParam != 0, IsCloseApp(m.LParam));
                m.Result = 0;
                return;
            case NativeMethods.WM_DROPFILES:
                OnDropFiles(m.WParam);
                m.Result = 0;
                return;
            case NativeMethods.WM_SYSCOMMAND:
                // In /.SETUP mode SC_MINIMIZE, SC_NEXTWINDOW and SC_PREVWINDOW are swallowed.
                if (SetupMode && IsSetupBlockedCommand((int)m.WParam))
                {
                    m.Result = 0;
                    return;
                }

                base.WndProc(ref m);
                return;
            case NativeMethods.WM_INITMENUPOPUP:
                base.WndProc(ref m);
                if (SetupMode && NativeMethods.HighWord(m.LParam) != 0 && _systemMenu != 0)
                {
                    _ = NativeMethods.EnableMenuItem(_systemMenu, NativeMethods.SC_MINIMIZE, NativeMethods.MF_GRAYED | NativeMethods.MF_DISABLED);
                }

                return;
            default:
                if (m.Msg >= 0xC000 && RegisteredMessage?.Invoke(m.Msg, m.LParam) == true)
                {
                    m.Result = 0;
                    return;
                }

                base.WndProc(ref m);
                return;
        }
    }

    private static bool IsCloseApp(nint lParam) => (lParam & NativeMethods.ENDSESSION_CLOSEAPP) != 0;

    /// <summary><c>((cmd &amp; 0xFFF0) - SC_MINIMIZE) &amp; ~0x30 == 0</c> without <c>SC_MAXIMIZE</c>:
    /// <c>SC_MINIMIZE</c>, <c>SC_NEXTWINDOW</c>, <c>SC_PREVWINDOW</c>.</summary>
    internal static bool IsSetupBlockedCommand(int command)
    {
        var cmd = command & 0xFFF0;
        return cmd != NativeMethods.SC_MAXIMIZE && ((cmd - NativeMethods.SC_MINIMIZE) & ~0x30) == 0;
    }

    /// <summary><c>OnDropFiles</c>: the first file only; none, or an empty name, just finishes the drop.</summary>
    private unsafe void OnDropFiles(nint drop)
    {
        string? path = null;
        try
        {
            if (NativeMethods.DragQueryFile(drop, 0xFFFFFFFF, null, 0) > 0)
            {
                var length = NativeMethods.DragQueryFile(drop, 0, null, 0);
                if (length > 0)
                {
                    var buffer = new char[length + 1];
                    fixed (char* p = buffer)
                    {
                        var copied = NativeMethods.DragQueryFile(drop, 0, p, length + 1);
                        path = new string(p, 0, (int)copied);
                    }
                }
            }
        }
        finally
        {
            NativeMethods.DragFinish(drop);
        }

        if (!string.IsNullOrEmpty(path))
        {
            NativeMethods.SetActiveWindow(Handle);
            FileDropped?.Invoke(path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor?.Dispose();
        }

        base.Dispose(disposing);
    }
}
