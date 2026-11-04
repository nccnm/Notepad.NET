namespace NotepadNet.Core.Search;

/// <summary>The button that sent a request (<c>FINDREPLACEW.Flags</c> action bits).</summary>
public enum FindAction
{
    /// <summary><c>FR_FINDNEXT</c>.</summary>
    FindNext,

    /// <summary><c>FR_REPLACE</c>.</summary>
    Replace,

    /// <summary><c>FR_REPLACEALL</c>.</summary>
    ReplaceAll,

    /// <summary><c>FR_DIALOGTERM</c>: the dialog closed.</summary>
    Terminate,
}

/// <summary>Result bits sent back to the dialog with message <c>0x464</c>.</summary>
[Flags]
public enum FindResults
{
    None = 0,
    Found = 0x8,
    Replaced = 0x10,
    ReplacedAll = 0x20,
}

/// <summary>State the dialog opens with.</summary>
/// <param name="ReplaceWith">Replace dialog only; null for Find.</param>
public sealed record FindDialogSetup(string FindWhat, string? ReplaceWith, bool Down, bool MatchCase, bool WrapAround);

/// <summary>A notification from the dialog: the action and the dialog's current contents.</summary>
/// <param name="ReplaceWith">Null for the Find dialog, which has no such box.</param>
public sealed record FindDialogRequest(FindAction Action, string FindWhat, string? ReplaceWith, bool Down, bool MatchCase, bool WrapAround);

/// <summary>
/// The modeless Find/Replace dialog (<c>FindTextW</c>/<c>ReplaceTextW</c>): "Match whole word
/// only" hidden, "Wrap around" shown, Replace always searching down. At most one is open.
/// </summary>
public interface IFindReplaceDialog
{
    bool IsOpen { get; }

    /// <summary>Opens the Find dialog. False when the dialog could not be created.</summary>
    /// <param name="handler">Called for every button press and when the dialog closes; its result
    /// goes back to the dialog.</param>
    bool ShowFind(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler);

    /// <summary>Opens the Replace dialog. False when the dialog could not be created.</summary>
    bool ShowReplace(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler);

    /// <summary>Sends <c>WM_CLOSE</c> to the open dialog, which reports
    /// <see cref="FindAction.Terminate"/> before this returns.</summary>
    void Close();

    /// <summary>"Cannot find" box: <c>MB_ICONINFORMATION</c>, owned by the dialog while it is open,
    /// else by the main window.</summary>
    void ShowNotFound(string text);
}
