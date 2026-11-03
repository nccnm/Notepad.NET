using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Documents;
using NotepadClassic.Core.Features;
using NotepadClassic.Core.IO;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Settings;
using NotepadClassic.Core.Tests.Fakes;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Tests.Documents;

/// <summary>File > Recent Files: the list rules, its registry value, what adds to it and what opens from it.</summary>
public sealed class RecentFilesTests
{
    private const string A = @"C:\docs\a.txt";
    private const string B = @"C:\docs\b.txt";

    private static string Numbered(int i) => $@"C:\docs\{i}.txt";

    // ---- The list ----

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_path_goes_first_and_an_older_entry_for_it_goes_away_whatever_its_case()
    {
        IReadOnlyList<string> list = [A, B];

        RecentFilesPolicy.Add(list, B).Should().Equal(B, A);
        RecentFilesPolicy.Add(list, A.ToUpperInvariant()).Should().Equal(A.ToUpperInvariant(), B);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_oldest_entry_goes_past_the_limit()
    {
        IReadOnlyList<string> list = [];
        for (var i = 0; i < RecentFilesPolicy.MaxCount + 1; i++)
        {
            list = RecentFilesPolicy.Add(list, Numbered(i));
        }

        list.Should().HaveCount(RecentFilesPolicy.MaxCount);
        list[0].Should().Be(Numbered(RecentFilesPolicy.MaxCount));
        list.Should().NotContain(Numbered(0));
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_path_that_cannot_be_stored_leaves_the_list_as_it_was()
    {
        IReadOnlyList<string> list = [A];

        RecentFilesPolicy.Add(list, @"C:\" + new string('x', RecentFilesPolicy.MaxPathLength)).Should().Equal(A);
        RecentFilesPolicy.Add(list, @"C:\a|b.txt").Should().Equal(A);
        RecentFilesPolicy.Add(list, "").Should().Equal(A);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Remove_ignores_case_and_an_unknown_path()
    {
        RecentFilesPolicy.Remove([A, B], A.ToUpperInvariant()).Should().Equal(B);
        RecentFilesPolicy.Remove([A], B).Should().Equal(A);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_stored_value_drops_what_could_not_have_been_written()
    {
        RecentFilesPolicy.Parse("").Should().BeEmpty();
        RecentFilesPolicy.Parse($"{A}||relative.txt|{A.ToUpperInvariant()}|{B}").Should().Equal(A, B);
        RecentFilesPolicy.Parse(string.Join('|', Enumerable.Range(0, 15).Select(Numbered))).Should().HaveCount(RecentFilesPolicy.MaxCount);
        RecentFilesPolicy.Parse(@"C:\" + new string('x', RecentFilesPolicy.MaxPathLength)).Should().BeEmpty();
    }

    // ---- Menu text ----

    [Theory]
    [Trait("Doc", "parity.md#file-gan-day")]
    [InlineData(0, "&1")]
    [InlineData(8, "&9")]
    [InlineData(9, "1&0")]
    public void Each_place_has_an_access_number(int index, string expected) =>
        RecentFilesPolicy.AccessNumber(index).Should().Be(expected);

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_short_path_shows_whole_with_an_ampersand_shown_as_itself() =>
        RecentFilesPolicy.DisplayPath(@"C:\R&D\a.txt").Should().Be(@"C:\R&&D\a.txt");

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_path_of_exactly_the_limit_shows_whole()
    {
        var path = @"C:\" + new string('x', RecentFilesPolicy.MaxDisplayLength - 3);

        RecentFilesPolicy.DisplayPath(path).Should().Be(path);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_long_path_keeps_its_root_and_as_much_of_its_end_as_fits()
    {
        var path = @"C:\" + string.Join('\\', Enumerable.Repeat("folder-name", 10)) + @"\last\name.txt";

        var shown = RecentFilesPolicy.DisplayPath(path);

        shown.Should().StartWith(@"C:\...\");
        shown.Should().EndWith(@"\folder-name\last\name.txt");
        shown.Length.Should().BeLessThanOrEqualTo(RecentFilesPolicy.MaxDisplayLength);
        shown.Length.Should().BeGreaterThan(RecentFilesPolicy.MaxDisplayLength - 13, "one more folder would not have fit");
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_name_too_long_to_fit_keeps_its_end()
    {
        var path = @"C:\docs\" + new string('n', 100) + ".txt";

        var shown = RecentFilesPolicy.DisplayPath(path);

        shown.Should().StartWith("...n").And.EndWith("n.txt");
        shown.Length.Should().Be(RecentFilesPolicy.MaxDisplayLength);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_long_text_without_a_folder_keeps_its_end()
    {
        var shown = RecentFilesPolicy.DisplayPath(new string('n', 100));

        shown.Should().StartWith("...").And.HaveLength(RecentFilesPolicy.MaxDisplayLength);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Shortening_a_rootless_path_can_take_every_folder_it_has()
    {
        var path = new string('x', 40) + @"\" + new string('y', 40);

        RecentFilesPolicy.DisplayPath(path).Should().Be(@"...\" + new string('y', 40));
    }

    // ---- The registry value ----

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_list_is_one_string_value_that_a_second_store_reads_back()
    {
        var registry = new MemorySettingsRegistry();

        new RegistryRecentFilesStore(registry).Save([A, B]);

        registry.User![SettingsSchema.RecentFiles].Data.Should().Be($"{A}|{B}");
        new RegistryRecentFilesStore(registry).Load().Should().Equal(A, B);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_full_list_of_the_longest_paths_fits_its_read_buffer()
    {
        var registry = new MemorySettingsRegistry();
        var store = new RegistryRecentFilesStore(registry);
        var paths = Enumerable.Range(0, RecentFilesPolicy.MaxCount)
            .Select(i => $@"C:\{i}".PadRight(RecentFilesPolicy.MaxPathLength, 'x'))
            .ToList();

        store.Save(paths);

        // One separator per path but the last, plus the NUL that Win32 stores and reads back.
        ((string)registry.User![SettingsSchema.RecentFiles].Data).Length.Should().BeLessThan(RecentFilesPolicy.StoredCapacity);
        store.Load().Should().Equal(paths);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_missing_key_value_or_wrong_kind_is_an_empty_list()
    {
        var registry = new MemorySettingsRegistry();
        var store = new RegistryRecentFilesStore(registry);
        store.Load().Should().BeEmpty("the key does not exist");

        registry.SetDword(SettingsLocation.User, "other", 1);
        store.Load().Should().BeEmpty("the value does not exist");

        registry.SetDword(SettingsLocation.User, SettingsSchema.RecentFiles, 1);
        store.Load().Should().BeEmpty("the value is not a string");
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_user_key_that_cannot_be_created_only_loses_the_write()
    {
        var store = new RegistryRecentFilesStore(new MemorySettingsRegistry { CanCreate = false });

        store.Save([A]);

        store.Load().Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_original_key_is_not_imported_into_the_list()
    {
        SettingsSchema.ImportedStrings.Select(s => s.Name).Should().NotContain(SettingsSchema.RecentFiles);
    }

    // ---- The service ----

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Nothing_is_read_until_a_menu_asks()
    {
        var store = new InMemoryRecentFilesStore();
        store.Save([A]);
        var service = new RecentFilesService(store);

        service.Items.Should().BeEmpty();

        service.Refresh();
        service.Items.Should().Equal(A);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_change_keeps_what_another_window_stored_meanwhile()
    {
        var store = new InMemoryRecentFilesStore();
        var one = new RecentFilesService(store);
        var other = new RecentFilesService(store);

        one.Add(A);
        other.Add(B);
        one.Add(Numbered(1));

        one.Items.Should().Equal(Numbered(1), B, A);
        other.Refresh();
        other.Items.Should().Equal(Numbered(1), B, A);

        other.Remove(B);
        one.Refresh();
        one.Items.Should().Equal(Numbered(1), A);

        one.Clear();
        other.Refresh();
        other.Items.Should().BeEmpty();
    }

    // ---- What adds to the list ----

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_load_adds_and_a_failed_one_does_not()
    {
        var h = new Harness();
        h.OpenSaved(A, "text");
        h.RecentStore.Load().Should().Equal(A);

        h.Documents.Load(B, TextEncodingKind.Detect).Should().Be(LoadOutcome.OpenFailed);

        h.RecentStore.Load().Should().Equal(A);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_refused_or_too_large_load_does_not_add()
    {
        var h = new Harness();
        h.Files.Files[A] = "text"u8.ToArray();
        h.AccessPolicy.Allow = false;

        h.Documents.Load(A, TextEncodingKind.Detect).Should().Be(LoadOutcome.FailedAndReported);

        h.RecentStore.Load().Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Save_as_adds_the_new_name_and_a_plain_save_changes_nothing()
    {
        var h = new Harness("text");
        h.Dialogs.SaveAsAnswers.Enqueue(FileDialogResult.Ok(B, TextEncodingKind.Utf8));

        h.Documents.SaveAs();
        h.RecentStore.Load().Should().Equal(B);

        h.RecentStore.Save([A, B]);
        h.Documents.Save();
        h.RecentStore.Load().Should().Equal(A, B);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_failed_save_as_does_not_add()
    {
        var h = new Harness("text");
        h.Files.SaveErrors[B] = FileErrors.AccessDenied;
        h.Dialogs.SaveAsAnswers.Enqueue(FileDialogResult.Ok(B, TextEncodingKind.Utf8));

        h.Documents.SaveAs();

        h.RecentStore.Load().Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Restoring_autosaved_text_does_not_add()
    {
        var h = new Harness();
        h.Files.Files[@"C:\autosave\1.txt"] = "t\0"u8.ToArray();

        h.Documents.RestoreAutosavedText(@"C:\autosave\1.txt", A, TextEncodingKind.Utf8).Should().BeTrue();

        h.RecentStore.Load().Should().BeEmpty();
    }

    // ---- The commands ----

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_submenu_is_grayed_and_every_place_hidden_while_the_list_is_empty()
    {
        var h = new Harness();
        var menu = h.Registry.MenusUnder(FileModule.Menu).Single(m => m.Path == FileModule.RecentMenu);

        h.Registry.RefreshMenuState(h.Context);

        menu.IsEnabled!(h.Context).Should().BeFalse();
        Place(h, 0).IsVisible!(h.Context).Should().BeFalse();
        h.CanRun(CommandId.RecentFile1).Should().BeFalse();
        h.Run(CommandId.RecentFile1).Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_menu_refresh_shows_what_another_window_stored()
    {
        var h = new Harness();
        var menu = h.Registry.MenusUnder(FileModule.Menu).Single(m => m.Path == FileModule.RecentMenu);
        h.RecentStore.Save([A, @"C:\R&D\b.txt"]);

        h.Registry.RefreshMenuState(h.Context);

        menu.IsEnabled!(h.Context).Should().BeTrue();
        Place(h, 0).DynamicText!(h.Context).Should().Be($"&1 {A}");
        Place(h, 1).DynamicText!(h.Context).Should().Be(@"&2 C:\R&&D\b.txt");
        Place(h, 1).IsVisible!(h.Context).Should().BeTrue();
        Place(h, 2).IsVisible!(h.Context).Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Every_place_opens_its_own_file_and_moves_it_first()
    {
        var h = new Harness();
        var paths = Enumerable.Range(0, RecentFilesPolicy.MaxCount).Select(Numbered).ToList();
        foreach (var path in paths)
        {
            h.Files.Files[path] = System.Text.Encoding.UTF8.GetBytes(path);
        }

        for (var place = 0; place < paths.Count; place++)
        {
            h.RecentStore.Save(paths);
            h.Registry.RefreshMenuState(h.Context);

            h.Run(CommandId.RecentFile1 + place).Should().BeTrue();

            h.State.Path.Should().Be(paths[place]);
            h.Editor.Text.Should().Be(paths[place]);
            h.RecentFiles.Items[0].Should().Be(paths[place]);
        }
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Opening_detects_the_encoding()
    {
        var h = new Harness();
        h.Files.Files[A] = [0xFF, 0xFE, (byte)'x', 0];
        h.RecentStore.Save([A]);
        h.Registry.RefreshMenuState(h.Context);

        h.Run(CommandId.RecentFile1);

        h.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16LE);
        h.Editor.Text.Should().Be("x");
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Cancel_at_the_save_question_keeps_the_text_and_opens_nothing()
    {
        var h = new Harness();
        h.OpenSaved(A, "old");
        h.Files.Files[B] = "new"u8.ToArray();
        h.RecentStore.Save([B, A]);
        h.Registry.RefreshMenuState(h.Context);
        h.Editor.Modified = true;
        h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);

        h.Run(CommandId.RecentFile1);

        h.Dialogs.SaveChangesQuestions.Should().ContainSingle();
        h.State.Path.Should().Be(A);
        h.Editor.Text.Should().Be("old");
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_file_that_is_gone_reports_the_error_keeps_the_document_and_leaves_the_list()
    {
        var h = new Harness();
        h.OpenSaved(A, "old");
        h.RecentStore.Save([B, A]);
        h.Registry.RefreshMenuState(h.Context);

        h.Run(CommandId.RecentFile1);

        h.Dialogs.Messages.Should().ContainSingle().Which.Text.Should().Be(h.SystemMessages.Messages[FileErrors.FileNotFound]);
        h.State.Path.Should().Be(A);
        h.Editor.Text.Should().Be("old");
        h.RecentFiles.Items.Should().Equal(A);
        h.RecentStore.Load().Should().Equal(A);
    }

    [Theory]
    [Trait("Doc", "parity.md#file-gan-day")]
    [InlineData(FileErrors.AccessDenied)]
    [InlineData(FileErrors.SharingViolation)]
    [InlineData(3)] // ERROR_PATH_NOT_FOUND: also what a drive that is not plugged in gives.
    public void A_file_that_may_come_back_stays_in_the_list(int error)
    {
        var h = new Harness();
        h.Files.ReadErrors[A] = error;
        h.RecentStore.Save([A]);
        h.Registry.RefreshMenuState(h.Context);

        h.Run(CommandId.RecentFile1);

        h.Dialogs.Messages.Should().ContainSingle();
        h.RecentStore.Load().Should().Equal(A);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Clear_empties_the_stored_list()
    {
        var h = new Harness();
        h.RecentStore.Save([A, B]);
        h.Registry.RefreshMenuState(h.Context);

        h.Run(CommandId.ClearRecentFiles).Should().BeTrue();

        h.RecentStore.Load().Should().BeEmpty();
        h.RecentFiles.Items.Should().BeEmpty();
    }

    [Fact]
    public void Menu_refreshes_are_checked_and_run_in_order()
    {
        var h = new Harness();
        var registry = new CommandRegistry();
        var calls = new List<int>();
        registry.AddMenuRefresh(_ => calls.Add(1));
        registry.AddMenuRefresh(_ => calls.Add(2));

        registry.RefreshMenuState(h.Context);

        calls.Should().Equal(1, 2);
        var act = () => registry.AddMenuRefresh(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static MenuContribution Place(Harness h, int index) =>
        h.Registry.ItemsOf(FileModule.RecentMenu).Single(i => i.Command == CommandId.RecentFile1 + index);
}
