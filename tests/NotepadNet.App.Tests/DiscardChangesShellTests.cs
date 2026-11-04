using System.Text;
using NotepadNet.Core.CommandLine;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Text;

namespace NotepadNet.App.Tests;

/// <summary>File > Discard Changes on the real Edit control: what a fake cannot show is how the control
/// treats line endings and the modified flag.</summary>
public sealed class DiscardChangesShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-discard-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TestApp Open(string text, out string path)
    {
        path = Path.Combine(_dir, "a.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        var app = new TestApp();
        app.Host.RunCommandLine($"\"{path}\"", untrustedSource: false).Should().Be(StartupResult.Run);
        return app;
    }

    private static void Edit(TestApp app, string text)
    {
        app.Editor.SetSelection(0, -1);
        app.Editor.ReplaceSelection(text, canUndo: true);
    }

    [Theory]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    [InlineData("one\r\ntwo\r\n")]
    [InlineData("one\ntwo\n")]
    [InlineData("one\rtwo\r")]
    [InlineData("one\ntwo\r\nthree\r")]
    public void The_file_comes_back_with_its_own_line_endings_and_the_document_is_clean(string text) => Sta.Run(() =>
    {
        using var app = Open(text, out var path);
        var endOfLine = app.Editor.EndOfLine;
        Edit(app, "edited");
        app.Host.Context.Documents.IsDirty.Should().BeTrue();

        app.Host.Context.Documents.DiscardChanges();

        app.Text.Should().Be(text);
        app.Host.Context.Documents.IsDirty.Should().BeFalse();
        app.Host.Window.Text.Should().NotStartWith("*");
        app.Editor.EndOfLine.Should().Be(endOfLine, "the status bar shows it");

        // Saving now must not rewrite the line endings.
        app.Host.Context.Documents.Save();
        File.ReadAllBytes(path).Should().Equal(Encoding.UTF8.GetBytes(text));
    });

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void Undo_brings_the_edits_back_dirty_so_that_they_become_a_note_again() => Sta.Run(() =>
    {
        using var app = Open("on disk", out _);
        Edit(app, "my edits");
        var documents = app.Host.Context.Documents;
        documents.DiscardChanges();
        app.Text.Should().Be("on disk");
        app.Host.Context.Editor.CanUndo.Should().BeTrue();

        app.Editor.Undo();

        app.Text.Should().Be("my edits");
        documents.IsDirty.Should().BeTrue();
        app.Host.Window.Text.Should().StartWith("*");
    });

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void Undo_brings_an_untitled_note_back_dirty() => Sta.Run(() =>
    {
        using var app = new TestApp();
        Edit(app, "a note");
        var documents = app.Host.Context.Documents;
        documents.DiscardChanges();
        app.Text.Should().BeEmpty();
        documents.IsDirty.Should().BeFalse();

        app.Editor.Undo();

        app.Text.Should().Be("a note");
        documents.IsDirty.Should().BeTrue();
        app.Host.Window.Text.Should().StartWith("*");
    });

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void The_menu_item_is_grayed_until_there_is_something_to_discard() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Host.Window.MainMenuStrip!.Items.Cast<ToolStripMenuItem>().First()
            .DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Tag is CommandId.DiscardChanges);

        app.Host.Menus.UpdateState();
        item.Enabled.Should().BeFalse();

        Edit(app, "text");
        app.Host.Menus.UpdateState();
        item.Enabled.Should().BeTrue();

        item.PerformClick();

        app.Text.Should().BeEmpty();
    });

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_long_text_goes_back_whole() => Sta.Run(() =>
    {
        var text = string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 100_000));
        using var app = Open(text, out _);
        Edit(app, "edited");

        app.Host.Context.Documents.DiscardChanges();

        app.Text.Should().Be(text);
        app.Host.Context.Documents.IsDirty.Should().BeFalse();
    });

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_hand_picked_encoding_survives_and_the_untouched_file_reads_the_same() => Sta.Run(() =>
    {
        var path = Path.Combine(_dir, "ansi.txt");
        File.WriteAllBytes(path, [(byte)'a', 0xE9]);
        using var app = new TestApp();
        var documents = app.Host.Context.Documents;
        documents.Load(path, TextEncodingKind.Ansi);
        var loaded = app.Text;
        Edit(app, "edited");

        documents.DiscardChanges();

        app.Text.Should().Be(loaded);
        documents.State.CurrentEncoding.Should().Be(TextEncodingKind.Ansi);
    });
}
