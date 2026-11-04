using NotepadNet.Core.Commands;
using NotepadNet.Core.Services;
using NotepadNet.Core.Tests.Fakes;

namespace NotepadNet.Core.Tests.Commands;

/// <summary>File > Copy Full Path and File > Open Containing Folder.</summary>
public sealed class FilePathCommandsTests
{
    private const string FilePath = @"C:\docs\Note.txt";

    private static Harness Opened()
    {
        var h = new Harness();
        h.OpenSaved(FilePath, "text");
        return h;
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void Both_commands_are_grayed_for_an_untitled_document()
    {
        var h = new Harness();
        h.Editor.Type("a note");

        h.CanRun(CommandId.CopyFullPath).Should().BeFalse();
        h.CanRun(CommandId.OpenContainingFolder).Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_file_saved_for_the_first_time_turns_them_on()
    {
        var h = new Harness();
        h.Editor.Type("a note");
        h.Dialogs.SaveAsAnswers.Enqueue(FileDialogResult.Ok(FilePath, h.State.CurrentEncoding));

        h.Documents.Save();

        h.CanRun(CommandId.CopyFullPath).Should().BeTrue();
        h.CanRun(CommandId.OpenContainingFolder).Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void Copy_full_path_puts_the_path_on_the_clipboard_and_leaves_the_document_alone()
    {
        var h = Opened();
        h.Editor.Type("edited");

        h.Run(CommandId.CopyFullPath).Should().BeTrue();

        h.Clipboard.Written.Should().Equal(FilePath);
        h.Editor.Text.Should().Be("edited");
        h.Documents.IsDirty.Should().BeTrue();
        h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_busy_clipboard_is_reported()
    {
        var h = Opened();
        h.Clipboard.SetTextResult = false;

        h.Run(CommandId.CopyFullPath);

        h.Dialogs.Messages.Should().ContainSingle().Which.Kind.Should().Be(MessageKind.Warning);
        h.Dialogs.Messages[0].Text.Should().Contain("clipboard");
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void Open_containing_folder_shows_the_file()
    {
        var h = Opened();

        h.Run(CommandId.OpenContainingFolder).Should().BeTrue();

        h.Shell.ShownInFolder.Should().Equal(FilePath);
        h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_folder_that_cannot_be_shown_is_reported()
    {
        var h = Opened();
        h.Shell.ShowInFolderResult = false;

        h.Run(CommandId.OpenContainingFolder);

        h.Dialogs.Messages.Should().ContainSingle().Which.Text.Should().Contain(FilePath);
        h.Editor.Text.Should().Be("text");
    }
}
