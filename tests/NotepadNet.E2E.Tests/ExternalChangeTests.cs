using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// Another program writes the open file, as a user would meet it: the window reports it with no
/// action in the app, on the real file, the real watcher and the real task dialog.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed class ExternalChangeTests
{
    private const string Question = "Notepad.NET";

    /// <summary>Longer than the wait before a report, so a report that was coming has come.</summary>
    private static readonly TimeSpan NoReportWindow = TimeSpan.FromMilliseconds(1500);

    private static Button Answer(AppSession app, string name)
    {
        var dialog = app.WaitForWindow(Question);
        return Wait.For(() => dialog.FindFirstDescendant(c => c.ByName(name))?.AsButton(), $"the button {name}");
    }

    private static void WaitForNoDialog(AppSession app) => Wait.Until(() => app.FindWindow(Question) is null, "the dialog to close");

    private static void NoDialogAppears(AppSession app)
    {
        Thread.Sleep(NoReportWindow);
        app.FindWindow(Question).Should().BeNull();
    }

    private static void Edit(AppSession app, string file, string typed)
    {
        Wait.Until(() => app.Text == file, "the file text", () => app.Text);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        app.Type(typed);
        Wait.Until(() => app.Text == file + typed, "the typed text", () => app.Text);
    }

    /// <summary>The watch starts just after the load; a write before it is seen by the stamp as well,
    /// but these tests want the watcher, so they give it time.</summary>
    private static void LetTheWatchStart() => Thread.Sleep(NoReportWindow);

    [Fact]
    public void A_change_outside_is_reported_at_once_and_reload_reads_the_new_version()
    {
        using var app = AppSession.StartWithFile("clean.txt", "old", out var path);
        Wait.Until(() => app.Text == "old", "the file text");
        LetTheWatchStart();

        File.WriteAllText(path, "theirs");

        Answer(app, "Reload").Invoke();
        Wait.Until(() => app.Text == "theirs", "the new version", () => app.Text);
        Wait.Until(() => !app.Title.StartsWith('*'), "the clean title");
    }

    [Fact]
    public void A_second_write_while_the_report_is_open_brings_no_second_report()
    {
        using var app = AppSession.StartWithFile("twice.txt", "old", out var path);
        Wait.Until(() => app.Text == "old", "the file text");
        LetTheWatchStart();

        File.WriteAllText(path, "first");
        var keep = Answer(app, "Keep");
        File.WriteAllText(path, "second");
        Thread.Sleep(NoReportWindow);
        keep.Invoke();
        WaitForNoDialog(app);

        NoDialogAppears(app);
        app.Text.Should().Be("old");

        // Answered: the next change is a new one.
        File.WriteAllText(path, "third");
        Answer(app, "Reload").Invoke();
        Wait.Until(() => app.Text == "third", "the newest version", () => app.Text);
    }

    [Fact]
    public void Unsaved_changes_are_kept_by_keep_and_save_still_asks()
    {
        using var app = AppSession.StartWithFile("note.txt", "old", out var path);
        Edit(app, "old", " mine");
        LetTheWatchStart();

        File.WriteAllText(path, "theirs");

        Answer(app, "Keep").Invoke();
        WaitForNoDialog(app);
        app.Text.Should().Be("old mine");
        NoDialogAppears(app);

        // Save: Cancel leaves both versions, Overwrite writes this one.
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Answer(app, "Cancel").Invoke();
        WaitForNoDialog(app);
        File.ReadAllText(path).Should().Be("theirs");
        Wait.Until(() => app.Title.StartsWith('*'), "the dirty title");

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Answer(app, "Overwrite").Invoke();
        Wait.Until(() => File.ReadAllText(path) == "old mine", "the overwritten file");
        Wait.Until(() => !app.Title.StartsWith('*'), "the clean title");
        NoDialogAppears(app);
    }

    [Fact]
    public void Saving_without_a_change_outside_asks_nothing()
    {
        using var app = AppSession.StartWithFile("quiet.txt", "old", out var path);
        Edit(app, "old", " new");
        LetTheWatchStart();

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);

        Wait.Until(() => File.ReadAllText(path) == "old new", "the saved file", () => File.ReadAllText(path));
        Wait.Until(() => !app.Title.StartsWith('*'), "the clean title");
        NoDialogAppears(app);
    }

    [Fact]
    public void A_deleted_file_is_reported_once()
    {
        using var app = AppSession.StartWithFile("gone.txt", "old", out var path);
        Wait.Until(() => app.Text == "old", "the file text");
        LetTheWatchStart();

        File.Delete(path);

        var dialog = app.WaitForWindow(Question);
        Wait.For(() => dialog.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
        WaitForNoDialog(app);
        NoDialogAppears(app);
        app.Text.Should().Be("old");
    }

    [Fact]
    public void A_note_comes_back_and_saving_it_asks_when_the_file_changed_while_closed()
    {
        string folder;
        string path;
        using (var app = AppSession.StartWithFile("closed.txt", "old", out path, keepFolder: true))
        {
            folder = app.Folder;
            Edit(app, "old", " mine");
            AppSession.Close(app.Window);
            app.WaitForExit();
        }

        try
        {
            File.WriteAllText(path, "theirs");
            using var again = AppSession.StartIn(folder, "");
            Wait.Until(() => again.Text == "old mine", "the restored text", () => again.Text);

            Answer(again, "Keep").Invoke();
            WaitForNoDialog(again);

            again.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            Answer(again, "Cancel").Invoke();

            File.ReadAllText(path).Should().Be("theirs");
            Wait.Until(() => again.Title.StartsWith('*'), "the dirty title");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A killed process may hold a file for a moment; the folder is under %TEMP%.
            }
        }
    }
}
