using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using NotepadNet.Core.Autosave;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Core.Text;
using NotepadNet.Platform.Autosave;

namespace NotepadNet.App.Tests;

/// <summary>Runs a test body on a fresh single-threaded apartment, as the UI thread is.</summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}

/// <summary>The real shell with scripted dialogs, a fixed clock and no side effects outside the
/// test (no URLs opened, no Recent Documents, no clipboard reads).</summary>
internal sealed class TestApp : IDisposable
{
    public TestApp(AppOptions? options = null, ISettingsStore? store = null, Action<IServiceCollection>? configure = null)
    {
        Store = store ?? new InMemorySettingsStore();
        Host = AppHost.Create(services =>
        {
            services.AddSingleton(Store);
            services.AddSingleton<IDialogService>(Dialogs);
            services.AddSingleton<IShell>(Shell);
            services.AddSingleton<IClipboard>(Clipboard);
            services.AddSingleton<IClock>(new FixedClock());
            services.AddSingleton<IDateTimeFormatter>(new FixedFormatter());
            services.AddSingleton<IAutosaveStore>(new Win32AutosaveStore(AutosaveFolder));
            services.AddSingleton<IRestartRegistration>(Restart);
            services.AddSingleton<IProcessDirectory>(Directory);
            if (options is not null)
            {
                services.AddSingleton(options);
            }

            configure?.Invoke(services);
        });
    }

    public AppHost Host { get; }

    /// <summary>Settings store; never the real registry.</summary>
    public ISettingsStore Store { get; }

    public FakeDialogs Dialogs { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    /// <summary>A fresh temporary folder, deleted on dispose.</summary>
    public string AutosaveFolder { get; } = Path.Combine(Path.GetTempPath(), "NotepadNet.Tests", Guid.NewGuid().ToString("N"));

    public FakeRestartRegistration Restart { get; } = new();

    public FakeProcessDirectory Directory { get; } = new();

    public Editor.EditorSurface Editor => Host.Window.Editor;

    public string Text
    {
        get
        {
            using var lease = Editor.AcquireText();
            return new string(lease.Text);
        }
    }

    public void Dispose()
    {
        Host.Dispose();
        if (System.IO.Directory.Exists(AutosaveFolder))
        {
            System.IO.Directory.Delete(AutosaveFolder, recursive: true);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime LocalNow => new(2026, 10, 29, 14, 44, 0);
    }

    private sealed class FixedFormatter : IDateTimeFormatter
    {
        public string FormatShortTime(DateTime localTime) => "2:44 PM";

        public string FormatShortDate(DateTime localTime) => "10/29/2026";

        public string FormatTime(DateTime localTime) => "2:44:00 PM";

        public string FormatLongDate(DateTime localTime) => "Thursday, October 29, 2026";
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public Queue<SaveChangesChoice> SaveChanges { get; } = new();

    public Queue<FileDialogResult> Open { get; } = new();

    public Queue<FileDialogResult> SaveAs { get; } = new();

    public List<string> Messages { get; } = [];

    public SaveChangesChoice AskSaveChanges(string instruction)
    {
        Messages.Add(instruction);
        return SaveChanges.Dequeue();
    }

    public Queue<FileChangedChoice> FileChanged { get; } = new();

    public FileChangedChoice AskFileChanged(string instruction)
    {
        Messages.Add(instruction);
        return FileChanged.Dequeue();
    }

    public List<string> ReloadQuestions { get; } = [];

    public Queue<bool> ReloadAnswers { get; } = new();

    public bool AskReload(string instruction, bool preferReload)
    {
        ReloadQuestions.Add(instruction);
        return ReloadAnswers.Count > 0 && ReloadAnswers.Dequeue();
    }

    public FileDialogResult ShowOpen() => Open.Dequeue();

    public FileDialogResult ShowSaveAs(string suggestedName, TextEncodingKind initialEncoding) => SaveAs.Dequeue();

    public MessageResult ShowMessage(string text, MessageKind kind)
    {
        Messages.Add(text);
        return MessageResult.Ok;
    }

    public FontSpec? ChooseFont(FontSpec current) => null;

    public void ShowAbout()
    {
    }

    /// <summary>What the user types into Go To, one entry per OK; null presses Cancel.</summary>
    public Queue<string?> GoToInputs { get; } = new();

    public List<string> GoToInitialTexts { get; } = [];

    public int? ShowGoTo(GoToDialogRequest request)
    {
        GoToInitialTexts.Add(request.InitialText);
        while (GoToInputs.Count > 0 && GoToInputs.Dequeue() is { } input)
        {
            if (request.Check(input).Line is { } line)
            {
                return line;
            }

            Messages.Add(request.ErrorText);
        }

        return null;
    }
}

internal sealed class FakeShell : IShell
{
    public List<string> Recent { get; } = [];

    public void OpenUri(string uri)
    {
    }

    public void StartNewWindow()
    {
    }

    public int NextNoteWindows { get; private set; }

    public void StartNextNoteWindow() => NextNoteWindows++;

    public void AddToRecentDocuments(string path) => Recent.Add(path);

    public bool ShowInFolder(string path) => true;
}

internal sealed class FakeClipboard : IClipboard
{
    public bool HasText { get; set; }

    public bool ContainsText() => HasText;

    public bool SetText(string text) => true;
}

/// <summary>Records the restart command line instead of registering the test host.</summary>
internal sealed class FakeRestartRegistration : IRestartRegistration
{
    public List<string> CommandLines { get; } = [];

    public bool Register(string commandLine)
    {
        CommandLines.Add(commandLine);
        return true;
    }
}

/// <summary>Keeps the current directory of the test process untouched.</summary>
internal sealed class FakeProcessDirectory : IProcessDirectory
{
    public string? Current { get; set; } = @"C:\Work";

    public bool TrySetCurrent(string path)
    {
        Current = path;
        return true;
    }
}
