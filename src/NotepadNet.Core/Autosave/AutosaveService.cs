using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Services;

namespace NotepadNet.Core.Autosave;

/// <summary>What <see cref="AutosaveService.Restore"/> did.</summary>
public enum AutosaveRestoreOutcome
{
    /// <summary>The id is not a session id; nothing was read.</summary>
    InvalidId,

    /// <summary>No manifest for this id (a restart after a crash, or already restored).</summary>
    NotFound,

    /// <summary>The manifest cannot be read or is not one this version knows; the data is kept.</summary>
    Unreadable,

    /// <summary>A clean untitled document: nothing to open. The data is deleted.</summary>
    Nothing,

    /// <summary>The document was clean: its file was opened again by path. The data is deleted.</summary>
    Reopened,

    /// <summary>The dirty text is back, marked dirty. The data is deleted.</summary>
    Restored,

    /// <summary>The dirty text could not be loaded; the user was told and the data is kept.</summary>
    Kept,
}

/// <summary>
/// Autosave, Restart Manager (<c>WM_QUERYENDSESSION</c>, <c>WM_ENDSESSION</c>, <c>WM_CLOSE</c>) and
/// closed notes. Deliberate difference (parity.md): the original saves without asking only for a
/// Restart Manager close; here dirty text is never asked about when the window closes or the
/// session ends. It stays in the autosave folder as a note, and the next start without arguments
/// brings it back. While the window runs, <see cref="Snapshot"/> keeps the same entry current, so a
/// crash or a power cut loses only the last seconds.
/// </summary>
public sealed class AutosaveService : IDisposable
{
    /// <summary>Longest text <see cref="Snapshot"/> writes, in characters: 8 MiB as UTF-16. Longer
    /// text would stall typing on each write, so it is saved only when the window closes.</summary>
    public const int MaxSnapshotLength = 4 * 1024 * 1024;

    /// <summary>A clean entry with a manifest that no run has restored within this time is deleted
    /// at startup. Restart Manager restarts a closed program within minutes, or at the next logon
    /// after a reboot; a month leaves room for a machine that stays off for a while. An entry with
    /// text is never deleted this way: it is the only copy of a note.</summary>
    public static readonly TimeSpan CompleteEntryLifetime = TimeSpan.FromDays(30);

    /// <summary>Files without a manifest can never be restored (a save cut short); they are
    /// deleted after this time, which leaves a save still in progress in another window alone.</summary>
    public static readonly TimeSpan IncompleteEntryLifetime = TimeSpan.FromDays(1);

    private readonly DocumentService _documents;
    private readonly IEditorSurface _editor;
    private readonly IAutosaveStore _store;
    private readonly IProcessDirectory _directory;
    private readonly IDialogService _dialogs;
    private readonly ILocalizedStrings _strings;

    /// <summary>The lock of this run's entry, taken with its first save.</summary>
    private IDisposable? _lock;

    /// <summary>This run's entry holds a complete save.</summary>
    private bool _hasEntry;

    /// <summary>The window has agreed to close: nothing more is written.</summary>
    private bool _closed;

    public AutosaveService(
        DocumentService documents, IEditorSurface editor, IAutosaveStore store, IProcessDirectory directory, IDialogService dialogs,
        ILocalizedStrings strings, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(sessionId);
        _documents = documents;
        _editor = editor;
        _store = store;
        _directory = directory;
        _dialogs = dialogs;
        _strings = strings;
        SessionId = sessionId;
    }

    /// <summary>This run's id (<c>CreateSessionGuidString</c>).</summary>
    public string SessionId { get; }

    /// <summary>The session is ending for Restart Manager (<c>+0x18</c> of the session object):
    /// <c>WM_CLOSE</c> then also saves a clean document, which the restart opens again by path.</summary>
    public bool Armed { get; private set; }

    /// <summary>
    /// The window closes as soon as a command-line print ends: the save-changes question stays, since
    /// the user did not write what is dirty there (a <c>.LOG</c> stamp) and expects no note from it.
    /// </summary>
    public bool AskOnClose { get; set; }

    /// <summary><c>RegisterApplicationRestart("RestartByRestartManager:&lt;id&gt;", 0)</c> at startup.</summary>
    public bool Register(IRestartRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return registration.Register(AutosaveSession.RestartCommandLine(SessionId));
    }

    /// <summary>
    /// <c>WM_QUERYENDSESSION</c>: true lets the session end. A save dialog of
    /// <see cref="DocumentService.CheckSave"/> being open blocks it with two beeps and string 24; a
    /// clean document agrees; so does dirty text when the autosave folder can take it. Only when it
    /// cannot does the save-changes question decide.
    /// </summary>
    public bool QueryEndSession()
    {
        if (_documents.State.IsSaveDialogOpen)
        {
            _dialogs.ShowMessage(_strings.GetString(StringKeys.CannotShutDown), MessageKind.ShutdownBlocked);
            return false;
        }

        if (!_documents.IsDirty)
        {
            return true;
        }

        return CanSave() || _documents.CheckSave();
    }

