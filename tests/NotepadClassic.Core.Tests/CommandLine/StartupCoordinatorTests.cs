using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.IO;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Tests.Fakes;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Tests.CommandLine;

/// <summary>Command line handling at startup: <c>HandleSetupArg</c> (<c>/.SETUP</c>), <c>HandlePrintArgs</c>.</summary>
public sealed class StartupCoordinatorTests
{
    private const string Path = @"C:\w\a.txt";

    private readonly Harness _h = new();
    private readonly FakeHost _host = new();

    private StartupResult Run(string arguments, bool untrusted = false) =>
        new StartupCoordinator(_h.Documents, _h.Files, new Paths(), _h.Dialogs, ResxStrings.Instance, _host)
            .Run(CommandLineParser.Parse(arguments, untrusted));

    private static string Text(string key, string path = Path) => MessageText.InsertArgument(ResxStrings.Instance.GetString(key), path);

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void No_arguments_leave_the_untitled_document()
    {
        Run("").Should().Be(StartupResult.Run);

        _h.State.IsUntitled.Should().BeTrue();
        _h.Dialogs.Messages.Should().BeEmpty();
    }

    [Theory]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    [InlineData("", 1)]
    [InlineData("/A", 1)]
    [InlineData("/.NEWWINDOW", 0)]
    [InlineData("/P", 0)]
    [InlineData("RestartByRestartManager:x", 0)]
    public void Only_a_start_without_a_task_takes_a_closed_note(string arguments, int restores)
    {
        Run(arguments).Should().Be(StartupResult.Run);

        _host.ClosedNoteRestores.Should().Be(restores);
    }

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void New_window_is_a_file_name_for_an_untrusted_source()
    {
        const string named = @"C:\w\/.NEWWINDOW";
        _h.Files.Files[named] = "text"u8.ToArray();

        Run("/.NEWWINDOW", untrusted: true);

        _h.State.Path.Should().Be(named);
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void An_existing_file_opens_with_the_forced_encoding()
    {
        _h.Files.Files[Path] = [0x41, 0x00, 0x42, 0x00];

        Run("/W a.txt").Should().Be(StartupResult.Run);

        _h.State.Path.Should().Be(Path);
        _h.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16LE);
        _h.Editor.Text.Should().Be("AB");
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void A_missing_file_asks_to_create_it_and_yes_creates_and_opens_it()
    {
        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.Yes);

        Run("a").Should().Be(StartupResult.Run);

        _h.Dialogs.Messages.Should().Equal((Text(StringKeys.CannotFindCreate), MessageKind.WarningYesNoCancel));
        _h.Files.Files.Should().ContainKey(Path).WhoseValue.Should().BeEmpty();
        _h.Files.Saves.Should().Equal((Path, true));
        _h.State.Path.Should().Be(Path);
        _h.State.IsUntitled.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void No_keeps_an_empty_document_and_cancel_ends_the_program()
    {
        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.No);
        Run("a.txt").Should().Be(StartupResult.Run);
        _h.State.IsUntitled.Should().BeTrue();
        _h.Files.Files.Should().BeEmpty();

        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.Cancel);
        Run("a.txt").Should().Be(StartupResult.Exit);
        _h.Files.Files.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Other_open_errors_show_the_file_error()
    {
        _h.Files.ReadErrors[Path] = FileErrors.AccessDenied;

        Run("a.txt").Should().Be(StartupResult.Run);

        _h.Dialogs.Messages.Should().Equal((Text(StringKeys.AccessDenied), MessageKind.Warning));
        _h.State.IsUntitled.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void A_file_that_cannot_be_created_shows_the_file_error()
    {
        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.Yes);
        _h.Files.SaveErrors[Path] = FileErrors.AccessDenied;

        Run("a.txt").Should().Be(StartupResult.Run);

        _h.Dialogs.Messages.Should().HaveCount(2);
        _h.Dialogs.Messages[1].Should().Be((Text(StringKeys.AccessDenied), MessageKind.Warning));
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void A_created_file_that_still_cannot_be_read_shows_the_file_error()
    {
        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.Yes);
        _h.Files.ReadErrors[Path] = FileErrors.FileNotFound;

        Run("a.txt");

        _h.Dialogs.Messages.Should().HaveCount(2);
        _h.State.IsUntitled.Should().BeTrue();
    }

