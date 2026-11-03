using NotepadClassic.Core.Editing;
using NotepadClassic.Core.IO;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.StatusBar;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Documents;

public enum LoadOutcome
{
    Loaded,

    /// <summary>The file could not be opened; the caller shows the file error.</summary>
    OpenFailed,

    /// <summary>The load failed and the user has already been told (too large, out of space, policy).</summary>
    FailedAndReported,
}

/// <param name="Win32Error">Error of the failed open or write.</param>
/// <param name="CancelledByUser">The user answered Cancel to the loss warning (string 27).</param>
public readonly record struct SaveResult(bool Succeeded, int Win32Error, bool CancelledByUser = false);

/// <summary>The external collaborators of <see cref="DocumentService"/>.</summary>
public sealed record DocumentServiceDependencies(
    IEditorSurface Editor,
    IMainWindow Window,
    IFileSystem FileSystem,
    ITextBufferFactory Buffers,
    EncodingDetector Detector,
    DocumentLoader Loader,
    TextEncoder Encoder,
    IDialogService Dialogs,
    ILocalizedStrings Strings,
    IFileTitleProvider FileTitles,
    ISystemErrorMessages SystemMessages,
    IFileAccessPolicy AccessPolicy,
    IShell Shell,
    TimeDateInserter TimeDate,
    StatusBarPresenter StatusBar,
    RecentFilesService RecentFiles);

/// <summary>
/// New, Open, Save, Save As, the save-changes question, and the load and save algorithms of the
/// original.
/// </summary>
public sealed class DocumentService
{
    private const string UntitledSuggestion = "*.txt";

    private readonly DocumentServiceDependencies _d;
    private readonly IEditorSurface _editor;

    public DocumentService(DocumentServiceDependencies dependencies, DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _d = dependencies;
        _editor = dependencies.Editor;
        State = state;
        _editor.TextChanged += (_, _) => OnTextChanged();
        _editor.OutOfSpace += (_, _) => _d.Dialogs.ShowMessage(_d.Strings.GetString(StringKeys.NotEnoughMemory), MessageKind.SystemError);
    }

    public DocumentState State { get; }

    /// <summary><c>IsDocumentDirty</c>: an untitled empty document never needs saving, even after
    /// typing and deleting; otherwise <c>EM_GETMODIFY</c>.</summary>
    public bool IsDirty => !(State.IsUntitled && _editor.TextLength == 0) && _editor.Modified;

    /// <summary><c>UpdateTitle</c>: <c>%1%2 - Notepad Classic</c> with <c>*</c> when dirty.</summary>
    public void UpdateTitle()
    {
        var dirty = IsDirty;
        var name = State.IsUntitled || State.Path is null ? _d.Strings.GetString(StringKeys.Untitled) : _d.FileTitles.GetTitle(State.Path);
        _d.Window.SetTitle(MessageText.FormatInserts(_d.Strings.GetString(StringKeys.TitleFormat), dirty ? "*" : "", name));
        State.TitleShowsDirty = dirty;
    }

    private void OnTextChanged()
    {
        if (IsDirty != State.TitleShowsDirty)
        {
            UpdateTitle();
        }
    }

