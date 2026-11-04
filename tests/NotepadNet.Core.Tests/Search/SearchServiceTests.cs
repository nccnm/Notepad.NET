using NotepadNet.Core.Commands;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Search;
using NotepadNet.Core.Services;
using NotepadNet.Core.StatusBar;
using NotepadNet.Core.Tests.Fakes;

namespace NotepadNet.Core.Tests.Search;

/// <summary>Find, Find Next/Previous, Replace and Replace All through the commands and the dialog
/// notifications (menu commands, <c>FINDMSGSTRING</c>).</summary>
public sealed class SearchServiceTests
{
    private const string FoundFromTop = " Found next from the top";
    private const string FoundFromBottom = " Found next from the bottom";

    private static Harness WithText(string text, int start = 0, int end = 0)
    {
        var h = new Harness(text);
        h.Editor.SetSelection(start, end);
        h.Editor.Calls.Clear();
        return h;
    }

    private static string Message(Harness h) => h.StatusView.Parts[StatusBarPresenter.MessagePart];

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Find_opens_with_the_settings_of_last_time()
    {
        var h = WithText("text");
        h.Settings.Reverse = true;
        h.Settings.MatchCase = true;
        h.Settings.WrapAround = true;
        h.Settings.SearchString = "old";

        h.Run(CommandId.Find).Should().BeTrue();

        h.FindDialog.Setups.Should().Equal(new FindDialogSetup("old", null, Down: false, MatchCase: true, WrapAround: true));
        h.Settings.SearchChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_always_searches_down_and_brings_the_replacement()
    {
        var h = WithText("text");
        h.Settings.Reverse = true;
        h.Settings.ReplaceString = "new";

        h.Run(CommandId.Replace).Should().BeTrue();

        h.FindDialog.Setups.Should().Equal(new FindDialogSetup("", "new", Down: true, MatchCase: false, WrapAround: false));
        h.FindDialog.IsReplace.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void The_selection_prefills_at_most_127_characters()
    {
        var text = new string('x', 10) + new string('y', 200);
        var h = WithText(text, 10, 210);

        h.Run(CommandId.Find);

        h.Settings.SearchString.Should().Be(new string('y', 127));
        h.FindDialog.Setups[0].FindWhat.Should().HaveLength(127);
        h.Editor.OpenLeases.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void A_short_selection_prefills_and_an_empty_one_keeps_the_old_pattern()
    {
        var h = WithText("hello world", 6, 11);
        h.Run(CommandId.Replace);
        h.Settings.SearchString.Should().Be("world");

        h.Editor.SetSelection(3, 3);
        h.Run(CommandId.Find);
        h.Settings.SearchString.Should().Be("world");
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Opening_a_dialog_closes_the_open_one_first()
    {
        var h = WithText("text");
        h.Run(CommandId.Find);
        h.FindDialog.Down = false;
        h.FindDialog.FindWhat = "typed";

        h.Run(CommandId.Replace);

        h.FindDialog.Calls.Should().Equal("ShowFind", "Close", "ShowReplace");
        // Closing reported the old dialog's state, which the new one starts from.
        h.Settings.SearchString.Should().Be("typed");
        h.Settings.Reverse.Should().BeTrue();
        h.FindDialog.Setups[1].FindWhat.Should().Be("typed");
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Find_next_opens_the_find_dialog_until_find_has_been_used_with_a_pattern()
    {
        var h = WithText("abc abc");
        h.Settings.SearchString = "abc";
        h.Run(CommandId.FindNext);
        h.FindDialog.Calls.Should().Equal("ShowFind");
        h.Editor.Selection.Should().Be(new TextRange(0, 0));

        h.FindDialog.Close();
        h.Settings.SearchString = "";
        h.Run(CommandId.FindPrevious);
        h.FindDialog.Calls.Should().Equal("ShowFind", "Close", "ShowFind");
        h.FindDialog.Setups[1].Down.Should().BeTrue("the dialog takes the saved direction, not the key's");
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void F3_searches_down_and_shift_F3_up_whatever_the_dialog_direction()
    {
        var h = WithText("abc abc abc", 4, 7);
        h.Settings.SearchChanged = true;
        h.Settings.SearchString = "abc";
        h.Settings.Reverse = true;

        h.Run(CommandId.FindNext);
        h.Editor.Selection.Should().Be(new TextRange(8, 11));

        h.Run(CommandId.FindPrevious);
        h.Editor.Selection.Should().Be(new TextRange(4, 7));
        h.Editor.Calls.Should().Contain("ScrollCaret");
        h.Window.WaitCursors.Should().Be(0, "F3 runs without the wait cursor");
        h.FindDialog.Calls.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Wrapping_shows_where_the_search_came_from_in_status_part_0()
    {
        var h = WithText("abc x abc", 6, 9);
        h.Settings.SearchChanged = true;
        h.Settings.SearchString = "abc";
        h.Settings.WrapAround = true;

        h.Run(CommandId.FindNext);
        h.Editor.Selection.Should().Be(new TextRange(0, 3));
        Message(h).Should().Be(FoundFromTop);

        h.Run(CommandId.FindPrevious);
        h.Editor.Selection.Should().Be(new TextRange(6, 9));
        Message(h).Should().Be(FoundFromBottom);

        h.Run(CommandId.FindPrevious);
        h.Editor.Selection.Should().Be(new TextRange(0, 3));
        Message(h).Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Not_found_shows_cannot_find_and_leaves_the_status_bar_and_selection()
    {
        var h = WithText("abc", 1, 2);
        h.Settings.SearchChanged = true;
        h.Settings.SearchString = "zz";
        h.StatusBar.SetMessage(FoundFromTop);

        h.Run(CommandId.FindNext);

        h.FindDialog.NotFound.Should().Equal("Cannot find \"zz\"");
        h.FindDialog.NotFoundOwnedByDialog.Should().Equal(false);
        Message(h).Should().Be(FoundFromTop);
        h.Editor.Selection.Should().Be(new TextRange(1, 2));
        h.Editor.OpenLeases.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Not_found_from_the_dialog_is_owned_by_the_dialog_and_reported_back()
    {
        var h = WithText("abc");
        h.Run(CommandId.Find);
        h.FindDialog.FindWhat = "zz";

        h.FindDialog.Press(FindAction.FindNext).Should().Be(FindResults.None);

        h.FindDialog.NotFoundOwnedByDialog.Should().Equal(true);
        h.Window.WaitCursors.Should().Be(1);
        h.Window.WaitCursorsShowing.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void An_empty_pattern_searches_nothing_and_says_nothing()
    {
        var h = WithText("abc");
        h.Search.FindNext(backward: false).Should().BeFalse();
        h.FindDialog.NotFound.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Every_notification_stores_the_dialog_state_first()
    {
        var h = WithText("Abc abc");
        h.Run(CommandId.Find);
        h.FindDialog.FindWhat = "abc";
        h.FindDialog.Down = false;
        h.FindDialog.MatchCase = true;
        h.FindDialog.WrapAround = true;

        h.FindDialog.Press(FindAction.FindNext).Should().Be(FindResults.Found);

        h.Settings.Reverse.Should().BeTrue();
        h.Settings.MatchCase.Should().BeTrue();
        h.Settings.WrapAround.Should().BeTrue();
        h.Settings.SearchString.Should().Be("abc");
        // Up from the start wraps to the last case-sensitive match.
        h.Editor.Selection.Should().Be(new TextRange(4, 7));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Patterns_from_the_dialog_are_cut_to_127_characters()
    {
        var h = WithText("abc");
        h.Run(CommandId.Replace);
        h.FindDialog.FindWhat = new string('a', 130);
        h.FindDialog.ReplaceWith = new string('b', 130);

        h.FindDialog.Press(FindAction.Terminate);

        h.Settings.SearchString.Should().HaveLength(127);
        h.Settings.ReplaceString.Should().HaveLength(127);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Closing_the_dialog_clears_status_part_0_and_the_find_dialog_keeps_the_replacement()
    {
        var h = WithText("abc");
        h.Settings.ReplaceString = "kept";
        h.Run(CommandId.Find);
        h.StatusBar.SetMessage(FoundFromTop);

        h.FindDialog.Press(FindAction.Terminate).Should().Be(FindResults.None);

        Message(h).Should().BeEmpty();
        h.Settings.ReplaceString.Should().Be("kept");
        h.FindDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void First_replace_only_finds_the_second_replaces_and_finds_the_next()
    {
        var h = WithText("cat cat cat");
        h.Run(CommandId.Replace);
        h.FindDialog.FindWhat = "cat";
        h.FindDialog.ReplaceWith = "dog";

        h.FindDialog.Press(FindAction.Replace).Should().Be(FindResults.Found);
        h.Editor.Text.Should().Be("cat cat cat");
        h.Editor.Selection.Should().Be(new TextRange(0, 3));

        h.FindDialog.Press(FindAction.Replace).Should().Be(FindResults.Replaced | FindResults.Found);
        h.Editor.Text.Should().Be("dog cat cat");
        h.Editor.Selection.Should().Be(new TextRange(4, 7));
        h.Editor.Calls.Should().Contain("ReplaceSelection(dog,True)");
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replacing_the_last_match_reports_replaced_and_cannot_find()
    {
        var h = WithText("cat", 0, 3);
        h.Run(CommandId.Replace);
        h.FindDialog.ReplaceWith = "";

        h.FindDialog.Press(FindAction.Replace).Should().Be(FindResults.Replaced);

        h.Editor.Text.Should().BeEmpty();
        h.FindDialog.NotFound.Should().Equal("Cannot find \"cat\"");
        h.Window.WaitCursorsShowing.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_all_is_one_undoable_replacement_from_the_start()
    {
        var h = WithText("a.b.c.d", 5, 5);
        h.Run(CommandId.Replace);
        h.FindDialog.FindWhat = ".";
        h.FindDialog.ReplaceWith = "--";

        h.FindDialog.Press(FindAction.ReplaceAll).Should().Be(FindResults.ReplacedAll);

        h.Editor.Text.Should().Be("a--b--c--d");
        h.Editor.Calls.Where(c => c.StartsWith("ReplaceSelection", StringComparison.Ordinal)).Should().Equal("ReplaceSelection(a--b--c--d,True)");
        h.Editor.Calls.Should().ContainInOrder("SetSelection(0,0)", "SetSelection(0,7)", "ReplaceSelection(a--b--c--d,True)", "SetSelection(0,0)", "ScrollCaret");
        h.StatusView.Parts[StatusBarPresenter.LineColumnPart].Should().Be("  Ln 1, Col 1");
        h.Window.WaitCursors.Should().Be(1);
        h.Window.WaitCursorsShowing.Should().Be(0);
        h.Editor.OpenLeases.Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_all_without_a_match_changes_nothing_and_says_nothing()
    {
        var h = WithText("abc", 1, 2);
        h.Run(CommandId.Replace);
        h.FindDialog.FindWhat = "zz";

        h.FindDialog.Press(FindAction.ReplaceAll).Should().Be(FindResults.None);

        h.Editor.Text.Should().Be("abc");
        h.Editor.Selection.Should().Be(new TextRange(0, 0));
        h.FindDialog.NotFound.Should().BeEmpty();
        h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_all_of_a_single_match_whose_replacement_contains_it_ends()
    {
        // The original's single-match loop never ends here with Wrap around (parity.md).
        var h = WithText("a");
        h.Run(CommandId.Replace);
        h.FindDialog.FindWhat = "a";
        h.FindDialog.ReplaceWith = "aa";
        h.FindDialog.WrapAround = true;

        h.FindDialog.Press(FindAction.ReplaceAll).Should().Be(FindResults.ReplacedAll);

        h.Editor.Text.Should().Be("aa");
    }

    [Fact]
    public void Replace_all_with_an_empty_pattern_does_nothing()
    {
        var h = WithText("abc");
        h.Settings.Reverse = true;
        h.Search.ReplaceAll().Should().BeFalse();
        h.Settings.Reverse.Should().BeFalse("Replace All forces the direction down and keeps it");
        h.Editor.Text.Should().Be("abc");
        h.Searcher.Calls.Should().Be(0);
    }

    [Fact]
    public void Replace_all_too_large_for_a_string_reports_not_enough_memory()
    {
        var h = WithText("aaaa");
        h.Search.MaxReplaceAllLength = 8;
        h.Settings.SearchString = "a";
        h.Settings.ReplaceString = "bbb";

        h.Search.ReplaceAll().Should().BeFalse();

        h.Editor.Text.Should().Be("aaaa");
        h.Dialogs.Messages.Should().Equal(("Not enough memory available to complete this operation. Quit one or more applications to increase available memory, and then try again.", MessageKind.SystemError));
    }

    [Fact]
    public void A_dialog_that_cannot_open_reports_not_enough_memory()
    {
        var h = WithText("abc");
        h.FindDialog.FailToOpen = true;

        h.Run(CommandId.Find);

        h.Dialogs.Messages.Should().ContainSingle().Which.Kind.Should().Be(MessageKind.SystemError);
        h.FindDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Find_commands_are_grayed_on_empty_text_but_replace_is_not()
    {
        var h = WithText("");
        h.Run(CommandId.Find).Should().BeFalse();
        h.Run(CommandId.FindNext).Should().BeFalse();
        h.Run(CommandId.FindPrevious).Should().BeFalse();
        h.Run(CommandId.Replace).Should().BeTrue();
        h.FindDialog.Calls.Should().Equal("ShowReplace");
    }
}
