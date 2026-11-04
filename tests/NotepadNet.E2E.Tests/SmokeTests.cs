using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// The user paths of the plan (open, edit, save, find, replace, Go To, Word Wrap, zoom, close with
/// unsaved changes) through the real exe, keyboard and dialogs. The tests share the keyboard and
/// the foreground window, so they never run in parallel.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed class SmokeTests
{
    [Fact]
    public void Opens_the_file_named_on_the_command_line()
    {
        using var app = AppSession.StartWithFile("notes.txt", "first line\r\nsecond line", out _);

        Wait.Until(() => app.Text == "first line\r\nsecond line", "the file text");
        app.Title.Should().Be(AppSession.FileTitle("notes.txt") + " - Notepad.NET");
        app.StatusParts.Should().Contain(" UTF-8").And.Contain(" Windows (CRLF)");
    }

    [Fact]
    public void Typing_marks_the_title_and_save_writes_the_file_in_place()
    {
        using var app = AppSession.StartWithFile("a.txt", "abc", out var path);
        Wait.Until(() => app.Text == "abc", "the file text");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        app.Type(" def");
        Wait.Until(() => app.Text == "abc def" && app.Title == "*" + AppSession.FileTitle("a.txt") + " - Notepad.NET", "the typed text and the dirty title", () => $"{app.Text} | {app.Title}");
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);

        Wait.Until(() => app.Title == AppSession.FileTitle("a.txt") + " - Notepad.NET", "the clean title");
        File.ReadAllText(path).Should().Be("abc def");
    }

    [Fact]
    public void Save_of_an_untitled_document_goes_through_save_as()
    {
        using var app = AppSession.Start();
        app.Type("hello");
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);

        var dialog = app.WaitForWindow("Save As");
        var target = Path.Combine(app.Folder, "saved.txt");
        var name = Wait.For(() => dialog.FindFirstDescendant(c => c.ByAutomationId("1001"))?.AsTextBox(), "the file name box");
        name.Text = target;
        Wait.For(() => dialog.FindFirstChild(c => c.ByAutomationId("1"))?.AsButton(), "the Save button").Invoke();

        Wait.Until(() => File.Exists(target) && app.Title == AppSession.FileTitle("saved.txt") + " - Notepad.NET", "the saved file");
        File.ReadAllBytes(target).Should().Equal(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void Find_next_selects_the_match_and_replace_all_replaces_every_one()
    {
        using var app = AppSession.StartWithFile("f.txt", "one two one two one", out _);
        Wait.Until(() => app.Text.Length == 19, "the file text");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        var find = app.WaitForWindow("Find");
        Wait.For(() => find.FindFirstDescendant(c => c.ByAutomationId("1152"))?.AsTextBox(), "Find what").Text = "two";
        Ui.Child(find, "1").AsButton().Invoke();
        Wait.Until(() => app.Selection == (4, 7), "the first match selected");
        Ui.Child(find, "2").AsButton().Invoke();

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        var replace = app.WaitForWindow("Replace");
        Wait.For(() => replace.FindFirstDescendant(c => c.ByAutomationId("1152"))?.AsTextBox(), "Find what").Text = "one";
        Ui.Descendant(replace, "1153").AsTextBox().Text = "1";
        Ui.Child(replace, "1025").AsButton().Invoke();

        Wait.Until(() => app.Text == "1 two 1 two 1", "every match replaced");
        app.Title.Should().StartWith("*");
    }

    [Fact]
    public void Go_to_moves_the_caret_to_the_line_and_the_status_bar_follows()
    {
        using var app = AppSession.StartWithFile("g.txt", "a\r\nb\r\nc\r\nd", out _);
        Wait.Until(() => app.Text.Length == 10, "the file text");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
        var goTo = app.WaitForWindow("Go To Line");
        var line = Wait.For(() => goTo.FindFirstDescendant(c => c.ByControlType(ControlType.Edit))?.AsTextBox(), "the line box");
        line.Text = "3";
        Ui.Child(goTo, "1").AsButton().Invoke();

        Wait.Until(() => app.Selection == (6, 6), "the caret at line 3");
        Wait.Until(() => app.StatusParts.Contains("  Ln 3, Col 1"), "the status bar position", () => string.Join(" | ", app.StatusParts));
    }

    [Fact]
    public void Word_wrap_turns_go_to_off_and_zoom_shows_in_the_status_bar()
    {
        using var app = AppSession.StartWithFile("w.txt", "text", out _);
        Wait.Until(() => app.Text == "text", "the file text");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.ADD);
        Wait.Until(() => app.StatusParts.Contains(" 110%"), "the zoom level");

        var format = Ui.MenuItem(app.Window, "Format");
        format.Expand();
        Wait.For(() => app.Window.FindFirstDescendant(c => c.ByName("Word Wrap"))?.AsMenuItem(), "Word Wrap").Invoke();
        var edit = Ui.MenuItem(app.Window, "Edit");
        edit.Expand();
        var goTo = Wait.For(() => app.Window.FindFirstDescendant(c => c.ByName("Go To...").And(c.ByControlType(ControlType.MenuItem))), "Go To");

        goTo.IsEnabled.Should().BeFalse();
        edit.Collapse();
        app.Text.Should().Be("text", "turning Word Wrap on keeps the text");
    }

    [Fact]
    public void Transparency_from_the_menu_changes_the_window_and_survives_a_restart()
    {
        string folder;
        using (var app = AppSession.StartWithFile("t.txt", "text", out _, keepFolder: true))
        {
            folder = app.Folder;
            Wait.Until(() => app.Text == "text", "the file text");
            app.WindowAlpha.Should().BeNull("a new window is opaque");

            Ui.MenuItem(app.Window, "View").Expand();
            Ui.MenuItem(app.Window, "Transparency").Expand();
            Ui.MenuItem(app.Window, "More Transparent").Invoke();
            Wait.Until(() => app.WindowAlpha == 229, "90% opacity", () => $"{app.WindowAlpha}");

            app.CloseWithKeyboard();
        }

        try
        {
            using var again = AppSession.StartIn(folder, "");
            Wait.Until(() => again.WindowAlpha == 229, "the saved opacity", () => $"{again.WindowAlpha}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Ctrl_shift_wheel_changes_the_transparency_without_zooming()
    {
        using var app = AppSession.StartWithFile("t.txt", "text", out _);
        Wait.Until(() => app.Text == "text", "the file text");
        app.Activate();
        app.MoveCursorIntoWindow();

        using (Keyboard.Pressing(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT))
        {
            AppSession.Wheel(-120);
            Wait.Until(() => app.WindowAlpha == 229, "90% opacity", () => $"{app.WindowAlpha}");
            AppSession.Wheel(120);
            Wait.Until(() => app.WindowAlpha is null, "the opaque window again", () => $"{app.WindowAlpha}");
        }

        app.StatusParts.Should().Contain(" 100%", "the Edit control must not see the Ctrl of the chord");
    }

    [Fact]
    public void Closing_while_the_menu_bar_is_active_exits_without_an_error_dialog()
    {
        using var app = AppSession.StartWithFile("m.txt", "text", out _);
        Wait.Until(() => app.Text == "text", "the file text");

        var view = Ui.MenuItem(app.Window, "View");
        view.Expand();
        view.Collapse();

        app.CloseWithKeyboard();
    }

    [Fact]
    public void New_with_unsaved_changes_asks_and_dont_save_empties_the_document()
    {
        using var app = AppSession.Start();
        app.Type("draft");
        Wait.Until(() => app.Title.StartsWith('*'), "the dirty title");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        var question = app.WaitForWindow("Notepad.NET");
        Wait.For(() => question.FindFirstDescendant(c => c.ByName("Don't Save"))?.AsButton(), "Don't Save").Invoke();

        Wait.Until(() => app.Text.Length == 0, "the empty document", () => app.Text);
        app.Title.Should().Be("Untitled - Notepad.NET");
        Directory.GetFiles(app.Folder, "*.txt").Should().BeEmpty();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopTests
{
    public const string Name = "Desktop";
}
