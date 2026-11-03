using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Services;

/// <summary>Buttons of the "Do you want to save changes" task dialog.</summary>
public enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>Buttons of the "changed by another program" task dialog.</summary>
public enum FileChangedChoice
{
    Overwrite,
    SaveAs,
    Cancel,
}

public enum DialogOutcome
{
    Ok,

    /// <summary>The user closed the dialog (<c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c>).</summary>
    Cancelled,

    /// <summary>The dialog could not be shown; <see cref="FileDialogResult.HResult"/> says why.</summary>
    Failed,
}

/// <param name="Encoding">Item chosen in the "Encoding:" box. Open returns
/// <see cref="TextEncodingKind.Detect"/> for "Auto-Detect".</param>
public readonly record struct FileDialogResult(DialogOutcome Outcome, string? Path, TextEncodingKind Encoding, int HResult)
{
    public const int OutOfMemory = unchecked((int)0x8007000E);

    public const int Cancelled = unchecked((int)0x800704C7);

    public static FileDialogResult Ok(string path, TextEncodingKind encoding) => new(DialogOutcome.Ok, path, encoding, 0);

    public static FileDialogResult Cancel() => new(DialogOutcome.Cancelled, null, TextEncodingKind.Detect, Cancelled);

    public static FileDialogResult Fail(int hresult) => new(DialogOutcome.Failed, null, TextEncodingKind.Detect, hresult);
}

/// <summary>Icon and buttons of a message box. The caption is always the product name.</summary>
public enum MessageKind
{
    /// <summary><c>MB_ICONEXCLAMATION</c>, OK (file errors, too large).</summary>
    Warning,

    /// <summary><c>MB_OKCANCEL | MB_ICONEXCLAMATION</c> (string 27, characters will be lost).</summary>
    WarningOkCancel,

    /// <summary><c>MB_YESNOCANCEL | MB_ICONEXCLAMATION</c> (string 2, create the missing file?).</summary>
    WarningYesNoCancel,

    /// <summary><c>MB_SYSTEMMODAL | MB_ICONHAND</c> (not enough memory, string 7).</summary>
    SystemError,

    /// <summary>Two <c>MessageBeep(0)</c>, then <c>MB_SYSTEMMODAL</c> without an icon (string 24,
    /// shutdown blocked while a save dialog is open).</summary>
    ShutdownBlocked,
}

public enum MessageResult
{
    Ok,
    Cancel,
    Yes,
    No,
}

public interface IDialogService
{
    /// <summary>Task dialog with "&amp;Save", "Do&amp;n't Save" and Cancel; Save is the default.</summary>
    /// <param name="instruction">String 3001 with the document name already inserted.</param>
    SaveChangesChoice AskSaveChanges(string instruction);

    /// <summary>Task dialog with a warning icon, "&amp;Overwrite", "Save &amp;As..." and Cancel. Save As
    /// is the default, so Enter cannot destroy the other program's version.</summary>
    FileChangedChoice AskFileChanged(string instruction);

    /// <summary>Task dialog with a warning icon, "&amp;Reload" and "&amp;Keep". True for Reload; Esc is Keep.</summary>
    /// <param name="preferReload">Reload is the default button, otherwise Keep: with unsaved changes
    /// Enter must not lose them.</param>
    bool AskReload(string instruction, bool preferReload);

    /// <summary>File Open dialog with the "Encoding:" box, "Auto-Detect" preselected.</summary>
    FileDialogResult ShowOpen();

    /// <summary>Save As dialog with the "Encoding:" box.</summary>
    /// <param name="suggestedName"><c>*.txt</c> for an untitled document, else the current path.</param>
    /// <param name="initialEncoding">Item to preselect. When the box has no such item (GB18030
    /// outside code page 936) UTF-8 is selected instead.</param>
    FileDialogResult ShowSaveAs(string suggestedName, TextEncodingKind initialEncoding);

    MessageResult ShowMessage(string text, MessageKind kind);

    /// <summary><c>ChooseFontW</c>. Null when the user cancels.</summary>
    FontSpec? ChooseFont(FontSpec current);

    /// <summary><c>ShellAboutW</c>.</summary>
    void ShowAbout();

    /// <summary>Modal Go To dialog. Returns the accepted line, or null when cancelled.</summary>
    int? ShowGoTo(GoToDialogRequest request);
}
