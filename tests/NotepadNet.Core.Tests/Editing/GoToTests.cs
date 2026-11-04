using NotepadNet.Core.Commands;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Tests.Fakes;

namespace NotepadNet.Core.Tests.Editing;

/// <summary>Edit > Go To (<c>GoToDlgProc</c>, <c>GoToLine</c>).</summary>
public sealed class GoToTests
{
    private const string BeyondEnd = "The line number is beyond the total number of lines";
    private const string Caption = "Notepad.NET - Goto Line";

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("", 0)]
    [InlineData("12", 12)]
    [InlineData("  42", 42)]
    [InlineData("\t\r\n 7", 7)]
    [InlineData("+5", 5)]
    [InlineData("-5", -5)]
    [InlineData("12abc", 12)]
    [InlineData("abc", 0)]
    [InlineData("- 5", 0)]
    [InlineData("0005", 5)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("2147483648", int.MaxValue)]
    [InlineData("99999999999999999999", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("-99999999999", int.MinValue)]
    [InlineData("١٢", 0)]
    public void Wtol_follows_the_c_runtime(string text, int expected) =>
        CRuntime.Wtol(text).Should().Be(expected);

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Dialog_opens_with_the_displayed_line_of_the_selection_start()
    {
        var h = new Harness("one\ntwo\nthree");
        h.Editor.SetSelection(9, 12);
        h.Dialogs.GoToInputs.Enqueue(null);

        h.Run(CommandId.GoTo).Should().BeTrue();

        h.Dialogs.GoToRequests.Should().ContainSingle().Which.InitialText.Should().Be("3");
        h.Dialogs.GoToRequests[0].ErrorText.Should().Be(BeyondEnd);
        h.Dialogs.GoToRequests[0].ErrorCaption.Should().Be(Caption);
        h.Editor.Selection.Should().Be(new TextRange(9, 12), "Cancel changes nothing");
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Ok_jumps_to_the_start_of_the_line_and_scrolls()
    {
        var h = new Harness("one\ntwo\nthree");
        h.Dialogs.GoToInputs.Enqueue("2");

        h.Run(CommandId.GoTo);

        h.Editor.Selection.Should().Be(new TextRange(4, 4));
        h.Editor.Calls.Should().EndWith(["SetSelection(4,4)", "ScrollCaret"]);
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("99999999999")]
    public void Invalid_lines_show_the_error_and_put_the_line_count_in_the_box(string input)
    {
        var h = new Harness("one\ntwo\nthree");
        h.Dialogs.GoToInputs.Enqueue(input);
        h.Dialogs.GoToInputs.Enqueue("3");

        h.Run(CommandId.GoTo);

        h.Dialogs.GoToErrors.Should().Equal((BeyondEnd, Caption, "3"));
        h.Editor.Selection.Should().Be(new TextRange(8, 8));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Digits_of_other_scripts_are_folded_before_reading()
    {
        var h = new Harness("1\n2\n3");
        h.Dialogs.GoToInputs.Enqueue("٣");

        h.Run(CommandId.GoTo);

        h.Dialogs.GoToErrors.Should().BeEmpty();
        h.Editor.Selection.Should().Be(new TextRange(4, 4));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Only_the_first_99_characters_are_read()
    {
        var h = new Harness("x");
        h.GoTo.Check(new string('0', 98) + "1").Line.Should().Be(1);
        h.GoTo.Check(new string('0', 99) + "1").Line.Should().BeNull();
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Check_uses_displayed_lines_and_the_jump_physical_ones()
    {
        // Only reachable with Word Wrap on, where the command is grayed: line 3 is the second
        // half of the wrapped first line, so it passes the check, but physical line 3 does not
        // exist and the jump does nothing.
        var h = new Harness("abcdef\nxy");
        h.Editor.WordWrap = true;
        h.Editor.WrapColumn = 3;
        h.Editor.SetSelection(4, 4);

        h.GoTo.InitialText().Should().Be("2");
        h.GoTo.Check("3").Line.Should().Be(3);
        h.GoTo.Jump(3);
        h.Editor.Selection.Should().Be(new TextRange(4, 4));

        h.GoTo.Check("2").Line.Should().Be(2);
        h.GoTo.Jump(2);
        h.Editor.Selection.Should().Be(new TextRange(7, 7), "physical line 2 starts after the line break");
        h.CanRun(CommandId.GoTo).Should().BeFalse();
    }
}
