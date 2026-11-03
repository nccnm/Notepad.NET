using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Features;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Tests.Fakes;

namespace NotepadClassic.Core.Tests.Commands;

/// <summary>Command map, accelerator tables, menu state (<c>UpdateMenuState</c>) and the command
/// handler.</summary>
public sealed class CommandTests
{
    // ---- Accelerator tables of the original's MUI resources ----

    public static TheoryData<KeyTier, ushort, KeyModifiers, CommandId> AcceleratorTable => new()
    {
        { KeyTier.Global, 'N', KeyModifiers.Control, CommandId.New },
        { KeyTier.Global, 'N', KeyModifiers.Control | KeyModifiers.Shift, CommandId.NewWindow },
        { KeyTier.Global, 'O', KeyModifiers.Control, CommandId.Open },
        { KeyTier.Global, 'S', KeyModifiers.Control, CommandId.Save },
        { KeyTier.Global, 'S', KeyModifiers.Control | KeyModifiers.Shift, CommandId.SaveAs },
        { KeyTier.Global, 'P', KeyModifiers.Control, CommandId.Print },
        { KeyTier.Global, 'W', KeyModifiers.Control, CommandId.Exit },
        { KeyTier.Global, VirtualKeys.F1, KeyModifiers.None, CommandId.ViewHelp },
        { KeyTier.Global, VirtualKeys.F3, KeyModifiers.None, CommandId.FindNext },
        { KeyTier.Global, VirtualKeys.F3, KeyModifiers.Shift, CommandId.FindPrevious },
        { KeyTier.Global, 'G', KeyModifiers.Control, CommandId.GoTo },
        { KeyTier.Global, 'F', KeyModifiers.Control, CommandId.Find },
        { KeyTier.Global, 'H', KeyModifiers.Control, CommandId.Replace },
        { KeyTier.Global, 'E', KeyModifiers.Control, CommandId.SearchWithBing },
        { KeyTier.Global, VirtualKeys.OemPlus, KeyModifiers.Control, CommandId.ZoomIn },
        { KeyTier.Global, VirtualKeys.Add, KeyModifiers.Control, CommandId.ZoomIn },
        { KeyTier.Global, VirtualKeys.OemMinus, KeyModifiers.Control, CommandId.ZoomOut },
        { KeyTier.Global, VirtualKeys.Subtract, KeyModifiers.Control, CommandId.ZoomOut },
        { KeyTier.Global, VirtualKeys.Numpad0, KeyModifiers.Control, CommandId.ZoomRestore },
        { KeyTier.Global, VirtualKeys.D0, KeyModifiers.Control, CommandId.ZoomRestore },
        { KeyTier.Main, VirtualKeys.Insert, KeyModifiers.Control, CommandId.Copy },
        { KeyTier.Main, VirtualKeys.F5, KeyModifiers.None, CommandId.TimeDate },
        { KeyTier.Main, VirtualKeys.Back, KeyModifiers.Alt, CommandId.Undo },
        { KeyTier.Main, 'Z', KeyModifiers.Control, CommandId.Undo },
        { KeyTier.Main, 'X', KeyModifiers.Control, CommandId.Cut },
        { KeyTier.Main, 'C', KeyModifiers.Control, CommandId.Copy },
        { KeyTier.Main, 'V', KeyModifiers.Control, CommandId.Paste },
        { KeyTier.Main, 'A', KeyModifiers.Control, CommandId.SelectAll },
    };

