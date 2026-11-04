using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Core.StatusBar;

namespace NotepadNet.Core.Search;

/// <summary>Everything <see cref="SearchService"/> works with.</summary>
public sealed record SearchServiceDependencies(
    IEditorSurface Editor,
    NotepadSettings Settings,
    ITextSearcher Searcher,
    IFindReplaceDialog Dialog,
    IDialogService Dialogs,
    StatusBarPresenter StatusBar,
    ILocalizedStrings Strings,
    IMainWindow Window);

/// <summary>
/// Find, Find Next/Previous, Replace and Replace All (the menu commands
/// and the <c>FINDMSGSTRING</c> branch of <c>MainWndProc</c>). The search
/// state lives in <see cref="NotepadSettings"/>, which plays the part of the original's shared
/// <c>FINDREPLACEW</c> buffers.
/// </summary>
public sealed class SearchService(SearchServiceDependencies dependencies)
{
    /// <summary>Find what and Replace with hold 128 WCHAR including the NUL.</summary>
    public const int MaxPatternLength = 127;

    private readonly IEditorSurface _editor = dependencies.Editor;
    private readonly NotepadSettings _settings = dependencies.Settings;
    private readonly ITextSearcher _searcher = dependencies.Searcher;
    private readonly IFindReplaceDialog _dialog = dependencies.Dialog;
    private readonly IDialogService _dialogs = dependencies.Dialogs;
    private readonly StatusBarPresenter _statusBar = dependencies.StatusBar;
    private readonly ILocalizedStrings _strings = dependencies.Strings;
    private readonly IMainWindow _window = dependencies.Window;

    /// <summary>Longest text Replace All may produce; tests lower it.</summary>
    internal int MaxReplaceAllLength { get; set; } = FindEngine.MaxStringLength;

    private SearchOptions Options => new(_settings.MatchCase, _settings.WrapAround);

    /// <summary>
    /// Edit > Find: closes an open dialog, prefills the selection, opens Find with the direction,
    /// Match case and Wrap around of last time.
    /// </summary>
    public void OpenFind() => Open(replace: false);

    /// <summary>Edit > Replace: like Find, but the dialog always searches down.</summary>
    public void OpenReplace() => Open(replace: true);

    /// <summary>
    /// F3 (down) and Shift+F3 (up), whatever direction the dialog last had. Searches only after
    /// Find or Replace has been opened in this session and with a non-empty pattern; otherwise
    /// opens the Find dialog.
    /// </summary>
    public void FindNextFromKey(bool backward)
    {
        if (_settings.SearchChanged && _settings.SearchString.Length != 0)
        {
            FindNext(backward);
            return;
        }

        Open(replace: false);
    }

    /// <summary>
    /// <c>FindNext</c>: selects and scrolls to the match, shows " Found next from the top/bottom"
    /// in status part 0 after wrapping (empty otherwise). With no match shows "Cannot find" and
    /// leaves the status bar alone. An empty pattern does nothing.
    /// </summary>
    /// <returns>Whether a match was found.</returns>
    public bool FindNext(bool backward)
    {
        var pattern = _settings.SearchString;
        if (pattern.Length == 0)
        {
            return false;
        }

        FindMatch? match;
        var selection = _editor.Selection;
        using (var lease = _editor.AcquireText())
        {
            match = FindEngine.Locate(lease.Text, selection, pattern, backward, Options, _searcher);
        }

        if (match is not { } found)
        {
            _dialog.ShowNotFound(MessageText.InsertArgument(_strings.GetString(StringKeys.CannotFind), pattern));
            return false;
        }

        _editor.SetSelection(found.Start, found.End);
        _editor.ScrollCaret();
        _statusBar.SetMessage(found.Wrapped ? _strings.GetString(backward ? StringKeys.FoundFromBottom : StringKeys.FoundFromTop) : "");
        return true;
    }

    /// <summary>
    /// <c>ReplaceSelectionIfMatch</c>: only when the selection is exactly one match, replaces it
    /// with undo and selects the new text.
    /// </summary>
    public bool ReplaceSelectionIfMatch()
    {
        var selection = _editor.Selection;
        bool matches;
        using (var lease = _editor.AcquireText())
        {
            matches = FindEngine.SelectionMatches(lease.Text, selection, _settings.SearchString, _settings.MatchCase, _searcher);
        }

        if (!matches)
        {
            return false;
        }

        var replacement = _settings.ReplaceString;
        _editor.ReplaceSelection(replacement, canUndo: true);
        _editor.SetSelection(selection.Start, selection.Start + replacement.Length);
        _editor.ScrollCaret();
        return true;
    }

