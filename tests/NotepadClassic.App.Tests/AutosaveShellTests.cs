using Microsoft.Extensions.DependencyInjection;
using NotepadClassic.App.Interop;
using NotepadClassic.Core.Autosave;
using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Autosave;
using NotepadClassic.Platform.Services;

namespace NotepadClassic.App.Tests;

/// <summary>
/// Phase f in the real shell: <c>WM_QUERYENDSESSION</c>, <c>WM_ENDSESSION</c> and <c>WM_CLOSE</c>
/// sent to the main window as Restart Manager or a logoff sends them, then a second app restoring
/// from the same folder. The autosave folder is a temporary one.
/// </summary>
public sealed class AutosaveShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-autosave-shell-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static nint Send(TestApp app, int message, nint wParam, nint lParam) =>
        NativeMethods.SendMessage(app.Host.Window.Handle, message, wParam, lParam);

    private static void MakeDirty(TestApp app, string path, string text)
    {
        app.Host.RunCommandLine($"\"{path}\"", untrustedSource: false).Should().Be(StartupResult.Run);
        app.Editor.SetSelection(0, -1);
        app.Editor.ReplaceSelection(text, canUndo: true);
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Startup_registers_the_restart_command_line_with_a_new_id() => Sta.Run(() =>
    {
        using var first = new TestApp();
        using var second = new TestApp();

        first.Restart.CommandLines.Should().Equal("RestartByRestartManager:" + first.Host.Autosave.SessionId);
        AutosaveSession.IsValidId(first.Host.Autosave.SessionId).Should().BeTrue();
        second.Host.Autosave.SessionId.Should().NotBe(first.Host.Autosave.SessionId);
    });

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void A_logoff_keeps_the_text_without_asking() => Sta.Run(() =>
    {
        using var app = new TestApp();
        File.WriteAllText(PathOf("a.txt"), "saved");
        MakeDirty(app, PathOf("a.txt"), "changed");

        Send(app, NativeMethods.WM_QUERYENDSESSION, 0, 0).Should().Be(1);
        Send(app, NativeMethods.WM_ENDSESSION, 1, 0).Should().Be(0);

        app.Dialogs.Messages.Should().BeEmpty();
        app.Host.Window.IsDisposed.Should().BeFalse("WinForms does not close the form on its own");
        File.ReadAllText(Path.Combine(app.AutosaveFolder, app.Host.Autosave.SessionId + ".txt"), System.Text.Encoding.Unicode).Should().Be("changed");
    });

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void Closing_keeps_the_note_and_a_start_without_arguments_brings_it_back() => Sta.Run(() =>
    {
        string id;
        using (var app = new TestApp())
        {
            app.Editor.ReplaceSelection("my note\r\nline 2", canUndo: true);
            app.Editor.SetSelection(3, 7);
            id = app.Host.Autosave.SessionId;

            app.Host.Window.Close();

            app.Dialogs.Messages.Should().BeEmpty();
            app.Host.Window.IsDisposed.Should().BeTrue();
            CopyFolder(app.AutosaveFolder, PathOf("saved"));
        }

        using var again = new TestApp();
        CopyFolder(PathOf("saved"), again.AutosaveFolder);

        again.Host.RunCommandLine("", untrustedSource: false).Should().Be(StartupResult.Run);

        again.Text.Should().Be("my note\r\nline 2");
        again.Host.Window.Text.Should().Be("*Untitled - Notepad Classic");
        again.Editor.Selection.Should().Be(new Core.Editing.TextRange(3, 7));
        File.Exists(Path.Combine(again.AutosaveFolder, id + ".json")).Should().BeFalse("the note moved to the new run's entry");
        File.Exists(Path.Combine(again.AutosaveFolder, again.Host.Autosave.SessionId + ".txt")).Should().BeTrue();
        again.Shell.NextNoteWindows.Should().Be(0);
    });

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void Two_notes_ask_for_one_more_window() => Sta.Run(() =>
    {
        foreach (var text in (string[])["first", "second"])
        {
            using var app = new TestApp();
            app.Editor.ReplaceSelection(text, canUndo: true);
            app.Host.Window.Close();
            CopyFolder(app.AutosaveFolder, PathOf("saved"));
        }

        using var again = new TestApp();
        CopyFolder(PathOf("saved"), again.AutosaveFolder);

        again.Host.RunCommandLine("", untrustedSource: false);

        again.Shell.NextNoteWindows.Should().Be(1);
    });

    [Theory]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    [InlineData("/.NEWWINDOW")]
    [InlineData("\"{0}\"")]
    public void New_window_and_opening_a_file_leave_closed_notes_waiting(string arguments) => Sta.Run(() =>
    {
        File.WriteAllText(PathOf("a.txt"), "the file");
        using (var app = new TestApp())
        {
            app.Editor.ReplaceSelection("my note", canUndo: true);
            app.Host.Window.Close();
            CopyFolder(app.AutosaveFolder, PathOf("saved"));
        }

        using var again = new TestApp();
        CopyFolder(PathOf("saved"), again.AutosaveFolder);

        again.Host.RunCommandLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, arguments, PathOf("a.txt")), untrustedSource: false);

        again.Text.Should().NotContain("my note");
        Directory.GetFiles(again.AutosaveFolder, "*.json").Should().ContainSingle();
    });

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void Two_windows_do_not_take_each_others_notes() => Sta.Run(() =>
    {
        using var first = new TestApp();
        first.Editor.ReplaceSelection("live", canUndo: true);
        first.Host.Autosave.Snapshot();
        using var second = new TestApp(configure: services =>
            services.AddSingleton<IAutosaveStore>(new Win32AutosaveStore(first.AutosaveFolder)));

        second.Host.RunCommandLine("", untrustedSource: false);

        second.Text.Should().BeEmpty();
        Directory.GetFiles(first.AutosaveFolder, "*.txt").Should().ContainSingle();
    });

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void A_command_line_print_asks_on_close() => Sta.Run(() =>
    {
        using var app = new TestApp();
        // A missing file: nothing reaches a printer.
        app.Host.RunCommandLine($"/P \"{PathOf("missing.txt")}\"", untrustedSource: false);
        app.Host.Autosave.AskOnClose.Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void A_clean_document_lets_the_session_end_without_asking() => Sta.Run(() =>
    {
        using var app = new TestApp();

        Send(app, NativeMethods.WM_QUERYENDSESSION, 0, 0).Should().Be(1);

        app.Dialogs.Messages.Should().BeEmpty();
    });

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Restart_manager_close_saves_without_asking_and_a_restart_restores_the_text() => Sta.Run(() =>
    {
        var path = PathOf("doc, with comma.txt");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, (byte)'o', (byte)'l', (byte)'d']);
        string id;
        string folder;
        using (var app = new TestApp())
        {
            MakeDirty(app, path, "new text\r\nline 2");
            id = app.Host.Autosave.SessionId;
            folder = app.AutosaveFolder;

            Send(app, NativeMethods.WM_QUERYENDSESSION, 0, NativeMethods.ENDSESSION_CLOSEAPP).Should().Be(1);
            Send(app, NativeMethods.WM_ENDSESSION, 1, NativeMethods.ENDSESSION_CLOSEAPP).Should().Be(0);
            Send(app, NativeMethods.WM_CLOSE, 0, 0);

            app.Dialogs.Messages.Should().BeEmpty();
            app.Host.Window.IsDisposed.Should().BeTrue("an armed session closes without asking");
            File.Exists(Path.Combine(folder, id + ".json")).Should().BeTrue();
            File.Exists(Path.Combine(folder, id + ".txt")).Should().BeTrue();
            // Keep the files for the second app: TestApp deletes its folder on dispose.
            CopyFolder(folder, PathOf("saved"));
        }

        using var restarted = new TestApp();
        CopyFolder(PathOf("saved"), restarted.AutosaveFolder);

        restarted.Host.RunCommandLine("RestartByRestartManager:" + id, untrustedSource: false).Should().Be(StartupResult.Run);

        restarted.Text.Should().Be("new text\r\nline 2");
        restarted.Host.Window.Text.Should().Be("*" + new Win32FileTitleProvider().GetTitle(path) + " - Notepad Classic");
        restarted.Host.Context.Documents.State.Path.Should().Be(path);
        restarted.Host.Context.Documents.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf8Bom);
        File.Exists(Path.Combine(restarted.AutosaveFolder, id + ".json")).Should().BeFalse("a restored session is deleted");
        File.Exists(Path.Combine(restarted.AutosaveFolder, id + ".txt")).Should().BeFalse("a restored session is deleted");
    });

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void An_open_save_dialog_blocks_even_a_restart_manager_close() => Sta.Run(() =>
    {
        using var app = new TestApp();
        File.WriteAllText(PathOf("a.txt"), "saved");
        MakeDirty(app, PathOf("a.txt"), "changed");
        app.Host.Context.Documents.State.IsSaveDialogOpen = true;

        Send(app, NativeMethods.WM_QUERYENDSESSION, 0, NativeMethods.ENDSESSION_CLOSEAPP).Should().Be(0);

        app.Dialogs.Messages.Should().ContainSingle().Which.Should().StartWith("You cannot shut down or log off Windows");
    });

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
    }
}
