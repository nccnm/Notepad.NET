using System.Globalization;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Services;

namespace NotepadNet.Core.Editing;

/// <summary><c>FoldStringW(MAP_FOLDDIGITS)</c>: digits of any script become ASCII digits.</summary>
public interface IDigitFolder
{
    string FoldDigits(string text);
}

/// <summary>Outcome of checking the Go To box: a line to go to, or the text that replaces the
/// input after the error message.</summary>
public readonly record struct GoToInputCheck(int? Line, string? ReplacementText)
{
    public static GoToInputCheck Accept(int line) => new(line, null);

    public static GoToInputCheck Reject(string replacementText) => new(null, replacementText);
}

/// <summary>What the Go To dialog shows and how it checks its input.</summary>
/// <param name="InitialText">Text of the "Line number" box when the dialog opens.</param>
/// <param name="Check">Runs on OK; a rejected input shows <paramref name="ErrorText"/> and keeps
/// the dialog open.</param>
public sealed record GoToDialogRequest(string InitialText, Func<string, GoToInputCheck> Check, string ErrorText, string ErrorCaption);

/// <summary>
/// Edit > Go To. The dialog starts with and
/// checks against displayed lines (<c>EM_LINEFROMCHAR</c>, <c>EM_LINEINDEX</c>) but jumps to a
/// physical line (<c>EM_FILELINEINDEX</c>). The two agree while Word Wrap is off, and the command
/// is grayed while it is on.
/// </summary>
public sealed class GoToService(IEditorSurface editor, IDigitFolder digits)
{
    /// <summary><c>GetDlgItemTextW(..., 100)</c>: at most 99 characters of the box are read.</summary>
    public const int MaxInputLength = 99;

    private readonly IEditorSurface _editor = editor;
    private readonly IDigitFolder _digits = digits;

    /// <summary><c>"%d"</c> of the displayed line holding the selection start, 1-based.</summary>
    public string InitialText() =>
        (_editor.LineFromChar(_editor.Selection.Start) + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Folds digits, reads the number like <c>_wtol</c>, and accepts it when it is not 0 and
    /// <c>EM_LINEINDEX(n - 1)</c> is not -1. Otherwise the box gets the line count.
    /// </summary>
    public GoToInputCheck Check(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > MaxInputLength)
        {
            input = input[..MaxInputLength];
        }

        var line = CRuntime.Wtol(_digits.FoldDigits(input));
        if (line != 0 && _editor.LineIndex(unchecked(line - 1)) != -1)
        {
            return GoToInputCheck.Accept(line);
        }

        return GoToInputCheck.Reject(_editor.LineCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary><c>GoToLine</c>: caret to the start of physical line <paramref name="line"/>
    /// (1-based) and scroll; nothing when the line does not exist.</summary>
    public void Jump(int line)
    {
        var index = _editor.FileLineIndex(unchecked(line - 1));
        if (index == -1)
        {
            return;
        }

        _editor.SetSelection(index, index);
        _editor.ScrollCaret();
    }

    /// <summary>Shows the modal dialog and jumps when it closes with OK.</summary>
    public void Run(IDialogService dialogs, ILocalizedStrings strings)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(strings);
        var line = dialogs.ShowGoTo(new(
            InitialText(), Check, strings.GetString(StringKeys.GoToBeyondEnd), strings.GetString(StringKeys.GoToCaption)));
        if (line is { } n)
        {
            Jump(n);
        }
    }
}

/// <summary>C runtime conversions the original relies on.</summary>
public static class CRuntime
{
    /// <summary>
    /// UCRT <c>_wtol</c>: skips leading white space, takes an optional sign and ASCII digits,
    /// stops at the first other character, and saturates at the 32-bit limits. No digits gives 0.
    /// </summary>
    public static int Wtol(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var negative = false;
        if (i < text.Length && text[i] is '+' or '-')
        {
            negative = text[i] == '-';
            i++;
        }

        long value = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++)
        {
            value = Math.Min(value * 10 + (text[i] - '0'), (long)int.MaxValue + 1);
        }

        if (negative)
        {
            value = -value;
        }

        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }
}