    /// <summary>
    /// Replace All: forces the direction down (and keeps it), replaces every match from the start
    /// of the text in one <c>EM_REPLACESEL</c> (one undo step), then puts the caret at the start.
    /// No message when nothing matches. Deliberate difference (parity.md): the original replaces a
    /// single match through its replace-then-find loop, which never ends with Wrap around when the
    /// replacement contains the pattern; the port builds the new text for any number of matches.
    /// </summary>
    /// <returns>Whether anything was replaced.</returns>
    public bool ReplaceAll()
    {
        _settings.Reverse = false;
        using (_window.ShowWaitCursor())
        {
            _editor.SetSelection(0, 0);
            string? replaced = null;
            var count = 0;
            var length = 0;
            if (_settings.SearchString.Length != 0)
            {
                using var lease = _editor.AcquireText();
                length = lease.Text.Length;
                replaced = FindEngine.BuildReplaceAll(lease.Text, _settings.SearchString, _settings.ReplaceString, _settings.MatchCase, _searcher, MaxReplaceAllLength, out count);
            }

            if (replaced is not null)
            {
                _editor.SetSelection(0, length);
                _editor.ReplaceSelection(replaced, canUndo: true);
            }

            _editor.SetSelection(0, 0);
            _editor.ScrollCaret();
            _statusBar.UpdateLineColumn(force: true);
            if (count > 0 && replaced is null)
            {
                _dialogs.ShowMessage(_strings.GetString(StringKeys.NotEnoughMemory), MessageKind.SystemError);
            }

            return replaced is not null;
        }
    }

    /// <summary>
    /// The <c>FINDMSGSTRING</c> branch: every notification first stores the dialog's direction,
    /// Match case, Wrap around and text, then runs the action. The result goes back to the dialog.
    /// </summary>
    public FindResults OnDialogRequest(FindDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _settings.Reverse = !request.Down;
        _settings.MatchCase = request.MatchCase;
        _settings.WrapAround = request.WrapAround;
        _settings.SearchString = Limit(request.FindWhat);
        if (request.ReplaceWith is not null)
        {
            _settings.ReplaceString = Limit(request.ReplaceWith);
        }

        switch (request.Action)
        {
            case FindAction.FindNext:
                using (_window.ShowWaitCursor())
                {
                    return FindNext(_settings.Reverse) ? FindResults.Found : FindResults.None;
                }

            case FindAction.Replace:
                using (_window.ShowWaitCursor())
                {
                    var replaced = ReplaceSelectionIfMatch();
                    var found = FindNext(_settings.Reverse);
                    return (replaced ? FindResults.Replaced : FindResults.None) | (found ? FindResults.Found : FindResults.None);
                }

            case FindAction.ReplaceAll:
                return ReplaceAll() ? FindResults.ReplacedAll : FindResults.None;

            default:
                _statusBar.SetMessage("");
                return FindResults.None;
        }
    }

    private void Open(bool replace)
    {
        _settings.SearchChanged = true;
        if (_dialog.IsOpen)
        {
            _dialog.Close();
        }

        PrefillFromSelection();
        bool shown;
        if (replace)
        {
            shown = _dialog.ShowReplace(
                new(_settings.SearchString, _settings.ReplaceString, Down: true, _settings.MatchCase, _settings.WrapAround), OnDialogRequest);
        }
        else
        {
            shown = _dialog.ShowFind(
                new(_settings.SearchString, null, Down: !_settings.Reverse, _settings.MatchCase, _settings.WrapAround), OnDialogRequest);
        }

        if (!shown)
        {
            // The original falls back to its own copy of the dialog; the port has none (parity.md).
            _dialogs.ShowMessage(_strings.GetString(StringKeys.NotEnoughMemory), MessageKind.SystemError);
        }
    }

    /// <summary><c>PrefillFindFromSelection</c>: the first 127 characters of a non-empty
    /// selection become the pattern; an empty selection keeps the old one.</summary>
    private void PrefillFromSelection()
    {
        var selection = _editor.Selection;
        if (selection.Start >= selection.End)
        {
            return;
        }

        using var lease = _editor.AcquireText();
        var text = lease.Text;
        var start = Math.Min(selection.Start, text.Length);
        var count = Math.Min(Math.Min(selection.End, text.Length) - start, MaxPatternLength);
        _settings.SearchString = new string(text.Slice(start, count));
    }

    private static string Limit(string text) => text.Length <= MaxPatternLength ? text : text[..MaxPatternLength];
}
