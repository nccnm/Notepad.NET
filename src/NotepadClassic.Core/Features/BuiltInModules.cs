using Microsoft.Extensions.DependencyInjection;
using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Documents;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Printing;
using NotepadClassic.Core.Services;

namespace NotepadClassic.Core.Features;

/// <summary>Win32 virtual-key codes used by the accelerator tables.</summary>
public static class VirtualKeys
{
    public const ushort Back = 0x08;
    public const ushort Escape = 0x1B;
    public const ushort Insert = 0x2D;
    public const ushort D0 = 0x30;
    public const ushort Numpad0 = 0x60;
    public const ushort Add = 0x6B;
    public const ushort Subtract = 0x6D;
    public const ushort F1 = 0x70;
    public const ushort F3 = 0x72;
    public const ushort F5 = 0x74;
    public const ushort OemPlus = 0xBB;
    public const ushort OemMinus = 0xBD;

    public static ushort Letter(char c) => c;
}

/// <summary>Helpers shared by the built-in modules.</summary>
internal static class Contribution
{
    public const KeyModifiers Ctrl = KeyModifiers.Control;
    public const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    public static void Item(
        this ICommandRegistry registry, string path, int group, int order, CommandId id, string textKey, bool hasShortcutText = false,
        Func<CommandContext, bool>? isChecked = null, Func<CommandContext, bool>? isVisible = null) =>
        registry.AddMenuItem(new(path, group, order, id, textKey, hasShortcutText ? textKey + "_Keys" : null, isChecked, isVisible));

    public static void Key(this ICommandRegistry registry, KeyTier tier, ushort key, KeyModifiers modifiers, CommandId id) =>
        registry.AddKeyBinding(new(new(key, modifiers), id, tier));

    public static void Command(this ICommandRegistry registry, CommandId id, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null) =>
        registry.Add(new DelegateCommand(id, execute, canExecute));

    /// <summary>Clipboard commands only act while focus is on the editor or the main window.</summary>
    public static void WhenFocused(CommandContext c, Action<IEditorSurface> action)
    {
        if (c.Editor.HasFocusInEditorOrMain)
        {
            action(c.Editor);
        }
    }

    /// <summary>Cut, Copy and Delete do nothing on an empty selection.</summary>
    public static void OnSelection(CommandContext c, Action<IEditorSurface> action)
    {
        if (!c.Editor.Selection.IsEmpty)
        {
            WhenFocused(c, action);
        }
    }

    public static bool HasSelection(CommandContext c) => !c.Editor.Selection.IsEmpty;
}

/// <summary>File menu.</summary>
public sealed class FileModule : IFeatureModule
{
    public const string Menu = "File";
    public const string RecentMenu = "File/Recent";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddMenu(new(Menu, "Menu_File", 0));
        registry.Command(CommandId.New, c => c.Documents.New());
        registry.Command(CommandId.NewWindow, c => c.Shell.StartNewWindow());
        registry.Command(CommandId.Open, c => c.Documents.Open());
        registry.Command(CommandId.Save, c => c.Documents.Save());
        registry.Command(CommandId.SaveAs, c => c.Documents.SaveAs());
        registry.Command(CommandId.DiscardChanges, c => c.Documents.DiscardChanges(), c => c.Documents.IsDirty);
        registry.Command(CommandId.CopyFullPath, CopyFullPath, HasFile);
        registry.Command(CommandId.OpenContainingFolder, OpenContainingFolder, HasFile);
        registry.Command(CommandId.PageSetup, c => c.Printing.PageSetup());
        registry.Command(CommandId.Print, c => c.Printing.Print(PrinterChoice.Dialog));
        registry.Command(CommandId.Exit, c => c.Window.RequestClose());

        registry.Item(Menu, 0, 0, CommandId.New, "Menu_File_New", true);
        registry.Item(Menu, 0, 1, CommandId.NewWindow, "Menu_File_NewWindow", true);
        registry.Item(Menu, 0, 2, CommandId.Open, "Menu_File_Open", true);
        AddRecentFiles(registry);
        registry.Item(Menu, 0, 4, CommandId.Save, "Menu_File_Save", true);
        registry.Item(Menu, 0, 5, CommandId.SaveAs, "Menu_File_SaveAs", true);
        registry.Item(Menu, 0, 6, CommandId.DiscardChanges, "Menu_File_DiscardChanges");
        registry.Item(Menu, 1, 0, CommandId.CopyFullPath, "Menu_File_CopyFullPath");
        registry.Item(Menu, 1, 1, CommandId.OpenContainingFolder, "Menu_File_OpenContainingFolder");
        registry.Item(Menu, 2, 0, CommandId.PageSetup, "Menu_File_PageSetup");
        registry.Item(Menu, 2, 1, CommandId.Print, "Menu_File_Print", true);
        registry.Item(Menu, 3, 0, CommandId.Exit, "Menu_File_Exit");

