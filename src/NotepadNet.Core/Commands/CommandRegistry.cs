using NotepadNet.Core.Documents;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Printing;
using NotepadNet.Core.Search;
using NotepadNet.Core.Services;
using NotepadNet.Core.Settings;
using NotepadNet.Core.StatusBar;

namespace NotepadNet.Core.Commands;

/// <summary>Everything a command can act on.</summary>
public sealed record CommandContext(
    IEditorSurface Editor,
    DocumentService Documents,
    IMainWindow Window,
    IDialogService Dialogs,
    IClipboard Clipboard,
    IShell Shell,
    ILocalizedStrings Strings,
    NotepadSettings Settings,
    StatusBarPresenter StatusBar,
    TimeDateInserter TimeDate,
    AppOptions Options,
    SearchService Search,
    GoToService GoTo,
    PrintService Printing,
    RecentFilesService RecentFiles);

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<CommandId, ICommand> _commands = [];
    private readonly List<MenuNode> _menus = [];
    private readonly List<MenuContribution> _items = [];
    private readonly List<Action<CommandContext>> _menuRefreshes = [];
    private readonly Dictionary<(KeyChord, KeyTier), KeyBinding> _keys = [];

    public IReadOnlyCollection<ICommand> Commands => _commands.Values;

    public IEnumerable<KeyBinding> KeyBindings => _keys.Values;

    public void Add(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"Command {command.Id} is registered twice.");
        }
    }

    public void AddMenu(MenuNode menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (_menus.Exists(m => m.Path == menu.Path))
        {
            throw new InvalidOperationException($"Menu '{menu.Path}' is registered twice.");
        }

        _menus.Add(menu);
    }

    public void AddMenuItem(MenuContribution item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public void AddMenuRefresh(Action<CommandContext> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        _menuRefreshes.Add(refresh);
    }

    /// <summary>Runs what <see cref="AddMenuRefresh"/> collected; call it before evaluating menu rules.</summary>
    public void RefreshMenuState(CommandContext context)
    {
        foreach (var refresh in _menuRefreshes)
        {
            refresh(context);
        }
    }

    public void AddKeyBinding(KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_keys.TryAdd((binding.Chord, binding.Tier), binding))
        {
            throw new InvalidOperationException($"Key {binding.Chord} is bound twice in {binding.Tier}.");
        }
    }

    public ICommand? Find(CommandId id) => _commands.GetValueOrDefault(id);

    /// <summary>Menus whose parent is <paramref name="parentPath"/> ("" for the menu bar), in order.</summary>
    public IReadOnlyList<MenuNode> MenusUnder(string parentPath) =>
        [.. _menus.Where(m => ParentOf(m.Path) == parentPath).OrderBy(m => m.Order)];

    /// <summary>Items of one menu, sorted by group then order.</summary>
    public IReadOnlyList<MenuContribution> ItemsOf(string path) =>
        [.. _items.Where(i => i.Path == path).OrderBy(i => i.Group).ThenBy(i => i.Order)];

    public KeyBinding? FindKey(KeyChord chord, KeyTier tier) => _keys.GetValueOrDefault((chord, tier));

    /// <summary>Runs a command when its rule allows it.</summary>
    /// <returns>Whether the command ran.</returns>
    public bool TryExecute(CommandId id, CommandContext context)
    {
        var command = Find(id);
        if (command is null || !command.CanExecute(context))
        {
            return false;
        }

        command.Execute(context);
        return true;
    }

    /// <summary>Checks that every menu item and key points to a registered command and every item
    /// sits in a registered menu.</summary>
    public void Validate()
    {
        foreach (var item in _items)
        {
            if (!_commands.ContainsKey(item.Command) || !_menus.Exists(m => m.Path == item.Path))
            {
                throw new InvalidOperationException($"Menu item {item.Command} in '{item.Path}' has no command or menu.");
            }
        }

        foreach (var key in _keys.Values)
        {
            if (!_commands.ContainsKey(key.Command))
            {
                throw new InvalidOperationException($"Key {key.Chord} points to unknown command {key.Command}.");
            }
        }
    }

    private static string ParentOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}
