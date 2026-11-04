using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Services;
using NotepadNet.Core.StatusBar;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

internal sealed class FakeDialogs : IDialogService
{
    public Queue<SaveChangesChoice> SaveChangesAnswers { get; } = new();

    public Queue<FileDialogResult> OpenAnswers { get; } = new();

    public Queue<FileDialogResult> SaveAsAnswers { get; } = new();

    public Queue<MessageResult> MessageAnswers { get; } = new();

    public List<string> SaveChangesQuestions { get; } = [];

    public List<(string Suggested, TextEncodingKind Encoding)> SaveAsRequests { get; } = [];

    public List<(string Text, MessageKind Kind)> Messages { get; } = [];

    public FontSpec? FontAnswer { get; set; }

    public int AboutShown { get; private set; }

    public int OpenShown { get; private set; }

    /// <summary>Set by the test to observe state while the save-changes dialog is up.</summary>
    public Action? DuringSaveChanges { get; set; }

    public SaveChangesChoice AskSaveChanges(string instruction)
    {
        SaveChangesQuestions.Add(instruction);
        DuringSaveChanges?.Invoke();
        return SaveChangesAnswers.Dequeue();
    }

    public Queue<FileChangedChoice> FileChangedAnswers { get; } = new();

    public List<string> FileChangedQuestions { get; } = [];

    public Action? DuringFileChanged { get; set; }

    public FileChangedChoice AskFileChanged(string instruction)
    {
        FileChangedQuestions.Add(instruction);
        DuringFileChanged?.Invoke();
        return FileChangedAnswers.Dequeue();
    }

    public Queue<bool> ReloadAnswers { get; } = new();

    public List<(string Text, bool PreferReload)> ReloadQuestions { get; } = [];

    public Action? DuringReloadQuestion { get; set; }

    public bool AskReload(string instruction, bool preferReload)
    {
        ReloadQuestions.Add((instruction, preferReload));
        DuringReloadQuestion?.Invoke();
        return ReloadAnswers.Dequeue();
    }

    public FileDialogResult ShowOpen()
    {
        OpenShown++;
        return OpenAnswers.Dequeue();
    }

    public Action? DuringSaveAs { get; set; }

    public FileDialogResult ShowSaveAs(string suggestedName, TextEncodingKind initialEncoding)
    {
        SaveAsRequests.Add((suggestedName, initialEncoding));
        DuringSaveAs?.Invoke();
        return SaveAsAnswers.Dequeue();
    }

    public MessageResult ShowMessage(string text, MessageKind kind)
    {
        Messages.Add((text, kind));
        return MessageAnswers.Count > 0 ? MessageAnswers.Dequeue() : MessageResult.Ok;
    }

    public FontSpec? ChooseFont(FontSpec current) => FontAnswer;

    public void ShowAbout() => AboutShown++;

    /// <summary>What the user types into Go To, one entry per OK; null presses Cancel.</summary>
    public Queue<string?> GoToInputs { get; } = new();

    public List<GoToDialogRequest> GoToRequests { get; } = [];

    /// <summary>Error boxes the Go To dialog showed and the text it put in the box after each.</summary>
    public List<(string Text, string Caption, string Replacement)> GoToErrors { get; } = [];

    public int? ShowGoTo(GoToDialogRequest request)
    {
        GoToRequests.Add(request);
        while (GoToInputs.Count > 0 && GoToInputs.Dequeue() is { } input)
        {
            var check = request.Check(input);
            if (check.Line is { } line)
            {
                return line;
            }

            GoToErrors.Add((request.ErrorText, request.ErrorCaption, check.ReplacementText!));
        }

        return null;
    }
}

internal sealed class FakeWindow : IMainWindow
{
    public List<string> Titles { get; } = [];

    public string Title => Titles.Count > 0 ? Titles[^1] : "";

    public int CloseRequests { get; private set; }

    public bool? StatusBarVisible { get; private set; }

    public void SetTitle(string title) => Titles.Add(title);

    public void RequestClose() => CloseRequests++;

    public void SetStatusBarVisible(bool visible) => StatusBarVisible = visible;

    public int? OpacityPercent { get; private set; }

    public void SetOpacity(int percent) => OpacityPercent = percent;

    public bool? AlwaysOnTop { get; private set; }

    public void SetAlwaysOnTop(bool onTop) => AlwaysOnTop = onTop;

    public int WaitCursors { get; private set; }

    public int WaitCursorsShowing { get; private set; }

    public IDisposable ShowWaitCursor()
    {
        WaitCursors++;
        WaitCursorsShowing++;
        return new Restore(this);
    }

    private sealed class Restore(FakeWindow owner) : IDisposable
    {
        public void Dispose() => owner.WaitCursorsShowing--;
    }
}

internal sealed class FakeStatusView : IStatusBarView
{
    public string[] Parts { get; } = ["", "", "", "", ""];

    public int Updates { get; private set; }

    public void SetPartText(int part, string text)
    {
        Updates++;
        Parts[part] = text;
    }
}

internal sealed class FakeShell : IShell
{
    public List<string> OpenedUris { get; } = [];

    public List<string> RecentDocuments { get; } = [];

    public int NewWindows { get; private set; }

    public void OpenUri(string uri) => OpenedUris.Add(uri);

    public void StartNewWindow() => NewWindows++;

    public void StartNextNoteWindow()
    {
    }

    public void AddToRecentDocuments(string path) => RecentDocuments.Add(path);

    public List<string> ShownInFolder { get; } = [];

    public bool ShowInFolderResult { get; set; } = true;

    public bool ShowInFolder(string path)
    {
        ShownInFolder.Add(path);
        return ShowInFolderResult;
    }
}

internal sealed class FakeClipboard : IClipboard
{
    public bool HasText { get; set; }

    public bool ContainsText() => HasText;

    public List<string> Written { get; } = [];

    public bool SetTextResult { get; set; } = true;

    public bool SetText(string text)
    {
        if (SetTextResult)
        {
            Written.Add(text);
        }

        return SetTextResult;
    }
}

internal sealed class FakeFileTitles : IFileTitleProvider
{
    public string GetTitle(string path) => Path.GetFileName(path);
}

internal sealed class FakeSystemMessages : ISystemErrorMessages
{
    public Dictionary<int, string> Messages { get; } = new() { [2] = "The system cannot find the file specified." };

    public string? Find(int win32Error) => Messages.GetValueOrDefault(win32Error);
}

internal sealed class FakeAccessPolicy : IFileAccessPolicy
{
    public bool Allow { get; set; } = true;

    public List<string> Saved { get; } = [];

    public bool CanOpen(string path) => Allow;

    public void AfterSave(string path) => Saved.Add(path);
}

internal sealed class FixedClock(DateTime now) : IClock
{
    public DateTime LocalNow { get; set; } = now;
}

/// <summary>en-US short formats: <c>h:mm tt</c> and <c>M/d/yyyy</c>.</summary>
internal sealed class FakeDateTimeFormatter : IDateTimeFormatter
{
    public string FormatShortTime(DateTime localTime) =>
        localTime.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public string FormatShortDate(DateTime localTime) =>
        localTime.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public string FormatTime(DateTime localTime) =>
        localTime.ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    public string FormatLongDate(DateTime localTime) =>
        localTime.ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