        registry.Key(KeyTier.Global, VirtualKeys.Letter('N'), Contribution.Ctrl, CommandId.New);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('N'), Contribution.CtrlShift, CommandId.NewWindow);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('O'), Contribution.Ctrl, CommandId.Open);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('S'), Contribution.Ctrl, CommandId.Save);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('S'), Contribution.CtrlShift, CommandId.SaveAs);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('P'), Contribution.Ctrl, CommandId.Print);
        // Ctrl+W is not shown in the menu.
        registry.Key(KeyTier.Global, VirtualKeys.Letter('W'), Contribution.Ctrl, CommandId.Exit);

        // SlipUpAcc (/.SETUP) is MainAcc plus these four Exit keys. Ctrl+C and Ctrl+Z come after
        // Copy and Undo in that table, so only Esc and Ctrl+D exit (KeyDispatcher looks up Main first).
        registry.Key(KeyTier.SetupOnly, VirtualKeys.Escape, KeyModifiers.None, CommandId.Exit);
        registry.Key(KeyTier.SetupOnly, VirtualKeys.Letter('C'), Contribution.Ctrl, CommandId.Exit);
        registry.Key(KeyTier.SetupOnly, VirtualKeys.Letter('D'), Contribution.Ctrl, CommandId.Exit);
        registry.Key(KeyTier.SetupOnly, VirtualKeys.Letter('Z'), Contribution.Ctrl, CommandId.Exit);
    }

    private static bool HasFile(CommandContext c) => c.Documents.FilePath is not null;

    private static void CopyFullPath(CommandContext c)
    {
        if (!c.Clipboard.SetText(c.Documents.FilePath!))
        {
            c.Dialogs.ShowMessage(c.Strings.GetString(StringKeys.ClipboardBusy), MessageKind.Warning);
        }
    }

    private static void OpenContainingFolder(CommandContext c)
    {
        var path = c.Documents.FilePath!;
        if (!c.Shell.ShowInFolder(path))
        {
            c.Dialogs.ShowMessage(MessageText.InsertArgument(c.Strings.GetString(StringKeys.FolderCannotOpen), path), MessageKind.Warning);
        }
    }

    /// <summary>The submenu under Open: one command per place in the list, hidden while that place
    /// is empty, and Clear. The list is read again each time a menu opens, for what other windows added.</summary>
    private static void AddRecentFiles(ICommandRegistry registry)
    {
        registry.AddMenu(new(RecentMenu, "Menu_File_Recent", 3, c => c.RecentFiles.Items.Count > 0));
        registry.AddMenuRefresh(c => c.RecentFiles.Refresh());
        for (var i = 0; i < RecentFilesPolicy.MaxCount; i++)
        {
            var index = i;
            bool Exists(CommandContext c) => index < c.RecentFiles.Items.Count;
            var id = CommandId.RecentFile1 + index;
            registry.Command(id, c => c.Documents.OpenRecent(c.RecentFiles.Items[index]), Exists);
            registry.AddMenuItem(new(
                RecentMenu, 0, index, id, "Menu_File_RecentItem", IsVisible: Exists,
                DynamicText: c => MessageText.FormatInserts(
                    c.Strings.GetString("Menu_File_RecentItem"),
                    RecentFilesPolicy.AccessNumber(index),
                    RecentFilesPolicy.DisplayPath(c.RecentFiles.Items[index]))));
        }

        registry.Command(CommandId.ClearRecentFiles, c => c.RecentFiles.Clear());
        registry.Item(RecentMenu, 1, 0, CommandId.ClearRecentFiles, "Menu_File_ClearRecent");
    }
}

/// <summary>Edit menu without the search commands.</summary>
public sealed class EditModule : IFeatureModule
{
    public const string Menu = "Edit";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public const string BingSearchUri = "https://www.bing.com/search?q=";

