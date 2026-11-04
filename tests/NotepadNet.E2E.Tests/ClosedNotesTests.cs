using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// Closed notes as a user meets them: close without a question, start again, the text is back.
/// Nothing here may reach a window this test did not start, an installed Notepad.NET with
/// unsaved text above all: windows are closed by handle rather than with Alt+F4, which goes to
/// whatever is in the foreground, and a child process must run the exe under test and be younger
/// than its parent, since a parent id alone can be a reused one.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed class ClosedNotesTests
{
    private static void CloseWindow(AppSession app)
    {
        AppSession.Close(app.Window);
        app.WaitForExit();
    }

    private static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A process killed by the test may hold a file for a moment; the folder is under %TEMP%.
        }
    }

    [Fact]
    public void Closing_asks_nothing_and_the_next_start_brings_the_text_back()
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        try
        {
            using (var app = AppSession.StartIn(folder, ""))
            {
                app.Type("ghi chú chưa lưu");
                Wait.Until(() => app.Text == "ghi chú chưa lưu", "the typed text", () => app.Text);

                CloseWindow(app);
            }

            using (var again = AppSession.StartIn(folder, ""))
            {
                Wait.Until(() => again.Text == "ghi chú chưa lưu", "the restored text", () => again.Text);
                again.Title.Should().Be("*Untitled - Notepad.NET");
                again.Selection.Should().Be((16, 16), "the caret stays where it was");

                // A note survives any number of rounds, with its later edits.
                again.Type(" nữa");
                Wait.Until(() => again.Text == "ghi chú chưa lưu nữa", "the edited text", () => again.Text);
                CloseWindow(again);
            }

            using var third = AppSession.StartIn(folder, "");
            Wait.Until(() => third.Text == "ghi chú chưa lưu nữa", "the text of the second round", () => third.Text);
        }
        finally
        {
            Delete(folder);
        }
    }

    [Fact]
    public void A_named_file_comes_back_with_its_unsaved_changes_and_the_file_untouched()
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
            File.ReadAllText(path).Should().Be("on disk");
            using var again = AppSession.StartIn(folder, "");
            Wait.Until(() => again.Text == "on disk edited", "the restored text", () => again.Text);
            again.Title.Should().Be("*" + AppSession.FileTitle("note.txt") + " - Notepad.NET");

            // Saved means done: nothing comes back after this close.
            again.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            Wait.Until(() => File.ReadAllText(path) == "on disk edited", "the saved file");
            CloseWindow(again);
            using var clean = AppSession.StartIn(folder, "");
            clean.Text.Should().BeEmpty();
            clean.Title.Should().Be("Untitled - Notepad.NET");
        }
        finally
        {
            Delete(folder);
        }
    }

    [Fact]
    public void A_killed_process_loses_at_most_the_last_seconds()
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        try
        {
            using (var app = AppSession.StartIn(folder, ""))
            {
                app.Type("before the crash");
                Wait.Until(() => app.Text == "before the crash", "the typed text", () => app.Text);
                Wait.Until(
                    () => Directory.Exists(Path.Combine(folder, "autosave")) && Directory.GetFiles(Path.Combine(folder, "autosave"), "*.json").Length == 1,
                    "the snapshot");

                // Dispose kills the process: no WM_CLOSE, as in a crash.
            }

            using var again = AppSession.StartIn(folder, "");
            Wait.Until(() => again.Text == "before the crash", "the restored text", () => again.Text);
        }
        finally
        {
            Delete(folder);
        }
    }

    [Fact]
    public void Every_closed_note_gets_a_window_and_a_second_start_gets_none()
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        var started = new List<Process>();
        try
        {
            foreach (var text in new[] { "note one", "note two" })
            {
                // Two windows of their own: a plain start would take the first note back.
                using var app = AppSession.StartIn(folder, "/.NEWWINDOW");
                app.Type(text);
                Wait.Until(() => app.Text == text, "the typed text", () => app.Text);
                CloseWindow(app);
            }

            using var first = AppSession.StartIn(folder, "");
            Wait.Until(() => first.Text == "note one", "the older note", () => first.Text);

            var child = Wait.For(() => ChildOf(first), "the second window");
            started.Add(child);
            using var automation = new UIA3Automation();
            var window = Wait.For(
                () =>
                {
                    child.Refresh();
                    return child.MainWindowHandle == 0 ? null : automation.FromHandle(child.MainWindowHandle).AsWindow();
                },
                "the window of the second note");
            var edit = Wait.For(() => window.FindFirstDescendant(c => c.ByName("Text Editor"))?.AsTextBox(), "its editor");
            Wait.Until(() => edit.Text == "note two", "the newer note", () => edit.Text);
            window.Title.Should().Be("*Untitled - Notepad.NET");

            // Both notes have a window now, so one more start is a plain empty document.
            using var plain = AppSession.StartIn(folder, "");
            plain.Text.Should().BeEmpty();
            ChildOf(first, except: child.Id).Should().BeNull();
            ChildOf(plain).Should().BeNull();
        }
        finally
        {
            foreach (var process in started)
            {
                process.Kill();
                process.WaitForExit();
                process.Dispose();
            }

            Delete(folder);
        }
    }

    [Fact]
    public void New_window_stays_empty_while_a_closed_note_waits_for_a_normal_start()
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        Process? child = null;
        try
        {
            using (var note = AppSession.StartIn(folder, ""))
            {
                note.Type("waiting note");
                Wait.Until(() => note.Text == "waiting note", "the typed text", () => note.Text);
                CloseWindow(note);
            }

            using (var opener = AppSession.StartIn(folder, "/.NEWWINDOW"))
            {
                opener.Text.Should().BeEmpty();
                opener.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_N);
                child = Wait.For(() => ChildOf(opener), "the new window");
                using var automation = new UIA3Automation();
                var window = Wait.For(
                    () =>
                    {
                        child.Refresh();
                        return child.MainWindowHandle == 0 ? null : automation.FromHandle(child.MainWindowHandle).AsWindow();
                    },
                    "its window");
                window.Title.Should().Be("Untitled - Notepad.NET");
            }

            using var normal = AppSession.StartIn(folder, "");
            Wait.Until(() => normal.Text == "waiting note", "the restored text", () => normal.Text);
        }
        finally
        {
            if (child is not null)
            {
                child.Kill();
                child.WaitForExit();
                child.Dispose();
            }

            Delete(folder);
        }
    }

    /// <summary>A process of the exe under test started by <paramref name="parent"/>, or null.</summary>
    private static Process? ChildOf(AppSession parent, int? except = null)
    {
        Process? found = null;
        foreach (var process in Process.GetProcessesByName("Notepad.NET"))
        {
            if (found is null && process.Id != except && IsChild(process, parent))
            {
                found = process;
            }
            else
            {
                process.Dispose();
            }
        }

        return found;
    }

    private static bool IsChild(Process process, AppSession parent)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, AppSession.ExePath, StringComparison.OrdinalIgnoreCase)
                && ProcessParent.Of(process.Handle) == parent.ProcessId
                && process.StartTime >= parent.StartTime;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
