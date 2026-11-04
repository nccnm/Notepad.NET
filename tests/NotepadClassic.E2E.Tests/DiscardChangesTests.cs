using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace NotepadClassic.E2E.Tests;

/// <summary>
/// The case that asks for File > Discard Changes: unsaved text came back after a restart and is no
/// longer wanted. Each window is closed by handle, like <see cref="ClosedNotesTests"/>.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed class DiscardChangesTests
{
    private const string Item = "Discard Changes";

    private static void CloseWindow(AppSession app)
    {
        AppSession.Close(app.Window);
        app.WaitForExit();
    }

    private static void Discard(AppSession app)
    {
        Ui.MenuItem(app.Window, "File").Expand();
        var item = Ui.MenuItem(app.Window, Item);
        item.IsEnabled.Should().BeTrue();
        item.Invoke();
    }

    [Fact]
    public void A_restored_file_goes_back_to_what_is_on_disk_and_no_note_is_left()
    {
        string folder;
        string path;
        using (var app = AppSession.StartWithFile("note.txt", "on disk", out path, keepFolder: true))
        {
            folder = app.Folder;
            Wait.Until(() => app.Text == "on disk", "the file text");
            app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
            app.Type(" edited");
            Wait.Until(() => app.Text == "on disk edited", "the typed text", () => app.Text);
            CloseWindow(app);
        }

        try
        {
            using (var again = AppSession.StartIn(folder, ""))
            {
                Wait.Until(() => again.Text == "on disk edited", "the restored text", () => again.Text);
                again.Title.Should().StartWith("*");

                Discard(again);

                Wait.Until(() => again.Text == "on disk", "the text of the file", () => again.Text);
                again.Title.Should().Be(AppSession.FileTitle("note.txt") + " - Notepad Classic");
                CloseWindow(again);
            }

            File.ReadAllText(path).Should().Be("on disk");
            using var clean = AppSession.StartIn(folder, "");
            Wait.Until(() => clean.Title.Length > 0, "the title");
            clean.Text.Should().BeEmpty("no note is left");
            clean.Title.Should().Be("Untitled - Notepad Classic");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_restored_untitled_note_goes_back_to_an_empty_page_and_no_note_is_left()
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        try
        {
            using (var app = AppSession.StartIn(folder, ""))
            {
                app.Type("a note");
                Wait.Until(() => app.Text == "a note", "the typed text", () => app.Text);
                CloseWindow(app);
            }

            using (var again = AppSession.StartIn(folder, ""))
            {
                Wait.Until(() => again.Text == "a note", "the restored text", () => again.Text);

                Discard(again);

                Wait.Until(() => again.Text.Length == 0, "an empty page", () => again.Text);
                again.Title.Should().Be("Untitled - Notepad Classic");
                CloseWindow(again);
            }

            using var clean = AppSession.StartIn(folder, "");
            Wait.Until(() => clean.Title.Length > 0, "the title");
            clean.Text.Should().BeEmpty("no note is left");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Ctrl_z_brings_the_edits_back_and_they_stay_a_note()
    {
        string folder;
        using (var app = AppSession.StartWithFile("note.txt", "on disk", out _, keepFolder: true))
        {
            folder = app.Folder;
            Wait.Until(() => app.Text == "on disk", "the file text");
            app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
            app.Type(" edited");
            Wait.Until(() => app.Text == "on disk edited", "the typed text", () => app.Text);

            Discard(app);
            Wait.Until(() => app.Text == "on disk", "the text of the file", () => app.Text);

            app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Wait.Until(() => app.Text == "on disk edited", "the edits", () => app.Text);
            app.Title.Should().StartWith("*");
            CloseWindow(app);
        }

        try
        {
            using var again = AppSession.StartIn(folder, "");
            Wait.Until(() => again.Text == "on disk edited", "the restored text", () => again.Text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_file_that_is_gone_keeps_the_text()
    {
        using var app = AppSession.StartWithFile("note.txt", "on disk", out var path);
        Wait.Until(() => app.Text == "on disk", "the file text");
        app.Type("x");
        Wait.Until(() => app.Text.Contains('x', StringComparison.Ordinal), "the typed text", () => app.Text);
        var edited = app.Text;

        // The window reports the deletion by itself; the watch starts a moment after the load.
        Thread.Sleep(500);
        File.Delete(path);
        PressOk(app);

        // By keyboard: the error is modal, and a UIA Invoke would wait for it to close.
        app.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_F);
        Keyboard.Type(VirtualKeyShort.KEY_D);

        PressOk(app);
        app.Text.Should().Be(edited);
        app.Title.Should().StartWith("*");
    }

    private static void PressOk(AppSession app)
    {
        var dialog = app.WaitForWindow("Notepad Classic");
        Wait.For(() => dialog.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
        Wait.Until(() => app.FindWindow("Notepad Classic") is null, "the dialog to close");
    }
}