    /// <summary>
    /// <c>EM_SEARCHWEB</c> first; when the Edit control does not search (older comctl32) and there is a
    /// selection, opens Bing with the selected text instead (deliberate addition, see parity.md).
    /// </summary>
    private static void SearchWithBing(CommandContext c)
    {
        if (c.Editor.SearchWeb())
        {
            return;
        }

        var selection = c.Editor.Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        string query;
        using (var lease = c.Editor.AcquireText())
        {
            var text = lease.Text;
            var start = Math.Clamp(selection.Start, 0, text.Length);
            var end = Math.Clamp(selection.End, start, text.Length);
            query = new string(text[start..end]);
        }

        c.Shell.OpenUri(BingSearchUri + Uri.EscapeDataString(query));
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddMenu(new(Menu, "Menu_Edit", 1));
        registry.Command(CommandId.Undo, c => c.Editor.Undo(), c => c.Editor.CanUndo);
        registry.Command(CommandId.Cut, c => Contribution.OnSelection(c, e => e.Cut()), Contribution.HasSelection);
        registry.Command(CommandId.Copy, c => Contribution.OnSelection(c, e => e.Copy()), Contribution.HasSelection);
        registry.Command(CommandId.Paste, c => Contribution.WhenFocused(c, e => e.Paste()), c => c.Clipboard.ContainsText());
        registry.Command(CommandId.Delete, c => Contribution.OnSelection(c, e => e.DeleteSelection()), Contribution.HasSelection);
        // The handler does not check the selection itself; only the grayed item does.
        registry.Command(CommandId.SearchWithBing, c => Contribution.WhenFocused(c, _ => SearchWithBing(c)), Contribution.HasSelection);
        registry.Command(CommandId.SelectAll, c =>
        {
            c.Editor.SetSelection(0, -1);
            c.Editor.ScrollCaret();
        });
        registry.Command(CommandId.TimeDate, c => c.TimeDate.Insert(c.Editor, forLog: false));

        registry.Item(Menu, 0, 0, CommandId.Undo, "Menu_Edit_Undo", true);
        registry.Item(Menu, 1, 0, CommandId.Cut, "Menu_Edit_Cut", true);
        registry.Item(Menu, 1, 1, CommandId.Copy, "Menu_Edit_Copy", true);
        registry.Item(Menu, 1, 2, CommandId.Paste, "Menu_Edit_Paste", true);
        registry.Item(Menu, 1, 3, CommandId.Delete, "Menu_Edit_Delete", true);
        registry.Item(Menu, 2, 0, CommandId.SearchWithBing, "Menu_Edit_SearchBing", true);
        registry.Item(Menu, 3, 0, CommandId.SelectAll, "Menu_Edit_SelectAll", true);
        registry.Item(Menu, 3, 1, CommandId.TimeDate, "Menu_Edit_TimeDate", true);

        registry.Key(KeyTier.Global, VirtualKeys.Letter('E'), Contribution.Ctrl, CommandId.SearchWithBing);
        registry.Key(KeyTier.Main, VirtualKeys.Insert, Contribution.Ctrl, CommandId.Copy);
        registry.Key(KeyTier.Main, VirtualKeys.F5, KeyModifiers.None, CommandId.TimeDate);
        registry.Key(KeyTier.Main, VirtualKeys.Back, KeyModifiers.Alt, CommandId.Undo);
        registry.Key(KeyTier.Main, VirtualKeys.Letter('Z'), Contribution.Ctrl, CommandId.Undo);
        registry.Key(KeyTier.Main, VirtualKeys.Letter('X'), Contribution.Ctrl, CommandId.Cut);
        registry.Key(KeyTier.Main, VirtualKeys.Letter('C'), Contribution.Ctrl, CommandId.Copy);
        registry.Key(KeyTier.Main, VirtualKeys.Letter('V'), Contribution.Ctrl, CommandId.Paste);
        registry.Key(KeyTier.Main, VirtualKeys.Letter('A'), Contribution.Ctrl, CommandId.SelectAll);
    }
}

