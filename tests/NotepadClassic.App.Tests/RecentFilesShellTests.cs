using Microsoft.Extensions.DependencyInjection;
using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Documents;

namespace NotepadClassic.App.Tests;

/// <summary>File > Recent Files on the real menu: the submenu's state and the text of its items.</summary>
public sealed class RecentFilesShellTests
{
    private static ToolStripMenuItem Submenu(TestApp app) =>
        ((ToolStripMenuItem)app.Host.Window.MainMenuStrip!.Items[0]).DropDownItems.OfType<ToolStripMenuItem>()
            .Single(i => i.Text == "Recent &Files");

    private static string[] VisibleTexts(TestApp app) =>
        [.. Submenu(app).DropDownItems.Cast<ToolStripItem>().Where(i => i.Available).Select(i => i is ToolStripSeparator ? "-" : i.Text ?? "")];

    private static TestApp WithStore(IRecentFilesStore store) =>
        new(configure: services => services.AddSingleton(store));

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void The_submenu_is_grayed_while_the_list_is_empty_and_follows_the_stored_list() => Sta.Run(() =>
    {
        var store = new InMemoryRecentFilesStore();
        using var app = WithStore(store);

        app.Host.Menus.UpdateState();
        Submenu(app).Enabled.Should().BeFalse();

        store.Save([@"C:\docs\a.txt", @"C:\R&D\b.txt"]);
        app.Host.Menus.UpdateState();

        Submenu(app).Enabled.Should().BeTrue();
        VisibleTexts(app).Should().Equal(@"&1 C:\docs\a.txt", @"&2 C:\R&&D\b.txt", "-", "&Clear Recent Files");

        store.Save([]);
        app.Host.Menus.UpdateState();
        Submenu(app).Enabled.Should().BeFalse();
    });

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void A_full_list_numbers_its_tenth_place_with_a_zero() => Sta.Run(() =>
    {
        var store = new InMemoryRecentFilesStore();
        store.Save([.. Enumerable.Range(1, RecentFilesPolicy.MaxCount).Select(i => $@"C:\{i}.txt")]);
        using var app = WithStore(store);

        app.Host.Menus.UpdateState();

        var texts = VisibleTexts(app);
        texts[0].Should().Be(@"&1 C:\1.txt");
        texts[8].Should().Be(@"&9 C:\9.txt");
        texts[9].Should().Be(@"1&0 C:\10.txt");
        texts.Should().HaveCount(RecentFilesPolicy.MaxCount + 2);
    });

    [Fact]
    [Trait("Doc", "parity.md#file-gan-day")]
    public void Clicking_an_item_opens_its_file() => Sta.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "from the list");
        try
        {
            var store = new InMemoryRecentFilesStore();
            store.Save([path]);
            using var app = WithStore(store);
            app.Host.Menus.UpdateState();

            Submenu(app).DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Tag is CommandId.RecentFile1).PerformClick();

            app.Text.Should().Be("from the list");
            app.Host.Context.Documents.State.Path.Should().Be(path);
        }
        finally
        {
            File.Delete(path);
        }
    });
}
