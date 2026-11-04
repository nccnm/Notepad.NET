using System.Buffers.Binary;
using System.Text;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Platform.Services;
using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Services;

/// <summary>The Go To dialog template and the real dialog driven from a second thread
/// (<c>GoToDlgProc</c>).</summary>
public sealed partial class GoToDialogTests
{
    private const string ErrorText = "The line number is beyond the total number of lines";
    private const string ErrorCaption = "Notepad.NET - Goto Line";
    
    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Template_header_and_items_follow_the_dlgtemplateex_layout()
    {
        var bytes = Win32GoToDialog.BuildTemplate(ResxStrings.Instance);
        var span = bytes.AsSpan();
        BinaryPrimitives.ReadUInt16LittleEndian(span).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(span[2..]).Should().Be(0xFFFF);
        BinaryPrimitives.ReadUInt32LittleEndian(span[12..]).Should().Be(0x80C800C0, "WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT");
        BinaryPrimitives.ReadUInt16LittleEndian(span[16..]).Should().Be(4);
        Shorts(span[18..], 4).Should().Equal(0, 0, 166, 60);

        var offset = 26 + 4;                                   // empty menu and class
        ReadString(span, ref offset).Should().Be("Go To Line");
        BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]).Should().Be(8);
        offset += 6;
        ReadString(span, ref offset).Should().Be("MS Shell Dlg");

        (uint Id, ushort Class, string Text, uint Style, short[] Bounds)[] expected =
        [
            (0x4B0, 0x82, "&Line number:", 0x50020000, [7, 7, 115, 8]),
            (0x102, 0x81, "", 0x50812080, [7, 18, 151, 14]),
            (1, 0x80, "Go To", 0x50010001, [55, 39, 50, 14]),
            (2, 0x80, "Cancel", 0x50010000, [109, 39, 50, 14]),
        ];
        foreach (var item in expected)
        {
            offset = (offset + 3) & ~3;
            BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]).Should().Be(item.Style);
            Shorts(span[(offset + 12)..], 4).Should().Equal(item.Bounds);
            BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 20)..]).Should().Be(item.Id);
            BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 24)..]).Should().Be(0xFFFF);
            BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 26)..]).Should().Be(item.Class);
            offset += 28;
            ReadString(span, ref offset).Should().Be(item.Text);
            BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]).Should().Be(0);
            offset += 2;
        }

        offset.Should().Be(bytes.Length);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Real_dialog_shows_the_initial_line_rejects_letters_and_reports_errors() => StaThread.Run(() =>
    {
        List<string> inputs = [];
        var request = new GoToDialogRequest(
            "7",
            input =>
            {
                inputs.Add(input);
                var line = CRuntime.Wtol(new Win32DigitFolder().FoldDigits(input));
                return line is >= 1 and <= 3 ? GoToInputCheck.Accept(line) : GoToInputCheck.Reject("3");
            },
            ErrorText,
            ErrorCaption);
        var observed = new Dictionary<string, string>();
        var driver = DialogDriver.Run("Go To Line", (thread, dialog) =>
        {
            var edit = Native.GetDlgItem(dialog, Win32GoToDialog.LineNumberId);
            observed["initial"] = Native.Text(edit);
            observed["focus"] = Native.FocusOf(dialog) == edit ? "edit" : "other";
            Native.SendMessage(edit, Native.EM_GETSEL, out var start, out var end);
            observed["selection"] = $"{start},{end}";

            Native.SendMessage(edit, Native.WM_SETTEXT, 0, "");
            Native.SendMessage(edit, Native.WM_CHAR, 'a', 0);
            Native.SendMessage(edit, Native.WM_CHAR, '-', 0);
            Native.SendMessage(edit, Native.WM_CHAR, '٣', 0);
            observed["typed"] = Native.Text(edit);

            Native.SendMessage(edit, Native.WM_SETTEXT, 0, "9");
            Native.PostMessage(dialog, Native.WM_COMMAND, 1, 0);
            var box = DialogWait.ForWindow(thread, ErrorCaption);
            // The box is found as soon as it exists; wait until it shows its text and runs its loop.
            DialogWait.Until(() => Native.IsWindowVisible(box) && Native.Text(Native.GetDlgItem(box, 0xFFFF)).Length > 0);
            observed["error"] = Native.Text(Native.GetDlgItem(box, 0xFFFF));
            observed["errorOwner"] = Native.GetWindow(box, Native.GW_OWNER) == dialog ? "dialog" : "other";
            Native.PostMessage(box, Native.WM_CLOSE, 0, 0);
            DialogWait.Until(() => !Native.IsWindow(box));
            DialogWait.Until(() => Native.Text(edit) == "3");
            observed["afterError"] = Native.Text(edit);

            Native.SendMessage(edit, Native.WM_SETTEXT, 0, "٢");
            Native.PostMessage(dialog, Native.WM_COMMAND, 1, 0);
        });

        var result = Win32GoToDialog.Show(0, ResxStrings.Instance, request);
        driver.Join();

        result.Should().Be(2);
        inputs.Should().Equal("9", "٢");
        observed["initial"].Should().Be("7");
        observed["focus"].Should().Be("edit");
        observed["selection"].Should().Be("0,1", "the dialog manager selects the text of the first tab stop");
        observed["typed"].Should().Be("٣", "ES_NUMBER refuses letters and the minus sign but takes digits of other scripts");
        observed["error"].Should().Be(ErrorText);
        observed["errorOwner"].Should().Be("dialog");
        observed["afterError"].Should().Be("3");
    });

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Cancel_returns_no_line() => StaThread.Run(() =>
    {
        var driver = DialogDriver.Run("Go To Line", (_, dialog) => Native.PostMessage(dialog, Native.WM_COMMAND, 2, 0));
        var result = Win32GoToDialog.Show(0, ResxStrings.Instance, new("1", _ => GoToInputCheck.Accept(1), ErrorText, ErrorCaption));
        driver.Join();
        result.Should().BeNull();
    });

    [Fact]
    public void An_exception_in_the_check_ends_the_dialog_and_is_rethrown() => StaThread.Run(() =>
    {
        var driver = DialogDriver.Run("Go To Line", (_, dialog) => Native.PostMessage(dialog, Native.WM_COMMAND, 1, 0));
        var show = () => Win32GoToDialog.Show(0, ResxStrings.Instance, new("1", _ => throw new InvalidOperationException("boom"), ErrorText, ErrorCaption));
        show.Should().Throw<InvalidOperationException>().WithMessage("boom");
        driver.Join();
    });

    [Fact]
    [Trait("Doc", "parity.md#hop-thoai-tim-kiem")]
    public void While_showing_moves_the_first_dialog_down_once_and_unhooks() => StaThread.Run(() =>
    {
        int TopOf(Func<Func<int?>, int?> show)
        {
            var top = 0;
            var driver = DialogDriver.Run("Go To Line", (_, dialog) =>
            {
                top = Native.Top(dialog);
                Native.PostMessage(dialog, Native.WM_COMMAND, 2, 0);
            });
            show(() => Win32GoToDialog.Show(0, ResxStrings.Instance, new("1", _ => GoToInputCheck.Accept(1), ErrorText, ErrorCaption)));
            driver.Join();
            return top;
        }

        var plain = TopOf(show => show());
        int first = 0, second = 0;
        DialogPlacement.WhileShowing(40, () =>
        {
            first = TopOf(show => show());
            second = TopOf(show => show());
            return 0;
        });
        var after = TopOf(show => show());

        first.Should().Be(plain + 40);
        second.Should().Be(plain, "only the first dialog shown under the hook moves");
        after.Should().Be(plain, "the hook is removed when the call returns");
    });

    private static short[] Shorts(ReadOnlySpan<byte> bytes, int count)
    {
        var result = new short[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
        }

        return result;
    }

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var text = new StringBuilder();
        while (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) is var c && c != 0)
        {
            text.Append((char)c);
            offset += 2;
        }

        offset += 2;
        return text.ToString();
    }
}