/// <summary>
/// Find, Find Next, Find Previous, Replace and Go To. Find and its keys are grayed on an empty text, Go To while
/// Word Wrap is on; Replace never.
/// </summary>
public sealed class SearchModule : IFeatureModule
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        static bool HasText(CommandContext c) => c.Editor.TextLength != 0;
        registry.Command(CommandId.Find, c => c.Search.OpenFind(), HasText);
        registry.Command(CommandId.FindNext, c => c.Search.FindNextFromKey(backward: false), HasText);
        registry.Command(CommandId.FindPrevious, c => c.Search.FindNextFromKey(backward: true), HasText);
        registry.Command(CommandId.Replace, c => c.Search.OpenReplace());
        registry.Command(CommandId.GoTo, c => c.GoTo.Run(c.Dialogs, c.Strings), c => !c.Editor.WordWrap);

        registry.Item(EditModule.Menu, 2, 1, CommandId.Find, "Menu_Edit_Find", true);
        registry.Item(EditModule.Menu, 2, 2, CommandId.FindNext, "Menu_Edit_FindNext", true);
        registry.Item(EditModule.Menu, 2, 3, CommandId.FindPrevious, "Menu_Edit_FindPrevious", true);
        registry.Item(EditModule.Menu, 2, 4, CommandId.Replace, "Menu_Edit_Replace", true);
        registry.Item(EditModule.Menu, 2, 5, CommandId.GoTo, "Menu_Edit_GoTo", true);

        registry.Key(KeyTier.Global, VirtualKeys.F3, KeyModifiers.None, CommandId.FindNext);
        registry.Key(KeyTier.Global, VirtualKeys.F3, KeyModifiers.Shift, CommandId.FindPrevious);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('G'), Contribution.Ctrl, CommandId.GoTo);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('F'), Contribution.Ctrl, CommandId.Find);
        registry.Key(KeyTier.Global, VirtualKeys.Letter('H'), Contribution.Ctrl, CommandId.Replace);
    }
}

/// <summary>Format menu: Word Wrap and Font.</summary>
public sealed class FormatModule : IFeatureModule
{
    public const string Menu = "Format";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddMenu(new(Menu, "Menu_Format", 2));
        registry.Command(CommandId.WordWrap, ToggleWordWrap);
        registry.Command(CommandId.Font, ChooseFont);
        registry.Item(Menu, 0, 0, CommandId.WordWrap, "Menu_Format_WordWrap", isChecked: c => c.Editor.WordWrap);
        registry.Item(Menu, 0, 1, CommandId.Font, "Menu_Format_Font");
    }

    /// <summary>Recreates the editor; on failure shows string 15 and keeps the old state.</summary>
    private static void ToggleWordWrap(CommandContext c)
    {
        var wrap = !c.Editor.WordWrap;
        if (!c.Editor.TrySetWordWrap(wrap))
        {
            c.Dialogs.ShowMessage(c.Strings.GetString(StringKeys.WordWrapTooMuchText), MessageKind.Warning);
            return;
        }

        c.Settings.WordWrap = wrap;
        c.Settings.ViewChanged = true;
        c.StatusBar.UpdateAll();
    }

    /// <summary>The new size is kept only when the font could be created; the "font changed"
    /// flag is set after any OK.</summary>
    private static void ChooseFont(CommandContext c)
    {
        var chosen = c.Dialogs.ChooseFont(c.Settings.Font);
        if (chosen is null)
        {
            return;
        }

        if (c.Editor.SetFont(chosen))
        {
            c.Settings.Font = chosen;
        }

        c.Settings.FontChanged = true;
    }
}

/// <summary>View menu: zoom, transparency, always on top and status bar.</summary>
public sealed class ViewModule : IFeatureModule
{
    public const string Menu = "View";
    public const string ZoomMenu = "View/Zoom";
    public const string TransparencyMenu = "View/Transparency";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddMenu(new(Menu, "Menu_View", 3));
        registry.AddMenu(new(ZoomMenu, "Menu_View_Zoom", 0));
        registry.AddMenu(new(TransparencyMenu, "Menu_View_Transparency", 1));
        registry.Command(CommandId.ZoomIn, c => SetZoom(c, ZoomPolicy.StepBy(c.Editor.Zoom, ZoomPolicy.Step)));
        registry.Command(CommandId.ZoomOut, c => SetZoom(c, ZoomPolicy.StepBy(c.Editor.Zoom, -ZoomPolicy.Step)));
        registry.Command(CommandId.ZoomRestore, c => SetZoom(c, ZoomPolicy.Restore()));
        registry.Command(CommandId.StatusBar, ToggleStatusBar);
        registry.Command(
            CommandId.MoreTransparent,
            c => SetOpacity(c, OpacityPolicy.StepBy(c.Settings.OpacityPercent, -OpacityPolicy.Step)),
            c => c.Settings.OpacityPercent > OpacityPolicy.Minimum);
        registry.Command(
            CommandId.LessTransparent,
            c => SetOpacity(c, OpacityPolicy.StepBy(c.Settings.OpacityPercent, OpacityPolicy.Step)),
            IsTransparent);
        registry.Command(CommandId.Opaque, c => SetOpacity(c, OpacityPolicy.Maximum), IsTransparent);
        registry.Command(CommandId.AlwaysOnTop, ToggleAlwaysOnTop);

