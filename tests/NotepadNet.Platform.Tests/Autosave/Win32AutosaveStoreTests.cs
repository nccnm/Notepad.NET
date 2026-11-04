using System.Text;
using NotepadNet.Core.Autosave;
using NotepadNet.Platform.Autosave;

namespace NotepadNet.Platform.Tests.Autosave;

/// <summary>Autosave and Restart Manager: the autosave folder
/// (<c>CanAutosave</c>, the files of a session) and <c>RegisterApplicationRestart</c>. Runs alone:
/// it changes process-wide state (current directory, an environment variable, the restart
/// registration).</summary>
[Collection(ProcessStateTests.Name)]
public sealed class Win32AutosaveStoreTests : IDisposable
{
    private const string Id = "1A2B3C4D-5E6F-7a8b-9C0D-EF1234567890";
    private const string OtherId = "00000000-1111-2222-3333-444444444444";

    private readonly string _root = Directory.CreateTempSubdirectory("npport-autosave-").FullName;

    public Win32AutosaveStoreTests() => Store = new Win32AutosaveStore(Path.Combine(_root, "Autosave"));

    private Win32AutosaveStore Store { get; }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    [Trait("Doc", "parity.md#doi-ten")]
    public void The_notes_of_the_earlier_name_move_to_the_new_folder()
    {
        var previous = Path.Combine(_root, "Old", "Autosave");
        var current = Path.Combine(_root, "New", "Autosave");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, Id + ".txt"), "note");

        Win32AutosaveStore.TakeOverFolder(current, previous).Should().Be(current);

        File.ReadAllText(Path.Combine(current, Id + ".txt")).Should().Be("note");
        Directory.Exists(previous).Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#doi-ten")]
    public void A_note_held_by_an_old_window_keeps_the_earlier_folder_in_use()
    {
        var previous = Path.Combine(_root, "Old", "Autosave");
        var current = Path.Combine(_root, "New", "Autosave");
        Directory.CreateDirectory(previous);
        var note = Path.Combine(previous, Id + ".txt");
        using (new FileStream(note, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Win32AutosaveStore.TakeOverFolder(current, previous).Should().Be(previous);
        }

        File.Exists(note).Should().BeTrue();
        Win32AutosaveStore.TakeOverFolder(current, previous).Should().Be(current, "the next run tries again");
    }

    [Fact]
    [Trait("Doc", "parity.md#doi-ten")]
    public void An_existing_new_folder_is_left_as_it_is()
    {
        var previous = Path.Combine(_root, "Old", "Autosave");
        var current = Path.Combine(_root, "New", "Autosave");
        Directory.CreateDirectory(previous);
        Directory.CreateDirectory(current);

        Win32AutosaveStore.TakeOverFolder(current, previous).Should().Be(current);

        Directory.Exists(previous).Should().BeTrue();
        Win32AutosaveStore.TakeOverFolder(Path.Combine(_root, "None"), Path.Combine(_root, "Missing")).Should().Be(Path.Combine(_root, "None"));
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Default_folder_is_under_local_app_data()
    {
        Win32AutosaveStore.DefaultFolder.Should().EndWith(@"\Notepad.NET\Autosave");
        Win32AutosaveStore.DefaultFolder.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Can_save_creates_the_folder_and_leaves_no_probe_behind()
    {
        Store.CanSave(1).Should().BeTrue();

        Directory.Exists(Store.Folder).Should().BeTrue();
        Directory.GetFiles(Store.Folder).Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void A_lock_excludes_a_second_holder_until_it_is_released_and_leaves_no_file()
    {
        var first = Store.TryLock(Id);

        first.Should().NotBeNull();
        Store.TryLock(Id).Should().BeNull();
        using (var other = Store.TryLock(OtherId))
        {
            other.Should().NotBeNull();
        }

        first!.Dispose();
        using (var second = Store.TryLock(Id))
        {
            second.Should().NotBeNull();
        }

        Directory.GetFiles(Store.Folder).Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void A_held_lock_is_no_entry_and_delete_leaves_it_alone()
    {
        using var held = Store.TryLock(Id);
        Store.List().Should().BeEmpty();

        Store.WriteText(Id, "text");
        Store.WriteManifest(Id, "{}");
        Store.List().Should().ContainSingle().Which.Should().Match<AutosaveEntry>(e => e.HasManifest && e.HasText);

        Store.Delete(Id);

        Store.List().Should().BeEmpty();
        Store.TryLock(Id).Should().BeNull();
    }

    [Fact]
    [Trait("Doc", "parity.md#ghi-chu-chua-luu")]
    public void An_entry_without_a_text_file_does_not_report_text()
    {
        Store.WriteManifest(Id, "{}");

        Store.List().Should().ContainSingle().Which.HasText.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Can_save_refuses_more_than_the_free_space()
    {
        Store.CanSave(long.MaxValue).Should().BeFalse();
        Directory.GetFiles(Store.Folder).Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Can_save_fails_with_an_io_exception_when_the_folder_cannot_exist()
    {
        File.WriteAllText(Path.Combine(_root, "file"), "");
        var store = new Win32AutosaveStore(Path.Combine(_root, "file", "Autosave"));

        store.Invoking(s => s.CanSave(1)).Should().Throw<IOException>();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Text_is_raw_utf16_le_and_each_write_replaces_the_file_whole()
    {
        Store.WriteText(Id, "\uFEFFlong first text\0");
        Store.WriteText(Id, "é\r\n");

        File.ReadAllBytes(Store.TextPath(Id)).Should().Equal(Encoding.Unicode.GetBytes("é\r\n"));
        Store.TextPath(Id).Should().Be(Path.Combine(Store.Folder, Id + ".txt"));
        Directory.GetFiles(Store.Folder).Should().ContainSingle("no temporary file is left");
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Manifest_round_trips_as_utf8()
    {
        var json = new AutosaveManifest(1, Core.Text.TextEncodingKind.Utf8, @"C:\ă,b\c.txt", true, @"C:\").ToJson();

        Store.WriteManifest(Id, json);

        Store.ReadManifest(Id).Should().Be(json);
        File.ReadAllBytes(Path.Combine(Store.Folder, Id + ".json"))[..3].Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void A_missing_manifest_or_folder_reads_as_null()
    {
        Store.ReadManifest(Id).Should().BeNull("the folder does not exist yet");
        Store.WriteText(Id, "x");
        Store.ReadManifest(Id).Should().BeNull();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Delete_removes_every_file_of_the_session_and_nothing_else()
    {
        Store.Delete(Id);
        Store.WriteText(Id, "x");
        Store.WriteManifest(Id, "{}");
        File.WriteAllText(Path.Combine(Store.Folder, Id + ".txt.tmp"), "");
        Store.WriteManifest(OtherId, "{}");

        Store.Delete(Id);

        Directory.GetFiles(Store.Folder).Select(Path.GetFileName).Should().Equal(OtherId + ".json");
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void List_groups_files_by_session_and_skips_other_names()
    {
        Store.List().Should().BeEmpty("the folder does not exist yet");
        Store.WriteText(Id, "x");
        Store.WriteManifest(Id, "{}");
        Store.WriteText(OtherId, "y");
        File.WriteAllText(Path.Combine(Store.Folder, "notes.txt"), "");
        File.WriteAllText(Path.Combine(Store.Folder, OtherId + ".json.bak"), "");
        var old = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(Store.TextPath(OtherId), old);
        File.SetLastWriteTimeUtc(Path.Combine(Store.Folder, OtherId + ".json.bak"), old.AddDays(-1));

        var entries = Store.List().OrderBy(e => e.Id, StringComparer.Ordinal).ToList();

        entries.Select(e => (e.Id, e.HasManifest)).Should().Equal((OtherId, false), (Id, true));
        entries[0].Age.Should().BeCloseTo(TimeSpan.FromDays(3), TimeSpan.FromMinutes(5));
        entries[1].Age.Should().BeLessThan(TimeSpan.FromMinutes(5));
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Ids_that_are_not_session_ids_never_become_paths()
    {
        Store.Invoking(s => s.TextPath(@"..\..\..\..\..\..\..\..\..\..\..\x")).Should().Throw<ArgumentException>();
        Store.Invoking(s => s.Delete("x")).Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Folder_comes_from_the_environment_variable_when_set()
    {
        var previous = Environment.GetEnvironmentVariable(Win32AutosaveStore.FolderVariable);
        var current = Path.Combine(_root, "New");
        var earlier = Path.Combine(_root, "Old");
        Directory.CreateDirectory(earlier);
        try
        {
            Environment.SetEnvironmentVariable(Win32AutosaveStore.FolderVariable, _root);
            Win32AutosaveStore.FromEnvironment(current, earlier).Folder.Should().Be(_root);
            Directory.Exists(earlier).Should().BeTrue("a named folder moves nothing");

            Environment.SetEnvironmentVariable(Win32AutosaveStore.FolderVariable, null);
            Win32AutosaveStore.FromEnvironment(current, earlier).Folder.Should().Be(current);
            Directory.Exists(earlier).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Win32AutosaveStore.FolderVariable, previous);
        }
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Restart_registration_reaches_windows()
    {
        try
        {
            new Win32RestartRegistration().Register("RestartByRestartManager:" + Id).Should().BeTrue();

            Win32RestartRegistration.RegisteredCommandLine().Should().Be("RestartByRestartManager:" + Id);
        }
        finally
        {
            Win32RestartRegistration.Unregister();
        }

        Win32RestartRegistration.RegisteredCommandLine().Should().BeNull();
    }

    [Fact]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    public void Process_directory_changes_only_to_existing_folders()
    {
        var directory = new Win32ProcessDirectory();
        var previous = directory.Current!;
        try
        {
            directory.TrySetCurrent(Path.Combine(_root, "missing")).Should().BeFalse();
            directory.Current.Should().Be(previous);
            directory.TrySetCurrent(_root).Should().BeTrue();
            directory.Current.Should().Be(_root);
        }
        finally
        {
            directory.TrySetCurrent(previous).Should().BeTrue();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateTests
{
    public const string Name = "Process state";
}
