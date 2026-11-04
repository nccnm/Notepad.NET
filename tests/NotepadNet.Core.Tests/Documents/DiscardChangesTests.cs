using System.Text;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Editing;
using NotepadNet.Core.IO;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Documents;

/// <summary>File > Discard Changes.</summary>
public sealed class DiscardChangesTests
{
    private const string FilePath = @"C:\docs\Note.txt";

    private static Harness Edited(string onDisk = "on disk", string edited = "on disk, edited")
    {
        var h = new Harness();
        h.OpenSaved(FilePath, onDisk);
        h.Editor.Type(edited);
        return h;
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_named_document_goes_back_to_its_file_in_one_undoable_step_without_a_question()
    {
        var h = Edited();

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("on disk");
        h.Documents.IsDirty.Should().BeFalse();
        h.Window.Title.Should().Be("Note.txt - Notepad.NET");
        h.State.Path.Should().Be(FilePath);
        h.Editor.Calls.Should().Contain("ReplaceSelection(on disk,True)").And.NotContain("Clear");
        h.Dialogs.SaveChangesQuestions.Should().BeEmpty();
        h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void An_untitled_document_goes_back_to_an_empty_page_in_one_undoable_step()
    {
        var h = new Harness();
        h.State.CurrentEncoding = TextEncodingKind.Utf16BE;
        h.Editor.Type("a note");

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().BeEmpty();
        h.Documents.IsDirty.Should().BeFalse();
        h.Window.Title.Should().Be("Untitled - Notepad.NET");
        h.State.IsUntitled.Should().BeTrue();
        h.State.CurrentEncoding.Should().Be(h.State.DefaultEncoding);
        h.Editor.Calls.Should().Contain("ReplaceSelection(,True)").And.NotContain("Clear");
        h.Dialogs.SaveChangesQuestions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void The_caret_stays_where_it_was()
    {
        var h = Edited();
        h.Editor.SetSelection(2, 4);

        h.Documents.DiscardChanges();

        h.Editor.Selection.Should().Be(new TextRange(2, 4));
        h.Editor.Calls[^1].Should().Be("ScrollCaret");
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_clean_document_is_left_alone_and_the_command_is_grayed()
    {
        var h = new Harness();
        h.OpenSaved(FilePath, "on disk");
        h.Editor.Calls.Clear();

        h.CanRun(CommandId.DiscardChanges).Should().BeFalse();
        h.Documents.DiscardChanges();

        h.Editor.Calls.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void The_command_runs_while_the_document_is_dirty()
    {
        var h = Edited();

        h.CanRun(CommandId.DiscardChanges).Should().BeTrue();
        h.Run(CommandId.DiscardChanges).Should().BeTrue();

        h.Editor.Text.Should().Be("on disk");
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_snapshot_after_it_deletes_the_note()
    {
        var h = Edited();
        h.Autosave.Snapshot();
        h.Autosave.WantsSnapshot.Should().BeTrue();
        h.AutosaveStore.Manifests.Should().ContainKey(Harness.SessionId);

        h.Documents.DiscardChanges();
        h.Autosave.Snapshot();

        h.AutosaveStore.Manifests.Should().BeEmpty();
        h.AutosaveStore.TextOf(Harness.SessionId).Should().BeNull();
        h.Autosave.WantsSnapshot.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_file_that_cannot_be_read_keeps_the_text_and_says_so()
    {
        var h = Edited();
        h.Files.ReadErrors[FilePath] = FileErrors.AccessDenied;

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("on disk, edited");
        h.Documents.IsDirty.Should().BeTrue();
        h.Dialogs.Messages.Should().ContainSingle();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_file_that_is_gone_keeps_the_text_and_says_so()
    {
        var h = Edited();
        h.Files.Files.Remove(FilePath);

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("on disk, edited");
        h.Documents.IsDirty.Should().BeTrue();
        h.State.Path.Should().Be(FilePath);
        h.Dialogs.Messages.Should().ContainSingle();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_file_the_policy_refuses_keeps_the_text()
    {
        var h = Edited();
        h.AccessPolicy.Allow = false;

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("on disk, edited");
        h.Documents.IsDirty.Should().BeTrue();
    }

    [Theory]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    [InlineData(false)]
    [InlineData(true)]
    public void An_editor_out_of_memory_leaves_the_document_dirty_and_its_note_alone(bool untitled)
    {
        var h = untitled ? new Harness() : Edited();
        if (untitled)
        {
            h.Editor.Type("a note");
        }

        var encoding = h.State.CurrentEncoding = TextEncodingKind.Utf16BE;
        h.Autosave.Snapshot();
        h.Editor.FailNextReplace = true;

        h.Documents.DiscardChanges();
        h.Autosave.Snapshot();

        h.Documents.IsDirty.Should().BeTrue();
        h.State.CurrentEncoding.Should().Be(encoding);
        h.AutosaveStore.Manifests.Should().ContainKey(Harness.SessionId);
        h.Dialogs.Messages.Should().ContainSingle().Which.Text.Should().StartWith("Not enough memory");
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_file_too_big_for_memory_keeps_the_text_path_and_note()
    {
        var buffers = new FailingBuffers();
        var h = new Harness(buffers: buffers);
        h.OpenSaved(FilePath, "on disk");
        h.Editor.Type("edited");
        h.Autosave.Snapshot();
        buffers.Fail = true;

        h.Documents.DiscardChanges();
        h.Autosave.Snapshot();

        h.Editor.Text.Should().Be("edited");
        h.State.Path.Should().Be(FilePath);
        h.Documents.IsDirty.Should().BeTrue();
        h.AutosaveStore.Manifests.Should().ContainKey(Harness.SessionId);
        h.Dialogs.Messages.Should().ContainSingle();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_log_file_gets_no_new_stamp_and_stays_clean()
    {
        var h = Edited(".LOG\r\nfirst", ".LOG\r\nfirst, edited");
        var original = h.Editor.Text;

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be(".LOG\r\nfirst").And.NotBe(original);
        h.Documents.IsDirty.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void An_unchanged_file_keeps_the_encoding_of_the_document()
    {
        var h = new Harness();
        var bytes = new byte[] { (byte)'a', 0xE9 };
        h.Files.Files[FilePath] = bytes;
        h.Documents.Load(FilePath, TextEncodingKind.Ansi).Should().Be(Core.Documents.LoadOutcome.Loaded);
        h.State.CurrentEncoding.Should().Be(TextEncodingKind.Ansi);
        h.Editor.Type("edited");

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("aé");
        h.State.CurrentEncoding.Should().Be(TextEncodingKind.Ansi);
    }

    [Fact]
    [Trait("Doc", "parity.md#bo-thay-doi")]
    public void A_file_changed_outside_is_read_as_a_fresh_open_and_save_no_longer_asks()
    {
        var h = new Harness();
        h.Files.Files[FilePath] = Encoding.ASCII.GetBytes("old");
        h.Documents.Load(FilePath, TextEncodingKind.Ansi);
        h.Editor.Type("mine");
        h.Files.Files[FilePath] = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("theirs")];

        h.Documents.DiscardChanges();

        h.Editor.Text.Should().Be("theirs");
        h.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16LE);

        h.Editor.Type("next edit");
        h.Documents.Save();
        h.Dialogs.FileChangedQuestions.Should().BeEmpty();
    }

    private sealed class FailingBuffers : ITextBufferFactory
    {
        public bool Fail { get; set; }

        /// <summary>Past the longest array there can be, the runtime refuses at once.</summary>
        public ITextBuffer Allocate(int length) =>
            ManagedTextBufferFactory.Instance.Allocate(Fail ? int.MaxValue - 1 : length);
    }
}