        registry.Item(ZoomMenu, 0, 0, CommandId.ZoomIn, "Menu_View_ZoomIn", true);
        registry.Item(ZoomMenu, 0, 1, CommandId.ZoomOut, "Menu_View_ZoomOut", true);
        registry.Item(ZoomMenu, 0, 2, CommandId.ZoomRestore, "Menu_View_ZoomRestore", true);
        registry.Item(TransparencyMenu, 0, 0, CommandId.MoreTransparent, "Menu_View_MoreTransparent", true);
        registry.Item(TransparencyMenu, 0, 1, CommandId.LessTransparent, "Menu_View_LessTransparent", true);
        registry.Item(TransparencyMenu, 0, 2, CommandId.Opaque, "Menu_View_Opaque");
        registry.Item(Menu, 0, 2, CommandId.AlwaysOnTop, "Menu_View_AlwaysOnTop", isChecked: c => c.Settings.AlwaysOnTop);
        registry.Item(Menu, 0, 3, CommandId.StatusBar, "Menu_View_StatusBar", isChecked: c => c.Settings.StatusBar);

        registry.Key(KeyTier.Global, VirtualKeys.OemPlus, Contribution.Ctrl, CommandId.ZoomIn);
        registry.Key(KeyTier.Global, VirtualKeys.Add, Contribution.Ctrl, CommandId.ZoomIn);
        registry.Key(KeyTier.Global, VirtualKeys.OemMinus, Contribution.Ctrl, CommandId.ZoomOut);
        registry.Key(KeyTier.Global, VirtualKeys.Subtract, Contribution.Ctrl, CommandId.ZoomOut);
        registry.Key(KeyTier.Global, VirtualKeys.Numpad0, Contribution.Ctrl, CommandId.ZoomRestore);
        registry.Key(KeyTier.Global, VirtualKeys.D0, Contribution.Ctrl, CommandId.ZoomRestore);
    }

    private static void SetZoom(CommandContext c, ZoomRatio zoom)
    {
        c.Editor.Zoom = zoom;
        c.StatusBar.UpdateZoom();
    }

    private static bool IsTransparent(CommandContext c) => c.Settings.OpacityPercent < OpacityPolicy.Maximum;

    private static void SetOpacity(CommandContext c, int percent)
    {
        c.Settings.OpacityPercent = percent;
        c.Settings.ViewChanged = true;
        c.Window.SetOpacity(percent);
    }

    private static void ToggleAlwaysOnTop(CommandContext c)
    {
        c.Settings.AlwaysOnTop = !c.Settings.AlwaysOnTop;
        c.Settings.ViewChanged = true;
        c.Window.SetAlwaysOnTop(c.Settings.AlwaysOnTop);
    }

    private static void ToggleStatusBar(CommandContext c)
    {
        c.Settings.StatusBar = !c.Settings.StatusBar;
        c.Settings.ViewChanged = true;
        c.Window.SetStatusBarVisible(c.Settings.StatusBar);
    }
}

/// <summary>Help menu. View Help and Send Feedback open configured links and are hidden without one.</summary>
public sealed class HelpModule : IFeatureModule
{
    public const string Menu = "Help";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddMenu(new(Menu, "Menu_Help", 4));
        registry.Command(CommandId.ViewHelp, c => c.Shell.OpenUri(c.Options.HelpUrl!), c => c.Options.HasHelp);
        registry.Command(CommandId.SendFeedback, c => c.Shell.OpenUri(c.Options.FeedbackUrl!), c => c.Options.HasFeedback);
        registry.Command(CommandId.About, c => c.Dialogs.ShowAbout());

        registry.Item(Menu, 0, 0, CommandId.ViewHelp, "Menu_Help_ViewHelp", isVisible: c => c.Options.HasHelp);
        registry.Item(Menu, 0, 1, CommandId.SendFeedback, "Menu_Help_Feedback", isVisible: c => c.Options.HasFeedback);
        registry.Item(Menu, 1, 0, CommandId.About, "Menu_Help_About");

        registry.Key(KeyTier.Global, VirtualKeys.F1, KeyModifiers.None, CommandId.ViewHelp);
    }
}

/// <summary>The modules of the original's menus, in menu order.</summary>
public static class BuiltInModules
{
    public static IReadOnlyList<IFeatureModule> All { get; } =
        [new FileModule(), new EditModule(), new SearchModule(), new FormatModule(), new ViewModule(), new HelpModule()];

    public static CommandRegistry CreateRegistry(IEnumerable<IFeatureModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var registry = new CommandRegistry();
        foreach (var module in modules)
        {
            module.Contribute(registry);
        }

        registry.Validate();
        return registry;
    }
}