    /// <summary>
    /// <c>CheckSave</c>: true lets the caller go on, false means the user cancelled. Asks with
    /// string 3001 naming <c>Untitled</c> or the full path. Save writes a named document straight
    /// away with the current encoding; an untitled document, or a failed write, goes through a
    /// Save As loop that reports each failure and offers the chosen name again.
    /// </summary>
    public bool CheckSave()
    {
        if (!IsDirty)
        {
            return true;
        }

        var name = State.IsUntitled || State.Path is null ? _d.Strings.GetString(StringKeys.Untitled) : State.Path;
        var question = MessageText.FormatInserts(_d.Strings.GetString(StringKeys.SaveChangesQuestion), name);
        SaveChangesChoice choice;
        State.IsSaveDialogOpen = true;
        try
        {
            choice = _d.Dialogs.AskSaveChanges(question);
        }
        finally
        {
            State.IsSaveDialogOpen = false;
        }

        if (choice != SaveChangesChoice.Save)
        {
            return choice == SaveChangesChoice.DontSave;
        }

        if (!State.IsUntitled && State.Path is not null && SaveFile(State.Path, isSaveAs: false, State.CurrentEncoding).Succeeded)
        {
            UpdateTitle();
            return true;
        }

        var suggestion = State.IsUntitled || State.Path is null ? UntitledSuggestion : State.Path;
        while (true)
        {
            var result = ShowSaveAs(suggestion);

            if (result.Outcome != DialogOutcome.Ok || result.Path is null)
            {
                ReportDialogFailure(result);
                return false;
            }

            var saved = SaveFile(result.Path, isSaveAs: true, result.Encoding);
            if (saved.Succeeded)
            {
                SetPath(result.Path);
                return true;
            }

            if (!saved.CancelledByUser)
            {
                ShowFileError(result.Path, saved.Win32Error);
            }

            suggestion = result.Path;
        }
    }

    /// <summary><c>NewDocument</c>: always asks first (the answer is ignored when
    /// <paramref name="askSave"/> is false); keeps zoom, font and word wrap.</summary>
    /// <returns>False when the user cancelled.</returns>
    public bool New(bool askSave = true)
    {
        var proceed = CheckSave();
        if (askSave && !proceed)
        {
            return false;
        }

        ResetToUntitled();
        return true;
    }

    private void ResetToUntitled()
    {
        _editor.Clear();
        State.IsUntitled = true;
        State.Path = null;
        UpdateTitle();
        _editor.SetSelection(0, 0);
        _editor.ScrollCaret();
        State.CurrentEncoding = State.DefaultEncoding;
        _d.StatusBar.UpdateAll();
    }

    /// <summary>
    /// File > Open: save-changes question, the Open dialog, then the load with the encoding chosen
    /// in the dialog. "Auto-Detect" becomes the result of the 1 KiB pre-scan, so the load gets a
    /// forced encoding. The current encoding takes the chosen value before the load and gets its
    /// old value back when the load fails.
    /// </summary>
    public void Open()
    {
        if (!CheckSave())
        {
            return;
        }

        var result = _d.Dialogs.ShowOpen();
        if (result.Outcome != DialogOutcome.Ok || result.Path is null)
        {
            ReportDialogFailure(result);
            return;
        }

        var encoding = result.Encoding == TextEncodingKind.Detect ? Sniff(result.Path) : result.Encoding;
        var previous = State.CurrentEncoding;
        State.CurrentEncoding = encoding;
        var outcome = Load(result.Path, encoding);
        if (outcome != LoadOutcome.Loaded)
        {
            State.CurrentEncoding = previous;
            if (outcome == LoadOutcome.OpenFailed)
            {
                ShowFileError(result.Path, LastOpenError);
            }
        }
    }

    /// <summary>
    /// <c>OnDropFiles</c> for the first dropped file: save-changes question, then a load that always
    /// detects the encoding. Unlike Open, nothing is set before the load, so nothing is restored
    /// when it fails; an open error shows the file error.
    /// </summary>
    public void OpenDropped(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!CheckSave())
        {
            return;
        }

