using NotepadNet.Core.Editing;
using NotepadNet.Core.Search;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>
/// Ordinal stand-in for <c>FindNLSString</c>: exact or case-insensitive per character. Characters
/// in <see cref="Ignorable"/> are skipped inside a match, so a match can be longer than the
/// pattern as it can with the linguistic comparison (soft hyphen, for example).
/// </summary>
internal sealed class FakeSearcher : ITextSearcher
{
    public string Ignorable { get; set; } = "­";

    public int Calls { get; private set; }

    public int Find(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength)
    {
        Calls++;
        matchLength = 0;
        if (text.IsEmpty || pattern.IsEmpty)
        {
            return -1;
        }

        for (var n = 0; n < text.Length; n++)
        {
            var start = fromEnd ? text.Length - 1 - n : n;
            var length = MatchAt(text, start, pattern, matchCase);
            if (length > 0)
            {
                matchLength = length;
                return start;
            }
        }

        return -1;
    }

    private int MatchAt(ReadOnlySpan<char> text, int start, ReadOnlySpan<char> pattern, bool matchCase)
    {
        var i = start;
        foreach (var p in pattern)
        {
            while (i > start && i < text.Length && Ignorable.Contains(text[i], StringComparison.Ordinal))
            {
                i++;
            }

            if (i >= text.Length || !Same(text[i], p, matchCase))
            {
                return 0;
            }

            i++;
        }

        return i - start;
    }

    private static bool Same(char a, char b, bool matchCase) =>
        matchCase ? a == b : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}

/// <summary>Records what the Find/Replace dialog is asked to do; the test plays the user.</summary>
internal sealed class FakeFindDialog : IFindReplaceDialog
{
    private Func<FindDialogRequest, FindResults>? _handler;

    public bool IsOpen { get; private set; }

    public bool IsReplace { get; private set; }

    public bool FailToOpen { get; set; }

    public List<string> Calls { get; } = [];

    public List<FindDialogSetup> Setups { get; } = [];

    public List<string> NotFound { get; } = [];

    /// <summary>Whether the dialog was open when each "Cannot find" box showed (it owns the box then).</summary>
    public List<bool> NotFoundOwnedByDialog { get; } = [];

    public List<FindResults> Results { get; } = [];

    /// <summary>Contents of the boxes; the dialog sends them with every notification.</summary>
    public string FindWhat { get; set; } = "";

    public string ReplaceWith { get; set; } = "";

    public bool Down { get; set; } = true;

    public bool MatchCase { get; set; }

    public bool WrapAround { get; set; }

    public bool ShowFind(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler) => Show(setup, handler, replace: false);

    public bool ShowReplace(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler) => Show(setup, handler, replace: true);

    public void Close()
    {
        Calls.Add("Close");
        Press(FindAction.Terminate);
    }

    public void ShowNotFound(string text)
    {
        NotFound.Add(text);
        NotFoundOwnedByDialog.Add(IsOpen);
    }

    /// <summary>Simulates a button: sends the request and keeps the result as the dialog would.</summary>
    public FindResults Press(FindAction action)
    {
        var handler = _handler ?? throw new InvalidOperationException("No dialog is open.");
        if (action == FindAction.Terminate)
        {
            IsOpen = false;
            _handler = null;
        }

        var result = handler(new(action, FindWhat, IsReplace ? ReplaceWith : null, IsReplace || Down, MatchCase, WrapAround));
        Results.Add(result);
        return result;
    }

    private bool Show(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler, bool replace)
    {
        Calls.Add(replace ? "ShowReplace" : "ShowFind");
        Setups.Add(setup);
        if (FailToOpen)
        {
            return false;
        }

        IsOpen = true;
        IsReplace = replace;
        _handler = handler;
        FindWhat = setup.FindWhat;
        ReplaceWith = setup.ReplaceWith ?? "";
        Down = setup.Down;
        MatchCase = setup.MatchCase;
        WrapAround = setup.WrapAround;
        return true;
    }
}

/// <summary>Folds Arabic-Indic (U+0660) and fullwidth (U+FF10) digits, like MAP_FOLDDIGITS does.</summary>
internal sealed class FakeDigitFolder : IDigitFolder
{
    public string FoldDigits(string text) => string.Create(text.Length, text, static (destination, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            destination[i] = c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '０' and <= '９' => (char)('0' + (c - '０')),
                _ => c,
            };
        }
    });
}