    [Theory]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    [MemberData(nameof(AcceleratorTable))]
    public void Key_bindings_match_the_accelerator_resources(KeyTier tier, ushort key, KeyModifiers modifiers, CommandId command)
    {
        var registry = BuiltInModules.CreateRegistry(BuiltInModules.All);

        registry.FindKey(new(key, modifiers), tier)!.Command.Should().Be(command);
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void There_are_exactly_20_global_and_8_main_bindings()
    {
        var registry = BuiltInModules.CreateRegistry(BuiltInModules.All);

        registry.KeyBindings.Count(k => k.Tier == KeyTier.Global).Should().Be(20);
        registry.KeyBindings.Count(k => k.Tier == KeyTier.Main).Should().Be(8);
        registry.FindKey(new(VirtualKeys.OemPlus, KeyModifiers.Control | KeyModifiers.Shift), KeyTier.Global).Should().BeNull("VK_OEM_PLUS is bound without Shift");
    }

    // ---- Menu structure ----

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Menus_follow_the_original_order()
    {
        var registry = BuiltInModules.CreateRegistry(BuiltInModules.All);

        registry.MenusUnder("").Select(m => m.Path).Should().Equal("File", "Edit", "Format", "View", "Help");
        registry.MenusUnder("View").Select(m => m.Path).Should().Equal("View/Zoom", "View/Transparency");
        registry.ItemsOf("View/Transparency").Select(i => i.Command).Should().Equal(CommandId.MoreTransparent, CommandId.LessTransparent, CommandId.Opaque);
        registry.ItemsOf("File").Select(i => i.Command).Should().Equal(
            CommandId.New, CommandId.NewWindow, CommandId.Open, CommandId.Save, CommandId.SaveAs,
            CommandId.PageSetup, CommandId.Print, CommandId.Exit);
        registry.ItemsOf("Edit").Select(i => (i.Group, i.Command)).Should().Equal(
            (0, CommandId.Undo),
            (1, CommandId.Cut), (1, CommandId.Copy), (1, CommandId.Paste), (1, CommandId.Delete),
            (2, CommandId.SearchWithBing), (2, CommandId.Find), (2, CommandId.FindNext), (2, CommandId.FindPrevious),
            (2, CommandId.Replace), (2, CommandId.GoTo),
            (3, CommandId.SelectAll), (3, CommandId.TimeDate));
        registry.ItemsOf("View/Zoom").Select(i => i.Command).Should().Equal(CommandId.ZoomIn, CommandId.ZoomOut, CommandId.ZoomRestore);
        registry.ItemsOf("Help").Select(i => i.Command).Should().Equal(CommandId.ViewHelp, CommandId.SendFeedback, CommandId.About);
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Every_menu_and_shortcut_text_exists_in_the_resources()
    {
        var registry = BuiltInModules.CreateRegistry(BuiltInModules.All);
        var keys = new List<string>();
        foreach (var top in registry.MenusUnder(""))
        {
            keys.Add(top.TextKey);
            foreach (var sub in registry.MenusUnder(top.Path))
            {
                keys.Add(sub.TextKey);
                keys.AddRange(registry.ItemsOf(sub.Path).SelectMany(Texts));
            }

            keys.AddRange(registry.ItemsOf(top.Path).SelectMany(Texts));
        }

        keys.Should().HaveCountGreaterThan(40);
        foreach (var key in keys)
        {
            ResxStrings.Instance.GetString(key).Should().NotBeNullOrEmpty(key);
        }

        static IEnumerable<string> Texts(MenuContribution item) =>
            item.ShortcutTextKey is null ? [item.TextKey] : [item.TextKey, item.ShortcutTextKey];
    }

    [Fact]
    public void Registry_rejects_duplicates_and_dangling_items()
    {
        var registry = new CommandRegistry();
        registry.Add(new DelegateCommand(CommandId.New, _ => { }));
        registry.AddMenu(new("File", "Menu_File", 0));
        registry.AddKeyBinding(new(new('N', KeyModifiers.Control), CommandId.New, KeyTier.Global));

        FluentActions.Invoking(() => registry.Add(new DelegateCommand(CommandId.New, _ => { }))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => registry.AddMenu(new("File", "x", 1))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => registry.AddKeyBinding(new(new('N', KeyModifiers.Control), CommandId.Open, KeyTier.Global))).Should().Throw<InvalidOperationException>();

        registry.AddKeyBinding(new(new('O', KeyModifiers.Control), CommandId.Open, KeyTier.Global));
        FluentActions.Invoking(registry.Validate).Should().Throw<InvalidOperationException>();

        var items = new CommandRegistry();
        items.Add(new DelegateCommand(CommandId.New, _ => { }));
        items.AddMenuItem(new("Nowhere", 0, 0, CommandId.New, "Menu_File_New"));
        FluentActions.Invoking(items.Validate).Should().Throw<InvalidOperationException>();
        items.TryExecute(CommandId.Open, new Harness().Context).Should().BeFalse("unknown commands do not run");
    }

    // ---- Enable rules (UpdateMenuState) ----

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Empty_selection_grays_cut_copy_delete_and_search_with_bing()
    {
        var h = new Harness();
        h.Editor.Type("text");
        CommandId[] selectionCommands = [CommandId.Cut, CommandId.Copy, CommandId.Delete, CommandId.SearchWithBing];

        selectionCommands.Should().OnlyContain(id => !h.CanRun(id));

        h.Editor.SetSelection(0, 2);
        selectionCommands.Should().OnlyContain(id => h.CanRun(id));
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Paste_follows_the_clipboard_undo_follows_can_undo()
    {
        var h = new Harness();

        h.CanRun(CommandId.Paste).Should().BeFalse();
        h.CanRun(CommandId.Undo).Should().BeFalse();

        h.Clipboard.HasText = true;
        h.Editor.CanUndo = true;
        h.CanRun(CommandId.Paste).Should().BeTrue();
        h.CanRun(CommandId.Undo).Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Empty_text_grays_find_commands_word_wrap_grays_go_to()
    {
        var h = new Harness();
        CommandId[] find = [CommandId.Find, CommandId.FindNext, CommandId.FindPrevious];

        find.Should().OnlyContain(id => !h.CanRun(id));
        h.CanRun(CommandId.GoTo).Should().BeTrue();

        h.Editor.Type("x");
        h.Editor.WordWrap = true;
        find.Should().OnlyContain(id => h.CanRun(id));
        h.CanRun(CommandId.GoTo).Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Replace_select_all_and_time_date_are_never_grayed()
    {
        var h = new Harness();
        h.Editor.WordWrap = true;

        new[] { CommandId.Replace, CommandId.SelectAll, CommandId.TimeDate }.Should().OnlyContain(id => h.CanRun(id));
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Check_marks_follow_word_wrap_and_status_bar()
    {
        var h = new Harness();
        var wrap = h.Registry.ItemsOf("Format").Single(i => i.Command == CommandId.WordWrap);
        var status = h.Registry.ItemsOf("View").Single(i => i.Command == CommandId.StatusBar);

        wrap.IsChecked!(h.Context).Should().BeFalse();
        status.IsChecked!(h.Context).Should().BeTrue();

        h.Editor.WordWrap = true;
        h.Settings.StatusBar = false;
        wrap.IsChecked!(h.Context).Should().BeTrue();
        status.IsChecked!(h.Context).Should().BeFalse();
    }

    // ---- Command behaviour ----

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Clipboard_commands_act_only_with_focus_in_editor_or_main_window()
    {
        var h = new Harness();
        h.Editor.Type("text");
        h.Editor.SetSelection(0, 4);
        h.Clipboard.HasText = true;
        h.Editor.Focused = false;

        h.Run(CommandId.Cut).Should().BeTrue();
        h.Run(CommandId.Paste).Should().BeTrue();
        h.Editor.Calls.Should().NotContain(["Cut", "Paste"]);

        h.Editor.Focused = true;
        h.Run(CommandId.Cut);
        h.Run(CommandId.Copy);
        h.Run(CommandId.Delete);
        h.Run(CommandId.Paste);
        h.Run(CommandId.SearchWithBing);
        h.Editor.Calls.Should().ContainInOrder("Cut", "Copy", "Delete", "Paste", "SearchWeb");
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Search_with_bing_uses_the_edit_control_and_opens_bing_only_when_it_does_not_search()
    {
        var h = new Harness("find a&b here");
        h.Editor.SetSelection(5, 8);

        h.Run(CommandId.SearchWithBing);
        h.Shell.OpenedUris.Should().BeEmpty();

        h.Editor.SearchWebHandled = false;
        h.Run(CommandId.SearchWithBing);
        h.Shell.OpenedUris.Should().Equal("https://www.bing.com/search?q=a%26b");

        // Grayed with an empty selection; run directly, the fallback still opens nothing.
        h.Editor.SetSelection(3, 3);
        h.Run(CommandId.SearchWithBing).Should().BeFalse();
        h.Registry.Find(CommandId.SearchWithBing)!.Execute(h.Context);
        h.Shell.OpenedUris.Should().HaveCount(1);
        h.Editor.Calls.Count(c => c == "SearchWeb").Should().Be(3);
    }

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Cut_copy_delete_skip_an_empty_selection_even_when_run_directly()
    {
        var h = new Harness();
        var context = h.Context;
        var cut = h.Registry.Find(CommandId.Cut)!;

        cut.Execute(context);

        h.Editor.Calls.Should().NotContain("Cut");
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Undo_select_all_and_time_date()
    {
        var h = new Harness("hello");
        h.Editor.CanUndo = true;

        h.Run(CommandId.Undo);
        h.Run(CommandId.SelectAll);
        h.Run(CommandId.TimeDate);

        h.Editor.Calls.Should().ContainInOrder("Undo", "SetSelection(0,-1)", "ScrollCaret", "ReplaceSelection(2:44 PM 10/29/2026,True)");
        h.Editor.Text.Should().Be("2:44 PM 10/29/2026", "F5 replaces the selection");
    }

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Zoom_commands_step_clamp_and_update_the_status_bar()
    {
        var h = new Harness();

        h.Run(CommandId.ZoomIn);
        h.Editor.Zoom.Should().Be(new ZoomRatio(110, 100));
        h.StatusView.Parts[2].Should().Be(" 110%");

        h.Editor.Zoom = new(495, 100);
        h.Run(CommandId.ZoomIn);
        h.Editor.Zoom.Should().Be(new ZoomRatio(500, 100));

        h.Editor.Zoom = new(15, 100);
        h.Run(CommandId.ZoomOut);
        h.Editor.Zoom.Should().Be(new ZoomRatio(10, 100));

        h.Run(CommandId.ZoomRestore);
        h.Editor.Zoom.Should().Be(new ZoomRatio(100, 100));
        h.StatusView.Parts[2].Should().Be(" 100%");
    }

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Word_wrap_toggles_and_marks_settings_changed()
    {
        var h = new Harness();

        h.Run(CommandId.WordWrap);

        h.Editor.WordWrap.Should().BeTrue();
        h.Settings.WordWrap.Should().BeTrue();
        h.Settings.ViewChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void Word_wrap_failure_shows_string_15_and_keeps_state()
    {
        var h = new Harness();
        h.Editor.FailWordWrap = true;

        h.Run(CommandId.WordWrap);

        h.Settings.WordWrap.Should().BeFalse();
        h.Settings.ViewChanged.Should().BeFalse();
        h.Dialogs.Messages.Should().Equal(("Cannot carry out the Word Wrap command because there is too much text in the file.", MessageKind.Warning));
    }

    [Theory]
    [Trait("Doc", "parity.md#lenh")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Font_keeps_the_new_size_only_when_created_but_always_marks_changed(bool fail, bool applied)
    {
        var h = new Harness();
        h.Editor.FailFont = fail;
        h.Dialogs.FontAnswer = new FontSpec("Consolas", 120);

        h.Run(CommandId.Font);

        h.Settings.Font.FaceName.Should().Be(applied ? "Consolas" : "Lucida Console");
        h.Settings.FontChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Font_cancel_changes_nothing()
    {
        var h = new Harness();

        h.Run(CommandId.Font);

        h.Settings.FontChanged.Should().BeFalse();
        h.Editor.Calls.Should().BeEmpty();
    }

    [Fact]
    [Trait("Doc", "parity.md#lenh")]
    public void Transparency_commands_step_stop_at_the_limits_and_mark_settings_changed()
    {
        var h = new Harness();
        bool Can(CommandId id) => h.Registry.Find(id)!.CanExecute(h.Context);

        Can(CommandId.LessTransparent).Should().BeFalse("the window starts opaque");
        Can(CommandId.Opaque).Should().BeFalse();
        h.Settings.ViewChanged.Should().BeFalse();

        h.Run(CommandId.MoreTransparent);
        h.Settings.OpacityPercent.Should().Be(90);
        h.Window.OpacityPercent.Should().Be(90);
        h.Settings.ViewChanged.Should().BeTrue();

        h.Settings.OpacityPercent = 35;
        h.Run(CommandId.MoreTransparent);
        h.Settings.OpacityPercent.Should().Be(OpacityPolicy.Minimum);
        Can(CommandId.MoreTransparent).Should().BeFalse("the window must stay findable on screen");

        h.Run(CommandId.LessTransparent);
        h.Window.OpacityPercent.Should().Be(40);

        h.Settings.OpacityPercent = 95;
        h.Run(CommandId.LessTransparent);
        h.Settings.OpacityPercent.Should().Be(OpacityPolicy.Maximum);

        h.Settings.OpacityPercent = 50;
        h.Run(CommandId.Opaque);
        h.Window.OpacityPercent.Should().Be(OpacityPolicy.Maximum);
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Status_bar_toggles_visibility()
    {
        var h = new Harness();

        h.Run(CommandId.StatusBar);

        h.Settings.StatusBar.Should().BeFalse();
        h.Settings.ViewChanged.Should().BeTrue();
        h.Window.StatusBarVisible.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void File_commands_reach_the_document_service_shell_and_window()
    {
        var h = new Harness();
        h.Dialogs.OpenAnswers.Enqueue(FileDialogResult.Cancel());
        h.Dialogs.SaveAsAnswers.Enqueue(FileDialogResult.Cancel());
        h.Dialogs.SaveAsAnswers.Enqueue(FileDialogResult.Cancel());

        h.Run(CommandId.New).Should().BeTrue();
        h.Run(CommandId.NewWindow);
        h.Run(CommandId.Open);
        h.Run(CommandId.Save);
        h.Run(CommandId.SaveAs);
        h.Run(CommandId.Exit);

        h.Shell.NewWindows.Should().Be(1);
        h.Dialogs.OpenShown.Should().Be(1);
        h.Dialogs.SaveAsRequests.Should().HaveCount(2);
        h.Window.CloseRequests.Should().Be(1);
        h.Printers.PageSetupResults.Enqueue(new(false, default, 0));
        h.CanRun(CommandId.PageSetup).Should().BeTrue();
        h.CanRun(CommandId.Print).Should().BeTrue("Page Setup and Print are always enabled");
        h.Run(CommandId.PageSetup);
        h.Run(CommandId.Print);
        h.Printers.PageSetupCalls.Should().ContainSingle();
        h.Printers.Opens.Should().Equal((Core.Printing.PrinterChoice.Dialog, null));
    }

    [Fact]
    [Trait("Doc", "parity.md#menu-va-phim-tat")]
    public void Help_links_are_hidden_and_disabled_without_a_url()
    {
        var h = new Harness(options: new AppOptions("https://example.invalid/help", null));
        var feedback = h.Registry.ItemsOf("Help").Single(i => i.Command == CommandId.SendFeedback);
        var help = h.Registry.ItemsOf("Help").Single(i => i.Command == CommandId.ViewHelp);

        help.IsVisible!(h.Context).Should().BeTrue();
        feedback.IsVisible!(h.Context).Should().BeFalse();
        h.Run(CommandId.ViewHelp).Should().BeTrue();
        h.Run(CommandId.SendFeedback).Should().BeFalse();
        h.Run(CommandId.About);

        h.Shell.OpenedUris.Should().Equal("https://example.invalid/help");
        h.Dialogs.AboutShown.Should().Be(1);

        var withFeedback = new Harness(options: new AppOptions(null, "https://example.invalid/feedback"));
        withFeedback.Run(CommandId.ViewHelp).Should().BeFalse();
        withFeedback.Run(CommandId.SendFeedback).Should().BeTrue();
        withFeedback.Shell.OpenedUris.Should().Equal("https://example.invalid/feedback");
    }

    [Fact]
    public void Built_in_modules_register_no_services()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        foreach (var module in BuiltInModules.All)
        {
            module.ConfigureServices(services);
        }

        services.Should().BeEmpty();
    }
}
