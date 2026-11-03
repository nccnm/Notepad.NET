using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace NotepadClassic.E2E.Tests;

/// <summary>File > Recent Files through the real exe, with the list kept in the settings hive.</summary>
[Collection(DesktopTests.Name)]
public sealed class RecentFilesTests
{
    private const string Recent = "Recent Files";

    /// <summary>File > Recent Files as UIA finds it, with both menus open.</summary>
    private static MenuItem OpenSubmenu(AppSession app)
    {
        Ui.MenuItem(app.Window, "File").Expand();
        var submenu = Ui.MenuItem(app.Window, Recent);
        submenu.Expand();
        return submenu;
    }

    private static void CollapseMenus(AppSession app) => Ui.MenuItem(app.Window, "File").Collapse();

    /// <summary>The names of the list entries, whose access number UIA shows without its <c>&amp;</c>.</summary>
    private static List<string> Entries(AppSession app)
    {
        OpenSubmenu(app);
        var names = app.Window.FindAllDescendants(c => c.ByControlType(ControlType.MenuItem))
            .Select(e => e.Name)
            .Where(n => n.Length > 2 && (char.IsAsciiDigit(n[0]) && n[1] == ' ' || n.StartsWith("10 ", StringComparison.Ordinal)))
            .ToList();
        CollapseMenus(app);
        return names;
    }

    private static string Entry(int number, string path) => $"{number} {path}";

    private static void Close(AppSession app) => app.CloseWithKeyboard();

    [Fact]
    public void A_first_run_has_a_grayed_submenu()
    {
        using var app = AppSession.Start();

        Ui.MenuItem(app.Window, "File").Expand();
        var submenu = Ui.MenuItem(app.Window, Recent);

        submenu.IsEnabled.Should().BeFalse();
        CollapseMenus(app);
    }

    [Fact]
    public void A_file_opened_in_one_run_reopens_from_the_menu_in_the_next()
    {
        string folder;
        string path;
        using (var first = AppSession.StartWithFile("first.txt", "the first text", out path, keepFolder: true))
        {
            folder = first.Folder;
            Wait.Until(() => first.Text == "the first text", "the file text");
            Close(first);
        }

        try
        {
            using var second = AppSession.StartIn(folder, "");
            Wait.Until(() => second.Title.Length > 0, "the title");
            second.Text.Should().BeEmpty();

            OpenSubmenu(second);
            Ui.MenuItem(second.Window, Entry(1, path)).Invoke();

            Wait.Until(() => second.Text == "the first text", "the text from the list", () => second.Text);
            second.Title.Should().StartWith(AppSession.FileTitle("first.txt"));
            Close(second);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_keyboard_reaches_an_entry_through_access_keys()
    {
        string folder;
        string path;
        using (var first = AppSession.StartWithFile("keys.txt", "by keyboard", out path, keepFolder: true))
        {
            folder = first.Folder;
            Wait.Until(() => first.Text == "by keyboard", "the file text");
            Close(first);
        }

        try
        {
            using var second = AppSession.StartIn(folder, "");
            Wait.Until(() => second.Title.Length > 0, "the title");

            second.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_F);
            Keyboard.Type(VirtualKeyShort.KEY_F);
            Keyboard.Type(VirtualKeyShort.KEY_1);

            Wait.Until(() => second.Text == "by keyboard", "the text from the list", () => second.Text);
            Close(second);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Save_as_puts_the_new_name_first_and_the_oldest_stays_below()
    {
        using var app = AppSession.StartWithFile("opened.txt", "text", out var opened);
        Wait.Until(() => app.Text == "text", "the file text");
        var saved = Path.Combine(app.Folder, "saved.txt");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);
        var dialog = app.WaitForWindow("Save As");
        var name = Wait.For(() => dialog.FindFirstDescendant(c => c.ByAutomationId("1001"))?.AsTextBox(), "the file name box");

        // Typed as a user does: over a suggested name that exists, a text set through UIA leaves the
        // dialog on the old name, and it asks to replace that file.
        name.Focus();
        AppSession.TypeUnicode(saved);
        Wait.Until(() => name.Text == saved, "the new file name in the box", () => name.Text);
        Keyboard.Type(VirtualKeyShort.ENTER);
        Wait.Until(() => File.Exists(saved), "the saved file");

        Wait.Until(
            () => Entries(app) is [var one, var two] && one == Entry(1, saved) && two == Entry(2, opened),
            "the saved file first",
            () => string.Join(" / ", Entries(app)));
    }

    [Fact]
    public void A_file_that_is_gone_reports_the_error_and_leaves_the_list()
    {
        using var app = AppSession.StartWithFile("gone.txt", "text", out var path);
        Wait.Until(() => app.Text == "text", "the file text");
        File.Delete(path);

        // The open document's file is gone: the window says so before anything else.
        var notice = app.WaitForWindow("Notepad Classic");
        Wait.For(() => notice.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
        Wait.Until(() => AppSession.Exists(notice) == false, "the notice to close");

        OpenSubmenu(app);
        // A UIA Invoke would wait for the message box to close.
        Keyboard.Type(VirtualKeyShort.KEY_1);

        var box = app.WaitForWindow("Notepad Classic");
        Wait.For(() => box.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
        Wait.Until(() => AppSession.Exists(box) == false, "the message box to close");

        app.Text.Should().Be("text", "a failed open keeps the document");
        Ui.MenuItem(app.Window, "File").Expand();
        Ui.MenuItem(app.Window, Recent).IsEnabled.Should().BeFalse("the only entry left the list");
        CollapseMenus(app);
    }

    [Fact]
    public void Clear_empties_the_list_for_the_next_run_as_well()
    {
        string folder;
        using (var first = AppSession.StartWithFile("clear.txt", "text", out _, keepFolder: true))
        {
            folder = first.Folder;
            Wait.Until(() => first.Text == "text", "the file text");

            OpenSubmenu(first);
            Ui.MenuItem(first.Window, "Clear Recent Files").Invoke();

            Ui.MenuItem(first.Window, "File").Expand();
            Ui.MenuItem(first.Window, Recent).IsEnabled.Should().BeFalse();
            CollapseMenus(first);
            Close(first);
        }

        try
        {
            using var second = AppSession.StartIn(folder, "");
            Ui.MenuItem(second.Window, "File").Expand();
            Ui.MenuItem(second.Window, Recent).IsEnabled.Should().BeFalse();
            CollapseMenus(second);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_unsaved_document_asks_first_and_cancel_keeps_it()
    {
        using var app = AppSession.StartWithFile("dirty.txt", "text", out var path);
        Wait.Until(() => app.Text == "text", "the file text");
        app.Type("more ");

        OpenSubmenu(app);
        Keyboard.Type(VirtualKeyShort.KEY_1);

        var question = app.WaitForWindow("Notepad Classic");
        Wait.For(() => question.FindFirstDescendant(c => c.ByName("Cancel"))?.AsButton(), "Cancel").Invoke();
        Wait.Until(() => AppSession.Exists(question) == false, "the question to close");

        app.Text.Should().Contain("more ");
        Entries(app).Should().Equal(Entry(1, path));
    }
}