    /// <summary><c>CanAutosave</c>: room for the text as UTF-16 with its NUL, <c>(length + 1) * 2</c> bytes.</summary>
    private bool CanSave()
    {
        try
        {
            return _store.CanSave((_editor.TextLength + 1L) * 2);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary><c>WM_ENDSESSION</c>: when the session really ends, dirty text is saved. For Restart
    /// Manager the session is also armed and a clean document saved too; a logoff sends no
    /// <c>WM_CLOSE</c> after this and has no restart to reopen a clean document.</summary>
    /// <param name="ending"><c>wParam</c>.</param>
    /// <param name="closeApp"><c>lParam &amp; ENDSESSION_CLOSEAPP</c>.</param>
    public void EndSession(bool ending, bool closeApp)
    {
        if (!ending)
        {
            return;
        }

        Armed = closeApp;
        if (closeApp || _documents.IsDirty)
        {
            Save();
        }
        else
        {
            Discard();
        }
    }

    /// <summary>
    /// <c>WM_CLOSE</c> after the settings are saved: true lets the window close. Dirty text, and any
    /// document of an armed session, is saved without asking when the save works. Otherwise the
    /// save-changes question decides, and this run's entry goes: what it held was saved or refused.
    /// The lock is released here so that a start right after this close finds the note.
    /// </summary>
    public bool ConfirmClose()
    {
        if (!AskOnClose && (Armed || _documents.IsDirty) && Save())
        {
            Finish();
            return true;
        }

        if (!_documents.CheckSave())
        {
            return false;
        }

        Discard();
        Finish();
        return true;
    }

    private void Finish()
    {
        _closed = true;
        ReleaseLock();
    }

    private void ReleaseLock()
    {
        _lock?.Dispose();
        _lock = null;
    }

    public void Dispose() => ReleaseLock();

    /// <summary>Whether <see cref="Snapshot"/> has anything to do. A clean window that never had
    /// dirty text needs no timer at all.</summary>
    public bool WantsSnapshot => _hasEntry || _documents.IsDirty;

    /// <summary>
    /// Brings this run's entry up to date while the window runs: dirty text is written, a clean
    /// document leaves nothing behind. Text over <see cref="MaxSnapshotLength"/> keeps its last
    /// snapshot until the window closes. A failure keeps the last snapshot too.
    /// </summary>
    public void Snapshot()
    {
        if (_closed || Armed)
        {
            return;
        }

        if (!_documents.IsDirty)
        {
            Discard();
        }
        else if (_editor.TextLength <= MaxSnapshotLength)
        {
            Save();
        }
    }

    private void Discard()
    {
        if (_hasEntry && TryDelete(SessionId))
        {
            _hasEntry = false;
        }
    }

    /// <summary>
    /// <c>SaveAutosaveState</c>: the text first when dirty, then the manifest, so a manifest never
    /// names text that is not there. A failure deletes what was written, as the original deletes
    /// its registry key, unless an earlier save is there: that one is still a good copy.
    /// </summary>
    public bool Save()
    {
        var state = _documents.State;
        var dirty = _documents.IsDirty;
        var selection = _editor.Selection;
        var manifest = new AutosaveManifest(
            AutosaveManifest.CurrentVersion, state.CurrentEncoding, state.IsUntitled ? null : state.Path, dirty, _directory.Current,
            dirty ? selection.Start : 0, dirty ? selection.End : 0, dirty ? state.KnownFileStamp : null);
        try
        {
            _lock ??= _store.TryLock(SessionId) ?? throw new IOException("This session's autosave entry is locked.");
            if (dirty)
            {
                using var lease = _editor.AcquireText();
                _store.WriteText(SessionId, lease.Text);
            }
            else if (_hasEntry)
            {
                // The text of an earlier save is out of date now.
                _store.Delete(SessionId);
                _hasEntry = false;
            }

            _store.WriteManifest(SessionId, manifest.ToJson());
            _hasEntry = true;
            return true;
        }
        catch (IOException)
        {
            if (!_hasEntry)
            {
                TryDelete(SessionId);
            }

            return false;
        }
    }

    /// <summary>
    /// A start without arguments: restores the oldest note no window owns. A note in another
    /// window's entry stays with that window, and one restored by a start running at the same time
    /// is skipped through its lock.
    /// </summary>
    /// <returns>True when another note is still waiting. The caller starts one more window for it,
    /// which does the same, so N notes open N windows and the chain ends by itself.</returns>
    public bool RestoreClosedNote()
    {
        var restored = false;
        foreach (var id in ClosedNotes())
        {
            if (!restored)
            {
                restored = Restore(id) == AutosaveRestoreOutcome.Restored;
            }
            else if (IsFree(id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Entries with text and a readable manifest, oldest first, so the note closed last
    /// ends up in the window opened last, on top.</summary>
    private List<string> ClosedNotes()
    {
        var notes = new List<string>();
        try
        {
            foreach (var entry in _store.List().Where(e => e.HasManifest && e.HasText && e.Id != SessionId).OrderByDescending(e => e.Age))
            {
                if (_store.ReadManifest(entry.Id) is { } json && AutosaveManifest.Parse(json) is { HasText: true })
                {
                    notes.Add(entry.Id);
                }
            }
        }
        catch (IOException)
        {
            // Whatever was found so far can still be restored.
        }

        return notes;
    }

    private bool IsFree(string id)
    {
        try
        {
            using var taken = _store.TryLock(id);
            return taken is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>RestoreAutosaveState</c> for <c>RestartByRestartManager:&lt;id&gt;</c> and for a closed
    /// note. The current directory comes back first. Dirty text is loaded with the document's path,
    /// title, encoding and selection and marked dirty, and moves to this run's entry; a clean named
    /// document is opened again by path; a clean untitled one opens nothing. The old entry is
    /// deleted afterwards, except when the dirty text failed to load or the manifest cannot be read
    /// (deliberate difference, parity.md), or the text could not move: the old entry is then still
    /// the only copy on disk. An entry another run holds counts as not found.
    /// </summary>
    public AutosaveRestoreOutcome Restore(string id)
    {
        if (!AutosaveSession.IsValidId(id))
        {
            return AutosaveRestoreOutcome.InvalidId;
        }

        IDisposable? claim;
        try
        {
            claim = _store.TryLock(id);
        }
        catch (IOException)
        {
            return AutosaveRestoreOutcome.Unreadable;
        }

        if (claim is null)
        {
            return AutosaveRestoreOutcome.NotFound;
        }

        using (claim)
        {
            return RestoreClaimed(id);
        }
    }

    private AutosaveRestoreOutcome RestoreClaimed(string id)
    {
        AutosaveManifest? manifest;
        try
        {
            var json = _store.ReadManifest(id);
            if (json is null)
            {
                return AutosaveRestoreOutcome.NotFound;
            }

            manifest = AutosaveManifest.Parse(json);
        }
        catch (IOException)
        {
            return AutosaveRestoreOutcome.Unreadable;
        }

        if (manifest is null)
        {
            return AutosaveRestoreOutcome.Unreadable;
        }

        if (!string.IsNullOrEmpty(manifest.CurrentDirectory))
        {
            _ = _directory.TrySetCurrent(manifest.CurrentDirectory);
        }

        AutosaveRestoreOutcome outcome;
        if (manifest.HasText)
        {
            if (!_documents.RestoreAutosavedText(_store.TextPath(id), manifest.DocumentPath, manifest.Encoding, manifest.FileStamp))
            {
                return AutosaveRestoreOutcome.Kept;
            }

            _editor.SetSelection(manifest.SelectionStart, manifest.SelectionEnd);
            _editor.ScrollCaret();
            outcome = AutosaveRestoreOutcome.Restored;
            if (!Save())
            {
                return outcome;
            }
        }
        else if (manifest.DocumentPath is { } path)
        {
            if (_documents.Load(path, manifest.Encoding) == LoadOutcome.OpenFailed)
            {
                _documents.ShowFileError(path, _documents.LastOpenError);
            }

            outcome = AutosaveRestoreOutcome.Reopened;
        }
        else
        {
            outcome = AutosaveRestoreOutcome.Nothing;
        }

        TryDelete(id);
        return outcome;
    }

    /// <summary>
    /// Deliberate difference (parity.md): the original never deletes autosave data that no run
    /// restores. At startup the port deletes clean entries older than <see cref="CompleteEntryLifetime"/>,
    /// or <see cref="IncompleteEntryLifetime"/> when they have no manifest. A note, with both a
    /// manifest and text, stays until the user gets it back.
    /// </summary>
    /// <returns>How many entries were deleted.</returns>
    public int CleanOrphans()
    {
        IReadOnlyList<AutosaveEntry> entries;
        try
        {
            entries = _store.List();
        }
        catch (IOException)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var entry in entries)
        {
            if (entry.HasManifest && entry.HasText)
            {
                continue;
            }

            var lifetime = entry.HasManifest ? CompleteEntryLifetime : IncompleteEntryLifetime;
            if (entry.Id != SessionId && entry.Age >= lifetime && TryDelete(entry.Id))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private bool TryDelete(string id)
    {
        try
        {
            _store.Delete(id);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
