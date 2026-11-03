using System.Diagnostics;
using NotepadClassic.Core.CommandLine;

namespace NotepadClassic.App.Tests;

/// <summary>A change from outside, through the real watcher and the real timer of the shell, with the
/// message loop pumped by the test and the dialogs scripted.</summary>
public sealed class ExternalChangeShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-external-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static bool PumpUntil(Func<bool> condition, int milliseconds = 10_000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        return condition();
    }

    private TestApp Open(string text, out string path)
    {
        path = Path.Combine(_dir, "a.txt");
        File.WriteAllText(path, text);
        var app = new TestApp();
        app.Host.RunCommandLine($"\"{path}\"", untrustedSource: false).Should().Be(StartupResult.Run);
        return app;
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_write_from_outside_reaches_the_dialog_through_the_real_watcher() => Sta.Run(() =>
    {
        using var app = Open("old", out var path);
        var watched = path;
        PumpUntil(() => app.Host.ExternalChanges.WatchedPath == watched).Should().BeTrue("the watch starts after the load");
        app.Dialogs.ReloadAnswers.Enqueue(true);

        File.WriteAllText(path, "theirs");

        PumpUntil(() => app.Text == "theirs").Should().BeTrue();
        app.Dialogs.ReloadQuestions.Should().ContainSingle().Which.Should().Contain("was changed by another program");
    });

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_change_is_held_back_while_a_modal_dialog_disables_the_window() => Sta.Run(() =>
    {
        using var app = Open("old", out var path);
        var watched = path;
        PumpUntil(() => app.Host.ExternalChanges.WatchedPath == watched).Should().BeTrue();

        app.Host.Window.Enabled = false;
        File.WriteAllText(path, "theirs");
        PumpUntil(() => app.Dialogs.ReloadQuestions.Count > 0, 1000).Should().BeFalse("a modal dialog is open");

        app.Host.Window.Enabled = true;
        PumpUntil(() => app.Dialogs.ReloadQuestions.Count > 0).Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void The_watch_follows_the_document_and_ends_with_it() => Sta.Run(() =>
    {
        using var app = Open("old", out var path);
        var watched = path;
        PumpUntil(() => app.Host.ExternalChanges.WatchedPath == watched).Should().BeTrue();

        app.Host.Context.Documents.New(askSave: false);

        PumpUntil(() => app.Host.ExternalChanges.WatchedPath is null).Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void Closing_the_window_ends_the_watch_without_a_report() => Sta.Run(() =>
    {
        using var app = Open("old", out var path);
        var watched = path;
        PumpUntil(() => app.Host.ExternalChanges.WatchedPath == watched).Should().BeTrue();

        app.Host.Window.Close();
        File.WriteAllText(path, "theirs");
        PumpUntil(() => app.Dialogs.ReloadQuestions.Count > 0, 600).Should().BeFalse();
    });
}
