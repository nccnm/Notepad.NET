using NotepadClassic.Core.IO;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Documents;

/// <summary>Document state the original keeps in globals.</summary>
public sealed class DocumentState(TextEncodingKind defaultEncoding)
{
    /// <summary>Full path; null while untitled.</summary>
    public string? Path { get; set; }

    /// <summary>No file name yet, so Save becomes Save As.</summary>
    public bool IsUntitled { get; set; } = true;

    /// <summary>The file as this document last read or wrote it; null when untitled or unknown.
    /// Save asks before overwriting a file that no longer matches.</summary>
    public FileStamp? KnownFileStamp
    {
        get;
        set
        {
            field = value;
            SeenFileStamp = value;
        }
    }

    /// <summary>The file as last reported to the user, null once reported gone: each change outside
    /// is reported once, while Save goes on comparing with <see cref="KnownFileStamp"/>.</summary>
    public FileStamp? SeenFileStamp { get; set; }

    /// <summary>Current encoding: status bar, Save, Save As preselection.</summary>
    public TextEncodingKind CurrentEncoding { get; set; } = defaultEncoding;

    /// <summary>Default encoding (<c>iDefaultEncoding</c>).</summary>
    public TextEncodingKind DefaultEncoding { get; set; } = defaultEncoding;

    /// <summary>Dirty state the title shows, so <c>EN_CHANGE</c> only
    /// rewrites the title when the <c>*</c> appears or disappears.</summary>
    public bool TitleShowsDirty { get; set; }

    /// <summary>A save-changes or Save As dialog of <c>CheckSave</c> is open
    /// (shutdown is blocked while it is set).</summary>
    public bool IsSaveDialogOpen { get; set; }
}

/// <summary>What the document logic needs from the main window.</summary>
public interface IMainWindow
{
    void SetTitle(string title);

    /// <summary><c>GetWindowTextW</c> of the main window (the print job's document name).</summary>
    string Title { get; }

    /// <summary>Posts <c>WM_CLOSE</c> (File > Exit).</summary>
    void RequestClose();

    void SetStatusBarVisible(bool visible);

    /// <summary>Opacity of the whole window, in percent (View > Transparency).</summary>
    void SetOpacity(int percent);

    /// <summary>Keeps the window above windows that are not topmost (View > Always on Top).</summary>
    void SetAlwaysOnTop(bool onTop);

    /// <summary>Shows the wait cursor until disposed (<c>SetCursor(IDC_WAIT)</c>).</summary>
    IDisposable ShowWaitCursor();
}
