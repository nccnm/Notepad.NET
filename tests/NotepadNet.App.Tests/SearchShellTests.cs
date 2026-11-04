using System.Runtime.InteropServices;
using NotepadNet.App.Interop;
using NotepadNet.App.Shell;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Features;
using NotepadNet.Core.StatusBar;

namespace NotepadNet.App.Tests;

/// <summary>
/// The real COMDLG32 Find/Replace dialog in the shell: keys through <c>IsDialogMessage</c> in the
/// order of the original message loop, <c>FINDMSGSTRING</c> into the search, Go To and Replace
/// All on the real Edit control.
/// </summary>
public sealed partial class SearchShellTests
{
    private const int FindWhatId = 0x480;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkEscape = 0x1B;

    private static KeyDispatcher Keys(TestApp app, KeyModifiers modifiers) =>
        new(app.Host.Registry, () => app.Host.Context, () => app.Host.Window.Handle, () => modifiers)
        {
            DialogMessage = app.Host.Keys.DialogMessage,
        };

    /// <summary>One turn of the message loop: the filters first, then dispatch when they pass.</summary>
    private static bool Deliver(KeyDispatcher keys, nint hwnd, ushort key)
    {
        var m = Message.Create(hwnd, NativeMethods.WM_KEYDOWN, key, 0);
        if (keys.PreFilterMessage(ref m))
        {
            return true;
        }

        NativeMethods.SendMessage(hwnd, m.Msg, m.WParam, m.LParam);
        return false;
    }

    private static TestApp AppWith(string text, int start, int end)
    {
        var app = new TestApp();
        app.Editor.ReplaceSelection(text, canUndo: false);
        app.Editor.SetSelection(start, end);
        return app;
    }