    // ---- /.SETUP ----

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Setup_enters_setup_mode_and_opens_the_file()
    {
        _h.Files.Files[Path] = "x"u8.ToArray();

        Run("/.SETUP a.txt").Should().Be(StartupResult.Run);

        _host.SetupMode.Should().BeTrue();
        _h.State.Path.Should().Be(Path);
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Setup_without_a_file_only_enters_setup_mode()
    {
        Run("/.SETUP").Should().Be(StartupResult.Run);

        _host.SetupMode.Should().BeTrue();
        _h.State.IsUntitled.Should().BeTrue();
        _h.Files.Saves.Should().BeEmpty();
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData(MessageResult.Yes, true)]
    [InlineData(MessageResult.No, false)]
    [InlineData(MessageResult.Cancel, false)]
    public void Setup_asks_to_create_and_cancel_does_not_end_the_program(MessageResult answer, bool created)
    {
        _h.Dialogs.MessageAnswers.Enqueue(answer);

        Run("/.SETUP a.txt").Should().Be(StartupResult.Run);

        _h.Files.Files.ContainsKey(Path).Should().Be(created);
        _h.State.IsUntitled.Should().Be(!created);
        // The port does not retry "/.SETUP a.txt" as a file name (deliberate difference).
        _h.Dialogs.Messages.Should().ContainSingle();
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData(FileErrors.AccessDenied, StringKeys.AccessDenied)]
    [InlineData(FileErrors.InvalidName, StringKeys.InvalidFileName)]
    [InlineData(FileErrors.SharingViolation, StringKeys.CannotOpenFile)]
    public void Setup_open_errors_use_an_ok_cancel_box(int error, string key)
    {
        _h.Files.ReadErrors[Path] = error;

        Run("/.SETUP a.txt");

        _h.Dialogs.Messages.Should().Equal((Text(key), MessageKind.WarningOkCancel));
    }

    // ---- /P, /PT ----

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Print_loads_prints_to_the_default_printer_and_closes()
    {
        _h.Files.Files[Path] = "x"u8.ToArray();

        Run("/P a.txt").Should().Be(StartupResult.Run);

        _h.State.Path.Should().Be(Path);
        _host.Prints.Should().Equal(new PrintRequest(null));
        _host.CloseRequests.Should().Be(1);
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Print_to_uses_the_named_printer()
    {
        _h.Files.Files[Path] = "x"u8.ToArray();

        Run("/PT \"a.txt\" \"Microsoft Print to PDF\"");

        _host.Prints.Should().Equal(new PrintRequest("Microsoft Print to PDF"));
        _host.CloseRequests.Should().Be(1);
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData(FileErrors.FileNotFound, StringKeys.CannotFindCreate)]
    [InlineData(FileErrors.AccessDenied, StringKeys.AccessDenied)]
    [InlineData(FileErrors.NetworkAccessDenied, StringKeys.AccessDenied)]
    [InlineData(FileErrors.InvalidName, StringKeys.InvalidFileName)]
    [InlineData(FileErrors.SharingViolation, StringKeys.CannotOpenFile)]
    public void Print_open_errors_show_an_ok_box_and_still_close(int error, string key)
    {
        if (error != FileErrors.FileNotFound)
        {
            _h.Files.ReadErrors[Path] = error;
        }

        Run("/P a.txt");

        _h.Dialogs.Messages.Should().Equal((Text(key), MessageKind.Warning));
        _host.Prints.Should().BeEmpty();
        _host.CloseRequests.Should().Be(1);
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void A_file_too_large_to_load_is_not_printed()
    {
        _h.Files.Files[Path] = "x"u8.ToArray();
        _h.Files.ReportedLength = 0x4000_0000;

        Run("/P a.txt");

        _host.Prints.Should().BeEmpty();
        _h.Dialogs.Messages.Should().ContainSingle();
        _host.CloseRequests.Should().Be(1);
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("/P")]
    [InlineData("/PT a.txt \"p\"")]
    public void Incomplete_print_arguments_do_nothing(string arguments)
    {
        Run(arguments).Should().Be(StartupResult.Run);

        _h.Dialogs.Messages.Should().BeEmpty();
        _host.CloseRequests.Should().Be(0);
        _h.State.IsUntitled.Should().BeTrue();
    }

    // ---- RestartByRestartManager: ----

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Restart_restores_only_a_36_character_id_and_never_opens_a_file()
    {
        Run("RestartByRestartManager:0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C");
        Run("RestartByRestartManager:bad");

        _host.Restores.Should().Equal("0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C");
        _h.Dialogs.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Untrusted_source_opens_options_as_a_file_name()
    {
        _h.Dialogs.MessageAnswers.Enqueue(MessageResult.No);

        Run("/.SETUP a.txt", untrusted: true);

        _host.SetupMode.Should().BeFalse();
        _h.Dialogs.Messages.Should().ContainSingle()
            .Which.Text.Should().Be(Text(StringKeys.CannotFindCreate, @"C:\w\/.SETUP a.txt"));
    }

    // ---- Drag and drop ----

    [Fact]
    [Trait("Doc", "parity.md#keo-tha")]
    public void A_dropped_file_asks_to_save_then_opens_with_detection()
    {
        _h.Editor.Type("changed");
        _h.Editor.Modified = true;
        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
        _h.Files.Files[Path] = [0xFF, 0xFE, 0x41, 0x00];

        _h.Documents.OpenDropped(Path);

        _h.Dialogs.SaveChangesQuestions.Should().ContainSingle();
        _h.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16LE);
        _h.Editor.Text.Should().Be("A");
    }

    [Fact]
    [Trait("Doc", "parity.md#keo-tha")]
    public void A_drop_is_abandoned_on_cancel_and_open_errors_show_the_file_error()
    {
        _h.Editor.Type("changed");
        _h.Editor.Modified = true;
        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);
        _h.Files.Files[Path] = "x"u8.ToArray();
        _h.Documents.OpenDropped(Path);
        _h.Editor.Text.Should().Be("changed");

        _h.Editor.Modified = false;
        _h.State.CurrentEncoding = TextEncodingKind.Utf16BE;
        _h.Documents.OpenDropped(@"C:\w\missing.txt");
        _h.Dialogs.Messages.Should().ContainSingle();
        _h.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16BE);
    }

    private sealed class Paths : IPathService
    {
        public bool FindFirstFileSucceeds(string path) => false;

        public string GetFullPath(string path) => @"C:\w\" + path;
    }

    private sealed class FakeHost : IStartupHost
    {
        public bool SetupMode { get; private set; }

        public int CloseRequests { get; private set; }

        public List<PrintRequest> Prints { get; } = [];

        public List<string> Restores { get; } = [];

        public void EnterSetupMode() => SetupMode = true;

        public void RequestClose() => CloseRequests++;

        public void Print(PrintRequest request) => Prints.Add(request);

        public void Restore(string restartId) => Restores.Add(restartId);

        public int ClosedNoteRestores { get; private set; }

        public void RestoreClosedNote() => ClosedNoteRestores++;
    }
}
