using System.Runtime.InteropServices;
using System.Text;
using NotepadClassic.App.Editor;
using NotepadClassic.App.Interop;
using NotepadClassic.App.Shell;
using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Features;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.StatusBar;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Text;

namespace NotepadClassic.App.Tests;

/// <summary>The WinForms shell built with fake services: menus, keys, the real Edit control.</summary>
public sealed unsafe partial class ShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-app-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ToolStripMenuItem[] TopMenus(TestApp app) =>
        [.. app.Host.Window.MainMenuStrip!.Items.Cast<ToolStripMenuItem>()];

    private static ToolStripMenuItem Item(TestApp app, CommandId id) =>
        TopMenus(app).SelectMany(Flatten).Single(i => i.Tag is CommandId c && c == id);

    private static IEnumerable<ToolStripMenuItem> Flatten(ToolStripMenuItem menu) =>
        menu.DropDownItems.OfType<ToolStripMenuItem>().SelectMany(i => i.HasDropDownItems ? Flatten(i) : [i]);

    private static string Describe(ToolStripItem item) => item switch
    {
        ToolStripSeparator => "-",
        ToolStripMenuItem { ShortcutKeyDisplayString: { } keys } m => $"{m.Text}\t{keys}",
        _ => item.Text ?? "",
    };

    // ---- Menus ----

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Menu_bar_matches_the_original_resource_layout() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var menus = TopMenus(app);

        menus.Select(m => m.Text).Should().Equal("&File", "&Edit", "F&ormat", "&View", "&Help");
        menus[0].DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal(
            "&New\tCtrl+N", "New &Window\tCtrl+Shift+N", "&Open...\tCtrl+O", "Recent &Files", "&Save\tCtrl+S", "Save &As...\tCtrl+Shift+S",
            "-", "Page Set&up...", "&Print...\tCtrl+P", "-", "E&xit");
        menus[1].DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal(
            "&Undo\tCtrl+Z", "-", "Cu&t\tCtrl+X", "&Copy\tCtrl+C", "&Paste\tCtrl+V", "De&lete\tDel", "-",
            "&Search with Bing...\tCtrl+E", "&Find...\tCtrl+F", "Find &Next\tF3", "Find Pre&vious\tShift+F3",
            "&Replace...\tCtrl+H", "&Go To...\tCtrl+G", "-", "Select &All\tCtrl+A", "Time/&Date\tF5");
        menus[2].DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal("&Word Wrap", "&Font...");
        menus[3].DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal("&Zoom", "&Transparency", "&Always on Top", "&Status Bar");
        ((ToolStripMenuItem)menus[3].DropDownItems[0]).DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal(
            "Zoom &In\tCtrl+Plus", "Zoom &Out\tCtrl+Minus", "&Restore Default Zoom\tCtrl+0");
        ((ToolStripMenuItem)menus[3].DropDownItems[1]).DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal(
            "&More Transparent\tCtrl+Shift+Wheel Down", "&Less Transparent\tCtrl+Shift+Wheel Up", "&Opaque");
        menus[4].DropDownItems.Cast<ToolStripItem>().Select(Describe).Should().Equal(
            "View &Help", "Send &Feedback", "-", "&About Notepad Classic");
    });

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Menu_state_follows_the_rules() => Sta.Run(() =>
    {
        using var app = new TestApp(new AppOptions("https://example.invalid/help", null));

        app.Host.Menus.UpdateState();
        Item(app, CommandId.Cut).Enabled.Should().BeFalse();
        Item(app, CommandId.Paste).Enabled.Should().BeFalse();
        Item(app, CommandId.Find).Enabled.Should().BeFalse();
        Item(app, CommandId.Undo).Enabled.Should().BeFalse();
        Item(app, CommandId.SelectAll).Enabled.Should().BeTrue();
        Item(app, CommandId.StatusBar).Checked.Should().BeTrue();
        Item(app, CommandId.WordWrap).Checked.Should().BeFalse();
        Item(app, CommandId.ViewHelp).Available.Should().BeTrue();
        Item(app, CommandId.SendFeedback).Available.Should().BeFalse("no feedback URL is configured");

        app.Editor.ReplaceSelection("text", canUndo: true);
        app.Editor.SetSelection(0, 2);
        app.Clipboard.HasText = true;
        app.Host.Menus.UpdateState();

        Item(app, CommandId.Cut).Enabled.Should().BeTrue();
        Item(app, CommandId.Paste).Enabled.Should().BeTrue();
        Item(app, CommandId.Find).Enabled.Should().BeTrue();
        Item(app, CommandId.Undo).Enabled.Should().BeTrue();
    });

    // ---- Keys ----

    private static Message KeyDown(nint hwnd, ushort key, bool system = false) =>
        Message.Create(hwnd, system ? NativeMethods.WM_SYSKEYDOWN : NativeMethods.WM_KEYDOWN, key, 0);

    private static KeyDispatcher Dispatcher(TestApp app, KeyModifiers modifiers) =>
        new(app.Host.Registry, () => app.Host.Context, () => app.Host.Window.Handle, () => modifiers);

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Main_keys_only_act_on_the_main_window_global_keys_act_anywhere() => Sta.Run(() =>
    {
        using var app = new TestApp();
        using var other = new Form();
        app.Editor.ReplaceSelection("hello", canUndo: false);
        app.Editor.Zoom = new(150, 100);
        var keys = Dispatcher(app, KeyModifiers.Control);

        var foreign = KeyDown(other.Handle, 'A');
        keys.PreFilterMessage(ref foreign).Should().BeFalse("MainAcc is translated after the modeless dialogs, only for the main window");
        app.Editor.Selection.Should().Be(new TextRange(5, 5));

        var global = KeyDown(other.Handle, VirtualKeys.D0);
        keys.PreFilterMessage(ref global).Should().BeTrue();
        ZoomPolicy.Percent(app.Editor.Zoom).Should().Be(100);

        var main = KeyDown(app.Editor.Host.Handle, 'A');
        keys.PreFilterMessage(ref main).Should().BeTrue();
        app.Editor.Selection.Should().Be(new TextRange(0, 5));
    });

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Keys_of_disabled_commands_are_swallowed_without_running() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Editor.ReplaceSelection("hello", canUndo: false);
        app.Editor.TrySetWordWrap(true).Should().BeTrue();
        var keys = Dispatcher(app, KeyModifiers.Control);
        var executed = false;
        var probe = new CommandRegistry();
        probe.Add(new DelegateCommand(CommandId.GoTo, _ => executed = true, _ => false));
        probe.AddKeyBinding(new(new('G', KeyModifiers.Control), CommandId.GoTo, KeyTier.Global));
        var probeKeys = new KeyDispatcher(probe, () => app.Host.Context, () => app.Host.Window.Handle, () => KeyModifiers.Control);

        var cut = KeyDown(app.Editor.Host.Handle, 'X');
        keys.PreFilterMessage(ref cut).Should().BeTrue();
        var goTo = KeyDown(app.Editor.Host.Handle, 'G');
        probeKeys.PreFilterMessage(ref goTo).Should().BeTrue();

        executed.Should().BeFalse();
        app.Text.Should().Be("hello");
    });

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Unbound_keys_menu_mode_and_other_messages_pass_through() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var keys = Dispatcher(app, KeyModifiers.None);
        var plain = KeyDown(app.Editor.Host.Handle, 'A');
        var up = Message.Create(app.Editor.Host.Handle, 0x0101, 'A', 0);
        var undo = KeyDown(app.Editor.Host.Handle, VirtualKeys.Back, system: true);
        var altKeys = Dispatcher(app, KeyModifiers.Alt);

        keys.PreFilterMessage(ref plain).Should().BeFalse();
        keys.PreFilterMessage(ref up).Should().BeFalse();
        altKeys.PreFilterMessage(ref undo).Should().BeTrue("Alt+Backspace is Undo in MainAcc");

        var f5 = KeyDown(app.Editor.Host.Handle, VirtualKeys.F5);
        keys.MenuActive = true;
        keys.PreFilterMessage(ref f5).Should().BeFalse();
        keys.MenuActive = false;
        keys.DialogMessage = _ => true;
        keys.PreFilterMessage(ref plain).Should().BeTrue("a modeless dialog consumed it");
    });

    // ---- Edit control ----

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Edit_control_is_created_like_the_original() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var host = app.Editor.Host;
        var buffer = stackalloc char[256];

        var length = GetClassName(host.Handle, buffer, 256);
        // WinForms superclasses system classes ("WindowsForms10.EDIT.app.0..."); the window
        // procedure is still the system Edit control's.
        new string(buffer, 0, length).Should().Contain(".EDIT.");
        NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GWLP_ID).Should().Be(EditHost.ControlId);
        ((int)NativeMethods.GetWindowLongPtr(host.Handle, GwlStyle) & ~0x10000000).Should().Be(EditHost.NoWrapStyle & ~0x10000000);
        NativeMethods.GetWindowLongPtr(host.Handle, NativeMethods.GWL_EXSTYLE).Should().Be(0, "no WS_EX_CLIENTEDGE");
        host.AccessibleName.Should().Be("Text Editor");
        var extended = (int)host.Send(NativeMethods.EM_GETEXTENDEDSTYLE);
        (extended & 0x1F).Should().Be(0x1F, "ALLOWEOL_CR|LF, CONVERT_EOL_ON_PASTE, 0x8 and ZOOMABLE");
    });

    private const int GwlStyle = -16;

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint hWnd, char* lpClassName, int nMaxCount);

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Buffers_are_handed_over_with_EM_SETHANDLE() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Editor.ReplaceSelection("old", canUndo: true);
        var buffer = (LocalTextBuffer)LocalTextBufferFactory.Instance.Allocate(5);
        "a\nb\nc\0".AsSpan().CopyTo(buffer.Chars);

        app.Editor.LoadBuffer(buffer, 5).Should().BeTrue();

        app.Text.Should().Be("a\nb\nc");
        app.Editor.TextLength.Should().Be(5);
        app.Editor.Modified.Should().BeFalse();
        app.Editor.CanUndo.Should().BeFalse("EM_SETHANDLE clears the undo history");
        app.Editor.EndOfLine.Should().Be(EndOfLineKind.Lf);
        app.Editor.FileLineFromChar(4).Should().Be(2);
        app.Editor.FileLineIndex(1).Should().Be(2);
    });

    [Fact]
    public void Managed_buffers_go_through_WM_SETTEXT() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var buffer = ManagedTextBufferFactory.Instance.Allocate(2);
        "hi\0".AsSpan().CopyTo(buffer.Chars);

        app.Editor.LoadBuffer(buffer, 2).Should().BeTrue();

        app.Text.Should().Be("hi");
    });

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Word_wrap_recreates_the_control_keeping_text_zoom_and_modified_but_not_undo() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var old = app.Editor.Host;
        app.Editor.ReplaceSelection(new string('x', 300) + "\r\nend", canUndo: true);
        app.Editor.Zoom = new(150, 100);
        app.Editor.SetSelection(3, 7);

        app.Editor.TrySetWordWrap(true).Should().BeTrue();

        var host = app.Editor.Host;
        host.Should().NotBeSameAs(old);
        old.IsDisposed.Should().BeTrue();
        ((int)NativeMethods.GetWindowLongPtr(host.Handle, GwlStyle) & 0x00100000).Should().Be(0, "no WS_HSCROLL with word wrap");
        app.Editor.WordWrap.Should().BeTrue();
        app.Text.Should().Be(new string('x', 300) + "\r\nend");
        app.Editor.Modified.Should().BeTrue();
        app.Editor.Zoom.Should().Be(new ZoomRatio(150, 100));
        app.Editor.CanUndo.Should().BeFalse();
        app.Editor.Selection.Should().Be(new TextRange(0, 0), "the selection is lost");
        app.Editor.FileLineFromChar(305).Should().Be(1, "physical lines ignore wrap breaks");

        app.Editor.SetSelection(305, 305);
        app.Editor.TrySetWordWrap(false).Should().BeTrue();
        app.Editor.TrySetWordWrap(false).Should().BeTrue();
        app.Editor.Selection.Should().Be(new TextRange(0, 0));
        NativeMethods.GetWindowLongPtr(app.Editor.Host.Handle, NativeMethods.GWLP_ID).Should().Be(EditHost.ControlId);
    });

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Editor_raises_text_changed_and_clears() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var changes = 0;
        app.Editor.TextChanged += (_, _) => changes++;

        app.Editor.ReplaceSelection("abc", canUndo: true);
        app.Editor.Undo();
        app.Editor.Clear();

        changes.Should().BeGreaterThanOrEqualTo(2);
        app.Text.Should().BeEmpty();
        app.Editor.SetFont(new FontSpec("Consolas", 120)).Should().BeTrue();
        app.Editor.HasFocusInEditorOrMain.Should().BeFalse("the window is not shown");
    });

    // ---- Document flows through the real shell ----

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Typing_saving_and_reopening_through_the_shell() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var path = Path.Combine(_dir, "Note.txt");
        app.Editor.ReplaceSelection("xin chào\r\n", canUndo: true);
        app.Host.Window.Text.Should().Be("*Untitled - Notepad Classic");
        app.Dialogs.SaveAs.Enqueue(FileDialogResult.Ok(path, TextEncodingKind.Utf16LE));

        app.Host.Registry.TryExecute(CommandId.Save, app.Host.Context).Should().BeTrue();

        File.ReadAllBytes(path).Should().Equal(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("xin chào\r\n")));
        app.Host.Window.Text.Should().StartWith("Note").And.EndWith(" - Notepad Classic");
        app.Host.Window.PartText(StatusBarPresenter.EncodingPart).Should().Be(" UTF-16 LE");

        app.Dialogs.Open.Enqueue(FileDialogResult.Ok(path, TextEncodingKind.Detect));
        app.Host.Registry.TryExecute(CommandId.Open, app.Host.Context);

        app.Text.Should().Be("xin chào\r\n");
        app.Shell.Recent.Should().Equal(path);
    });

    [Fact]
    [Trait("Doc", "parity.md#encoding-va-nap-file")]
    public void Log_files_get_a_stamp_when_opened() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var path = Path.Combine(_dir, "log.txt");
        File.WriteAllText(path, ".LOG\r\nentry");
        app.Dialogs.Open.Enqueue(FileDialogResult.Ok(path, TextEncodingKind.Detect));

        app.Host.Registry.TryExecute(CommandId.Open, app.Host.Context);

        app.Text.Should().Be(".LOG\r\nentry\r\n2:44 PM 10/29/2026\r\n");
        app.Editor.CanUndo.Should().BeTrue();
        app.Host.Window.Text.Should().StartWith("*");
    });

    // ---- Window ----

    [Fact]
    [Trait("Doc", "parity.md#tieu-de-status-bar")]
    public void Layout_places_menu_editor_and_status_parts() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var window = app.Host.Window;
        window.ClientSize = new Size(1000, 600);
        window.LayoutChildren();
        var dpi = window.MonitorDpiForLayout;

        var windowDpi = (uint)NativeMethods.GetDpiForWindow(window.Handle);
        var menu = window.MainMenuStrip!;
        menu.Bounds.Should().Be(new Rectangle(0, 0, 1000, NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYMENU, windowDpi)));

        var host = app.Editor.Host;
        host.Top.Should().Be(menu.Height);
        host.Width.Should().Be(1000);
        var status = window.Controls.OfType<StatusStrip>().Single();
        status.Bounds.Should().Be(new Rectangle(0, 600 - NativeStatusBarHeight(window.Handle), 1000, NativeStatusBarHeight(window.Handle)));
        host.Bottom.Should().Be(status.Top);

        // Every label after the first starts one pixel left of its part edge, where the native
        // status bar draws the separator.
        var edges = StatusBarLayout.RightEdges(1000, dpi);
        window.PartWidth(0).Should().Be(edges[0] - 1);
        window.PartWidth(1).Should().Be(Win32Math.Scale(140, dpi));
        window.PartWidth(4).Should().Be(Win32Math.Scale(120, dpi) + 1);

        app.Host.Registry.TryExecute(CommandId.StatusBar, app.Host.Context);
        host.Bottom.Should().Be(600, "a hidden status bar leaves the space to the editor");
        app.Host.CaretHookInstalled.Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Native_resizes_lay_out_with_the_new_client_size() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var window = app.Host.Window;
        var host = app.Editor.Host;
        foreach (var (width, height) in new[] { (900, 700), (1200, 800), (700, 500) })
        {
            // A size set from outside WinForms, as a drag or a DPI change does.
            SetWindowPos(window.Handle, 0, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            NativeMethods.GetClientRect(window.Handle, out var client).Should().BeTrue();
            NativeMethods.GetWindowRect(host.Handle, out var edit).Should().BeTrue();
            (edit.Right - edit.Left).Should().Be(client.Right, $"the editor follows a {width}x{height} window");
            (edit.Bottom - edit.Top).Should().Be(client.Bottom - window.MainMenuStrip!.Height - NativeStatusBarHeight(window.Handle));
        }
    });

    [Fact]
    [Trait("Doc", "parity.md#so-sanh-hinh")]
    public void Menu_items_take_the_geometry_of_a_win32_menu_bar_with_the_same_texts() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var window = app.Host.Window;
        var menus = TopMenus(app);
        NativeMethods.GetWindowRect(window.Handle, out var rect);

        var native = NativeBarMetrics.MenuItems(
            [.. menus.Select(m => m.Text!)], Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));

        native.Should().NotBeNull();
        native![0].Left.Should().Be(0);
        for (var i = 0; i < menus.Length; i++)
        {
            menus[i].Bounds.Should().Be(new Rectangle(native[i].Left, 0, native[i].Width, native[i].Height), menus[i].Text);
            if (i > 0)
            {
                native[i].Left.Should().Be(native[i - 1].Left + native[i - 1].Width, "Win32 menu bar items touch");
            }
        }

        var dpi = (int)NativeMethods.GetDpiForWindow(window.Handle);
        native[0].Height.Should().Be(NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYMENU, (uint)dpi) - 1);
    });

    [Fact]
    [Trait("Doc", "parity.md#so-sanh-hinh")]
    public void Bar_fonts_come_from_the_non_client_metrics_of_the_window_dpi() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var window = app.Host.Window;
        var (menuFont, statusFont) = NativeBarMetrics.Fonts((int)NativeMethods.GetDpiForWindow(window.Handle))!.Value;
        using (menuFont)
        using (statusFont)
        {
            var menu = window.MainMenuStrip!;
            var status = window.Controls.OfType<StatusStrip>().Single();
            (menu.Font.Name, menu.Font.Size).Should().Be((menuFont.Name, menuFont.Size));
            (status.Font.Name, status.Font.Size).Should().Be((statusFont.Name, statusFont.Size));
        }
    });

    [Fact]
    [Trait("Doc", "parity.md#so-sanh-hinh")]
    public void Status_text_origin_is_measured_on_a_real_status_bar_and_the_gripper_shows() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var status = app.Host.Window.Controls.OfType<StatusStrip>().Single();
        var renderer = (StatusBarRenderer)status.Renderer;

        renderer.TextOrigin.Should().NotBeNull();
        var origin = renderer.TextOrigin!.Value;
        origin.X.Should().BeInRange(0, 10, "comctl32 starts the text near the part's left edge");
        origin.Y.Should().BeInRange(0, status.Height / 2);
        status.SizingGrip.Should().BeTrue("comctl32 draws the gripper under a sizable parent");
    });

    [Fact]
    [Trait("Doc", "parity.md#so-sanh-hinh")]
    public void Menu_bar_underlines_access_keys_only_in_keyboard_mode() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var menu = app.Host.Window.MainMenuStrip!;
        var renderer = (MenuBarRenderer)menu.Renderer;
        using var hidden = Snapshot(menu);
        renderer.KeyboardMode = true;
        using var shown = Snapshot(menu);
        renderer.KeyboardMode = false;
        using var hiddenAgain = Snapshot(menu);

        Ink(hiddenAgain).Should().Be(Ink(hidden));
        if (!SystemInformation.MenuAccessKeysUnderlined)
        {
            Ink(shown).Should().BeGreaterThan(Ink(hidden), "the underlines add ink");
        }

        static Bitmap Snapshot(Control control)
        {
            var bitmap = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
            return bitmap;
        }

        static int Ink(Bitmap bitmap)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    count += c.R + c.G + c.B < 300 ? 1 : 0;
                }
            }

            return count;
        }
    });

    private const uint SWP_NOMOVE = 0x2;
    private const uint SWP_NOZORDER = 0x4;
    private const uint SWP_NOACTIVATE = 0x10;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    /// <summary>Height comctl32 gives a real status window created on <paramref name="parent"/>.</summary>
    private static int NativeStatusBarHeight(nint parent)
    {
        var probe = NativeMethods.CreateWindowEx(0, "msctls_statusbar32", null, NativeMethods.WS_CHILD, 0, 0, 0, 0, parent, 0, 0, 0);
        probe.Should().NotBe(0);
        try
        {
            NativeMethods.SendMessage(probe, NativeMethods.WM_SIZE, 0, 0);
            NativeMethods.GetWindowRect(probe, out var rect).Should().BeTrue();
            return rect.Bottom - rect.Top;
        }
        finally
        {
            NativeMethods.DestroyWindow(probe);
        }
    }

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Minimum_track_width_is_230_logical_pixels() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var info = stackalloc int[10];

        NativeMethods.SendMessage(app.Host.Window.Handle, NativeMethods.WM_GETMINMAXINFO, 0, (nint)info);

        info[6].Should().Be(Win32Math.Scale(230, (int)NativeMethods.GetDpiForWindow(app.Host.Window.Handle)));
    });

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Closing_asks_to_save_and_cancel_keeps_the_window() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Editor.ReplaceSelection("unsaved", canUndo: true);
        app.Dialogs.SaveChanges.Enqueue(SaveChangesChoice.Cancel);

        app.Host.Window.Close();

        app.Host.Window.IsDisposed.Should().BeFalse();
        app.Dialogs.Messages.Should().Equal("Do you want to save changes to Untitled?");
    });
}