    private static string FindWhat(nint dialog) => Native.Text(Native.GetDlgItem(dialog, FindWhatId));

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Find_dialog_takes_tab_enter_and_escape_through_is_dialog_message() => Sta.Run(() =>
    {
        using var app = AppWith("one two one", 4, 7);
        var ctrl = Keys(app, KeyModifiers.Control);
        var plain = Keys(app, KeyModifiers.None);

        Deliver(ctrl, app.Editor.Host.Handle, 'F').Should().BeTrue();
        var dialog = app.Host.FindDialog.Handle;
        dialog.Should().NotBe(0);
        FindWhat(dialog).Should().Be("two", "the selection is prefilled");
        var findWhat = Native.GetDlgItem(dialog, FindWhatId);
        NativeMethods.GetFocus().Should().Be(findWhat);

        Deliver(plain, findWhat, VkTab).Should().BeTrue();
        var next = NativeMethods.GetFocus();
        next.Should().NotBe(findWhat);
        NativeMethods.IsChild(dialog, next).Should().BeTrue("Tab moves focus inside the dialog");

        Native.SetText(findWhat, "one");
        Deliver(plain, findWhat, VkReturn).Should().BeTrue();
        app.Editor.Selection.Should().Be(new TextRange(8, 11), "Enter presses Find Next");
        app.Host.Context.Settings.SearchString.Should().Be("one");

        Deliver(plain, findWhat, VkEscape).Should().BeTrue();
        app.Host.FindDialog.IsOpen.Should().BeFalse("Esc closes the dialog (FR_DIALOGTERM)");
        Native.IsWindow(dialog).Should().BeFalse();
        app.Host.Window.PartText(StatusBarPresenter.MessagePart).Should().BeEmpty();
    });

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Global_keys_act_in_the_dialog_main_keys_do_not() => Sta.Run(() =>
    {
        using var app = AppWith("one two one", 0, 0);
        var ctrl = Keys(app, KeyModifiers.Control);
        var plain = Keys(app, KeyModifiers.None);
        Deliver(ctrl, app.Editor.Host.Handle, 'F').Should().BeTrue();
        var findWhat = Native.GetDlgItem(app.Host.FindDialog.Handle, FindWhatId);
        Native.SetText(findWhat, "one");
        Deliver(plain, findWhat, VkReturn);
        app.Editor.Selection.Should().Be(new TextRange(0, 3));

        app.Host.Context.Settings.WrapAround = true;
        Deliver(plain, findWhat, VirtualKeys.F3).Should().BeTrue();
        app.Editor.Selection.Should().Be(new TextRange(8, 11), "F3 is in GlobalAcc, translated before the dialog");

        Deliver(ctrl, findWhat, 'A').Should().BeTrue("the dialog consumes the key");
        app.Editor.Selection.Should().Be(new TextRange(8, 11), "Ctrl+A is in MainAcc, which the dialog's keys never reach");

        var dialog = app.Host.FindDialog.Handle;
        Deliver(ctrl, findWhat, 'H').Should().BeTrue();
        app.Host.FindDialog.IsReplace.Should().BeTrue("Ctrl+H closes Find and opens Replace");
        Native.IsWindow(dialog).Should().BeFalse();
        FindWhat(app.Host.FindDialog.Handle).Should().Be("one", "the selection is prefilled again");
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void F3_before_find_was_used_opens_the_find_dialog() => Sta.Run(() =>
    {
        using var app = AppWith("abc", 0, 0);
        app.Host.Context.Settings.SearchString = "abc";

        Deliver(Keys(app, KeyModifiers.None), app.Editor.Host.Handle, VirtualKeys.F3).Should().BeTrue();

        app.Host.FindDialog.IsOpen.Should().BeTrue();
        app.Host.FindDialog.IsReplace.Should().BeFalse();
        app.Editor.Selection.Should().Be(new TextRange(0, 0));
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Browser_search_app_command_opens_find_even_on_empty_text() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Host.Registry.Find(CommandId.Find)!.CanExecute(app.Host.Context).Should().BeFalse();

        NativeMethods.SendMessage(app.Host.Window.Handle, NativeMethods.WM_APPCOMMAND, app.Host.Window.Handle, NativeMethods.APPCOMMAND_BROWSER_SEARCH << 16);

        app.Host.FindDialog.IsOpen.Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Go_to_jumps_on_the_real_edit_control() => Sta.Run(() =>
    {
        using var app = AppWith("one\r\ntwo\r\nthree", 6, 6);
        app.Dialogs.GoToInputs.Enqueue("9");
        app.Dialogs.GoToInputs.Enqueue("3");

        app.Host.Registry.TryExecute(CommandId.GoTo, app.Host.Context).Should().BeTrue();

        app.Dialogs.GoToInitialTexts.Should().Equal("2");
        app.Dialogs.Messages.Should().Equal("The line number is beyond the total number of lines");
        app.Editor.Selection.Should().Be(new TextRange(10, 10));
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Go_to_checks_displayed_lines_and_jumps_to_physical_ones_on_the_real_control() => Sta.Run(() =>
    {
        // With Word Wrap on (Go To is grayed then) a long first line takes several displayed lines.
        using var app = AppWith(new string('x', 4000) + "\r\nend", 0, 0);
        app.Editor.TrySetWordWrap(true).Should().BeTrue();
        app.Editor.LineCount.Should().BeGreaterThan(2);
        app.Host.Registry.Find(CommandId.GoTo)!.CanExecute(app.Host.Context).Should().BeFalse();

        app.Host.Context.GoTo.Check("3").Line.Should().Be(3, "displayed line 3 exists");
        app.Host.Context.GoTo.Jump(2);
        app.Editor.Selection.Should().Be(new TextRange(4002, 4002), "physical line 2 starts after the line break");
        app.Host.Context.GoTo.Jump(3);
        app.Editor.Selection.Should().Be(new TextRange(4002, 4002), "there is no physical line 3");
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_all_is_undone_in_one_step_on_the_real_control() => Sta.Run(() =>
    {
        using var app = AppWith("a.b.c.d", 0, 0);
        app.Editor.Modified = false;
        var settings = app.Host.Context.Settings;
        settings.SearchString = ".";
        settings.ReplaceString = "--";

        app.Host.Context.Search.ReplaceAll().Should().BeTrue();
        app.Text.Should().Be("a--b--c--d");
        app.Editor.Selection.Should().Be(new TextRange(0, 0));
        app.Editor.Modified.Should().BeTrue();

        app.Editor.Undo();
        app.Text.Should().Be("a.b.c.d");
    });

    private static partial class Native
    {
        [LibraryImport("user32.dll")]
        public static partial nint GetDlgItem(nint dialog, int id);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(nint window);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetText(nint window, string text);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
        private static unsafe partial int GetWindowText(nint window, char* text, int count);

        public static unsafe string Text(nint window)
        {
            var buffer = stackalloc char[256];
            var length = GetWindowText(window, buffer, 256);
            return new string(buffer, 0, length);
        }
    }
}
