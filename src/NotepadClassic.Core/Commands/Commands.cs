using Microsoft.Extensions.DependencyInjection;

namespace NotepadClassic.Core.Commands;

/// <summary>Command IDs of the original's menu and accelerators.</summary>
public enum CommandId
{
    New = 0x1,
    Open = 0x2,
    Save = 0x3,
    SaveAs = 0x4,
    PageSetup = 0x5,
    Print = 0x6,
    Exit = 0x7,
    NewWindow = 0x8,
    Undo = 0x10,
    Find = 0x15,
    FindNext = 0x16,
    Replace = 0x17,
    GoTo = 0x18,
    SelectAll = 0x19,
    TimeDate = 0x1A,
    StatusBar = 0x1B,
    SearchWithBing = 0x1C,
    FindPrevious = 0x1D,
    WordWrap = 0x20,
    Font = 0x21,
    ZoomIn = 0x22,
    ZoomOut = 0x23,
    ZoomRestore = 0x24,
    ViewHelp = 0x40,
    About = 0x41,
    SendFeedback = 0x42,

    /// <summary>Port addition (View > Transparency), like the two below; not in the original.</summary>
    MoreTransparent = 0x50,
    LessTransparent = 0x51,
    Opaque = 0x52,

    /// <summary>Port addition (View > Always on Top); not in the original.</summary>
    AlwaysOnTop = 0x53,
    Cut = 0x300,
    Copy = 0x301,
    Paste = 0x302,
    Delete = 0x303,
}

public interface ICommand
{
    CommandId Id { get; }

    /// <summary>The enable rule of the original's <c>UpdateMenuState</c>. A disabled command's
    /// menu item is grayed and its shortcut does nothing.</summary>
    bool CanExecute(CommandContext context);

    void Execute(CommandContext context);
}

/// <summary>A command built from two delegates; most commands are one line each.</summary>
public sealed class DelegateCommand(CommandId id, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null) : ICommand
{
    private readonly Action<CommandContext> _execute = execute;
    private readonly Func<CommandContext, bool>? _canExecute = canExecute;

    public CommandId Id { get; } = id;

    public bool CanExecute(CommandContext context) => _canExecute?.Invoke(context) ?? true;

    public void Execute(CommandContext context) => _execute(context);
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>A virtual key with modifiers, independent of any UI framework.</summary>
public readonly record struct KeyChord(ushort VirtualKey, KeyModifiers Modifiers)
{
    public override string ToString() => $"{Modifiers}+0x{VirtualKey:X2}";
}

/// <summary>Accelerator table a binding belongs to.</summary>
public enum KeyTier
{
    /// <summary><c>GlobalAcc</c>: translated before modeless dialogs see the key.</summary>
    Global,

    /// <summary><c>MainAcc</c>: translated after modeless dialogs, so it does not act while
    /// focus is in Find/Replace.</summary>
    Main,

    /// <summary>Extra <c>SlipUpAcc</c> keys of <c>/.SETUP</c> mode (phase d).</summary>
    SetupOnly,
}

public sealed record KeyBinding(KeyChord Chord, CommandId Command, KeyTier Tier);

/// <summary>A top-level menu or submenu. <paramref name="Path"/> uses <c>/</c> between levels
/// ("View/Zoom").</summary>
public sealed record MenuNode(string Path, string TextKey, int Order);

/// <summary>A menu item. Items of one menu are sorted by <paramref name="Group"/> then
/// <paramref name="Order"/>; a separator goes between groups.</summary>
/// <param name="ShortcutTextKey">Shortcut text shown after a tab, as in the original resources.</param>
/// <param name="IsChecked">Check mark rule (Word Wrap, Status Bar).</param>
/// <param name="IsVisible">Visibility rule (Help links without a configured URL).</param>
public sealed record MenuContribution(
    string Path,
    int Group,
    int Order,
    CommandId Command,
    string TextKey,
    string? ShortcutTextKey = null,
    Func<CommandContext, bool>? IsChecked = null,
    Func<CommandContext, bool>? IsVisible = null);

public interface ICommandRegistry
{
    void Add(ICommand command);

    void AddMenu(MenuNode menu);

    void AddMenuItem(MenuContribution item);

    void AddKeyBinding(KeyBinding binding);
}

/// <summary>A feature: registers its services, then contributes commands, menu items and keys.
/// Adding a feature means adding a module, not editing a central switch.</summary>
public interface IFeatureModule
{
    void ConfigureServices(IServiceCollection services);

    void Contribute(ICommandRegistry registry);
}
