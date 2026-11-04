using NotepadNet.Core.Autosave;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Features;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Printing;
using NotepadNet.Core.Search;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Core.StatusBar;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>Document service, commands and every fake wired together like the app does.</summary>
internal sealed class Harness
{
    public Harness(string text = "", ManagedCodePageConverter? converter = null, AppOptions? options = null, ITextBufferFactory? buffers = null)
    {
        Editor = new FakeEditor(text);
        Converter = converter ?? new ManagedCodePageConverter(1252);
        var detector = new EncodingDetector(Heuristic);
        State = new DocumentState(Settings.DefaultEncoding);
        StatusBar = new StatusBarPresenter(StatusView, Editor, ResxStrings.Instance, () => State.CurrentEncoding);
        TimeDate = new TimeDateInserter(Clock, new FakeDateTimeFormatter());
        RecentFiles = new RecentFilesService(RecentStore);
        Documents = new DocumentService(
            new DocumentServiceDependencies(
                Editor, Window, Files, buffers ?? ManagedTextBufferFactory.Instance, detector,
                new DocumentLoader(detector, new TextDecoder(Converter)), new TextEncoder(Converter),
                Dialogs, ResxStrings.Instance, new FakeFileTitles(), SystemMessages, AccessPolicy, Shell, TimeDate, StatusBar, RecentFiles),
            State);
        Search = new SearchService(new(Editor, Settings, Searcher, FindDialog, Dialogs, StatusBar, ResxStrings.Instance, Window));
        GoTo = new GoToService(Editor, new FakeDigitFolder());
        Printing = new PrintService(new(
            Editor, Documents, Window, Settings, Printers, Dialogs, ResxStrings.Instance, Clock, new FakeDateTimeFormatter(), new FakeFileTitles(), SystemMessages));
        AutosaveStore = new FakeAutosaveStore(Files);
        Autosave = new AutosaveService(Documents, Editor, AutosaveStore, ProcessDirectory, Dialogs, ResxStrings.Instance, SessionId);
        Registry = BuiltInModules.CreateRegistry(BuiltInModules.All);
        Context = new CommandContext(
            Editor, Documents, Window, Dialogs, Clipboard, Shell, ResxStrings.Instance, Settings, StatusBar, TimeDate,
            options ?? new AppOptions("https://example.invalid/help", null), Search, GoTo, Printing, RecentFiles);
    }

    public FakeEditor Editor { get; }

    public InMemoryRecentFilesStore RecentStore { get; } = new();

    public RecentFilesService RecentFiles { get; }

    public const string SessionId = "1A2B3C4D-5E6F-7a8b-9C0D-EF1234567890";

    public FakeAutosaveStore AutosaveStore { get; }

    public FakeProcessDirectory ProcessDirectory { get; } = new();

    public AutosaveService Autosave { get; }

    public FakePrinterService Printers { get; } = new();

    public PrintService Printing { get; }

    public FakeWindow Window { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public MemoryFileSystem Files { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public FakeStatusView StatusView { get; } = new();

    public FakeSystemMessages SystemMessages { get; } = new();

    public FakeAccessPolicy AccessPolicy { get; } = new();

    public FakeUnicodeHeuristic Heuristic { get; } = new();

    public FixedClock Clock { get; } = new(new DateTime(2026, 10, 29, 14, 44, 0));

    public ManagedCodePageConverter Converter { get; }

    public NotepadSettings Settings { get; } = new();

    public DocumentState State { get; }

    public StatusBarPresenter StatusBar { get; }

    public TimeDateInserter TimeDate { get; }

    public DocumentService Documents { get; }

    public FakeSearcher Searcher { get; } = new();

    public FakeFindDialog FindDialog { get; } = new();

    public SearchService Search { get; }

    public GoToService GoTo { get; }

    public CommandRegistry Registry { get; }

    public CommandContext Context { get; }

    public bool Run(CommandId id) => Registry.TryExecute(id, Context);

    public bool CanRun(CommandId id) => Registry.Find(id)!.CanExecute(Context);

    /// <summary>Makes the document a saved file with the given text.</summary>
    public void OpenSaved(string path, string text, TextEncodingKind encoding = TextEncodingKind.Utf8)
    {
        Files.Files[path] = System.Text.Encoding.UTF8.GetBytes(text);
        Documents.Load(path, encoding).Should().Be(LoadOutcome.Loaded);
    }
}