        if (Load(path, TextEncodingKind.Detect) == LoadOutcome.OpenFailed)
        {
            ShowFileError(path, LastOpenError);
        }
    }

    /// <summary>
    /// File > Recent Files: like a dropped file, save-changes question, then a load that detects the
    /// encoding. A file that is gone leaves the list; other errors keep the entry, since a missing
    /// drive or a locked file can come back.
    /// </summary>
    public void OpenRecent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!CheckSave())
        {
            return;
        }

        if (Load(path, TextEncodingKind.Detect) == LoadOutcome.OpenFailed)
        {
            ShowFileError(path, LastOpenError);
            if (LastOpenError == FileErrors.FileNotFound)
            {
                _d.RecentFiles.Remove(path);
            }
        }
    }

    /// <summary>Win32 error of the last <see cref="LoadOutcome.OpenFailed"/>.</summary>
    public int LastOpenError { get; private set; }

    /// <summary><c>SniffFileEncoding</c>: first 1 KiB; the default encoding when the file cannot be read.</summary>
    public TextEncodingKind Sniff(string path)
    {
        try
        {
            using var file = _d.FileSystem.OpenRead(path);
            Span<byte> prefix = stackalloc byte[EncodingDetector.SniffLength];
            var read = file.Read(0, prefix);
            return _d.Detector.Sniff(prefix[..read], file.Length > read, State.DefaultEncoding);
        }
        catch (IOException)
        {
            return State.DefaultEncoding;
        }
    }

    /// <summary>
    /// <c>LoadFile</c> without EDP: size limit (string 8), access policy, decode, hand-off to the
    /// editor with the out-of-space handshake (string 8 and a new document), <c>.LOG</c> stamp at
    /// the end, status bar, Recent Documents.
    /// </summary>
    public LoadOutcome Load(string path, TextEncodingKind forcedEncoding)
    {
        ArgumentNullException.ThrowIfNull(path);
        var outcome = Read(path, forcedEncoding, autosaved: false, out var document);
        if (outcome != LoadOutcome.Loaded)
        {
            return outcome;
        }

        State.CurrentEncoding = document.Encoding;
        _editor.Modified = false;
        State.Path = path;
        State.IsUntitled = false;
        UpdateTitle();
        if (!_editor.LoadBuffer(document.Buffer, document.Length))
        {
            ShowMessage(StringKeys.FileTooLarge, path, MessageKind.Warning);
            ResetToUntitled();
            return LoadOutcome.FailedAndReported;
        }

        if (document.StartsWithLogMarker)
        {
            _editor.SetSelection(document.Length, document.Length);
            _editor.ScrollCaret();
            _d.TimeDate.Insert(_editor, forLog: true);
        }

        _d.StatusBar.UpdateAll();
        _d.Shell.AddToRecentDocuments(path);
        _d.RecentFiles.Add(path);
        return LoadOutcome.Loaded;
    }

    /// <summary>
    /// Opens and decodes a file for <see cref="Load"/> or <see cref="RestoreAutosavedText"/>:
    /// size limit (string 8), access policy (not for the port's own autosave file), decode. An
    /// allocation failure leaves an untitled document and shows string 8.
    /// </summary>
    private LoadOutcome Read(string path, TextEncodingKind forcedEncoding, bool autosaved, out DecodedDocument document)
    {
        document = null!;
        IFileContent file;
        try
        {
            file = _d.FileSystem.OpenRead(path);
        }
        catch (IOException e)
        {
            LastOpenError = FileErrors.Win32ErrorOf(e);
            return LoadOutcome.OpenFailed;
        }

        using (file)
        {
            if (DocumentLoader.IsTooLarge(file.Length))
            {
                ShowMessage(StringKeys.FileTooLarge, path, MessageKind.Warning);
                return LoadOutcome.FailedAndReported;
            }

            if (!autosaved && !_d.AccessPolicy.CanOpen(path))
            {
                return LoadOutcome.FailedAndReported;
            }

            try
            {
                document = autosaved
                    ? _d.Loader.Load(file.GetAllBytes(), new EncodingDecision(TextEncodingKind.Utf16LE, 0), keepNulls: true, _d.Buffers)
                    : _d.Loader.Load(file.GetAllBytes(), forcedEncoding, keepNulls: false, _d.Buffers);
            }
            catch (OutOfMemoryException)
            {
                ResetToUntitled();
                ShowMessage(StringKeys.FileTooLarge, path, MessageKind.Warning);
                return LoadOutcome.FailedAndReported;
            }
        }

        return LoadOutcome.Loaded;
    }

    /// <summary>
    /// <c>RestoreAutosaveState</c> with an autosave file: the
    /// text comes from <paramref name="textPath"/> (raw UTF-16 LE, NULs kept, no <c>.LOG</c>
    /// stamp), while path, title and encoding are the document's, and the text is marked dirty.
    /// Deliberate differences (parity.md): a failed load leaves the untitled document as it was
    /// and returns false so the caller keeps the autosave data (the original still takes the
    /// document's name, marks the empty text dirty and deletes the data); the autosave file is
    /// not added to Recent Documents.
    /// </summary>
    /// <param name="documentPath">Null for an untitled document.</param>
    /// <returns>False when the text could not be loaded; the user has been told.</returns>
    public bool RestoreAutosavedText(string textPath, string? documentPath, TextEncodingKind encoding)
    {
        ArgumentNullException.ThrowIfNull(textPath);
        var outcome = Read(textPath, TextEncodingKind.Utf16LE, autosaved: true, out var document);
        if (outcome == LoadOutcome.OpenFailed)
        {
            ShowFileError(textPath, LastOpenError);
        }

        if (outcome != LoadOutcome.Loaded)
        {
            return false;
        }

        if (!_editor.LoadBuffer(document.Buffer, document.Length))
        {
            ShowMessage(StringKeys.FileTooLarge, textPath, MessageKind.Warning);
            ResetToUntitled();
            return false;
        }

        State.Path = documentPath;
        State.IsUntitled = documentPath is null;
        State.CurrentEncoding = encoding;
        _editor.Modified = true;
        UpdateTitle();
        _d.StatusBar.UpdateAll();
        return true;
    }

    /// <summary>File > Save: a named document is written with the current encoding; when that
    /// fails (read-only file, for example) it falls through to Save As without an error.</summary>
    public void Save()
    {
        if (!State.IsUntitled && State.Path is not null && SaveFile(State.Path, isSaveAs: false, State.CurrentEncoding).Succeeded)
        {
            UpdateTitle();
            return;
        }

        SaveAs();
    }

    /// <summary>File > Save As: suggested name <c>*.txt</c> when untitled; a failed save shows the file error.</summary>
    public void SaveAs()
    {
        var suggestion = State.IsUntitled || State.Path is null ? UntitledSuggestion : State.Path;
        var result = ShowSaveAs(suggestion);
        if (result.Outcome != DialogOutcome.Ok || result.Path is null)
        {
            ReportDialogFailure(result);
            return;
        }

        var saved = SaveFile(result.Path, isSaveAs: true, result.Encoding);
        if (saved.Succeeded)
        {
            SetPath(result.Path);
        }
        else if (!saved.CancelledByUser)
        {
            ShowFileError(result.Path, saved.Win32Error);
        }
    }

    /// <summary>The Save As dialog keeps <see cref="DocumentState.IsSaveDialogOpen"/> set while it is up.</summary>
    private FileDialogResult ShowSaveAs(string suggestion)
    {
        State.IsSaveDialogOpen = true;
        try
        {
            return _d.Dialogs.ShowSaveAs(suggestion, InitialSaveAsEncoding());
        }
        finally
        {
            State.IsSaveDialogOpen = false;
        }
    }

    /// <summary>Save As preselects the current encoding, or the default one when it is 0.</summary>
    public TextEncodingKind InitialSaveAsEncoding() =>
        State.CurrentEncoding == TextEncodingKind.Detect ? State.DefaultEncoding : State.CurrentEncoding;

    private void SetPath(string path)
    {
        State.Path = path;
        State.IsUntitled = false;
        UpdateTitle();
        _d.RecentFiles.Add(path);
    }

    /// <summary>
    /// <c>SaveFile</c>, without EFS/EDP: in-place
    /// overwrite, unchecked BOM write, string 27 before a lossy ANSI save (accepting it converts
    /// with best-fit and reloads the file), <c>SetEndOfFile</c>, modified flag cleared. With word
    /// wrap on, the selection is set to (0, 0) during the write and restored afterwards. A failed
    /// save deletes the file only when this save created it.
    /// </summary>
    public SaveResult SaveFile(string path, bool isSaveAs, TextEncodingKind encoding)
    {
        ArgumentNullException.ThrowIfNull(path);
        IWritableFile file;
        try
        {
            file = _d.FileSystem.OpenForSave(path, shareWrite: isSaveAs);
        }
        catch (IOException e)
        {
            return new(false, FileErrors.Win32ErrorOf(e));
        }

        var wordWrap = _editor.WordWrap;
        var selection = default(TextRange);
        if (wordWrap)
        {
            selection = _editor.Selection;
            _editor.SetSelection(0, 0);
        }

        var acceptedLoss = false;
        var error = 0;
        var cancelled = false;
        var written = false;
        try
        {
            using var lease = _editor.AcquireText();
            var text = lease.Text;
            try
            {
                file.Write(TextEncoder.GetPreamble(encoding));
            }
            catch (IOException)
            {
                // The original does not check the result of the BOM write.
            }

            var needsConfirmation = _d.Encoder.RequiresLossConfirmation(text, encoding);
            if (needsConfirmation)
            {
                var answer = _d.Dialogs.ShowMessage(MessageText.InsertArgument(_d.Strings.GetString(StringKeys.UnicodeLossWarning), path), MessageKind.WarningOkCancel);
                acceptedLoss = answer == MessageResult.Ok;
                cancelled = !acceptedLoss;
            }

            if (!cancelled)
            {
                _d.Encoder.WriteBody(text, encoding, allowBestFit: acceptedLoss, file);
                file.SetEndOfFile();
                written = true;
            }
        }
        catch (IOException e)
        {
            error = FileErrors.Win32ErrorOf(e);
        }

        if (!written)
        {
            var created = !file.AlreadyExisted;
            file.Dispose();
            if (created)
            {
                TryDelete(path);
            }

            if (wordWrap)
            {
                _editor.SetSelection(selection.Start, selection.End);
            }

            return new(false, error, cancelled);
        }

        file.Dispose();
        State.CurrentEncoding = encoding;
        _editor.Modified = false;
        State.IsUntitled = false;
        if (encoding == TextEncodingKind.Ansi && acceptedLoss)
        {
            // Shows what was really written; a .LOG file gets one more stamp here.
            Load(path, TextEncodingKind.Ansi);
        }

        if (wordWrap)
        {
            _editor.SetSelection(selection.Start, selection.End);
        }

        _d.AccessPolicy.AfterSave(path);
        _d.StatusBar.UpdateAll();
        return new(true, 0);
    }

    private void TryDelete(string path)
    {
        try
        {
            _d.FileSystem.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// <c>ShowFileError</c>: access denied shows string 26 with the file name; any other error shows
    /// the system message alone; when there is none, string 1 with the file name.
    /// </summary>
    public void ShowFileError(string path, int win32Error)
    {
        if (win32Error == FileErrors.AccessDenied)
        {
            ShowMessage(StringKeys.AccessDenied, path, MessageKind.Warning);
            return;
        }

        var system = _d.SystemMessages.Find(win32Error);
        if (!string.IsNullOrEmpty(system))
        {
            _d.Dialogs.ShowMessage(system, MessageKind.Warning);
            return;
        }

        ShowMessage(StringKeys.CannotOpenFile, path, MessageKind.Warning);
    }

    /// <summary>
    /// A file dialog that failed for a reason other than Cancel: out of memory shows string 7.
    /// Deliberate difference (parity.md): the original falls back to the old
    /// <c>GetOpenFileNameW</c>/<c>GetSaveFileNameW</c> dialogs; the port shows string 10.
    /// </summary>
    private void ReportDialogFailure(FileDialogResult result)
    {
        if (result.Outcome != DialogOutcome.Failed)
        {
            return;
        }

        var key = result.HResult == FileDialogResult.OutOfMemory ? StringKeys.NotEnoughMemory : StringKeys.FileDialogsFailed;
        _d.Dialogs.ShowMessage(_d.Strings.GetString(key), MessageKind.SystemError);
    }

    private void ShowMessage(string key, string path, MessageKind kind) =>
        _d.Dialogs.ShowMessage(MessageText.InsertArgument(_d.Strings.GetString(key), path), kind);
}
