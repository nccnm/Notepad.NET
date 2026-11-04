using NotepadNet.Core.Commands;
using NotepadNet.Core.Localization;

namespace NotepadNet.App.Shell;

/// <summary>
/// Builds the MenuStrip from the command registry and applies the enable, check and visibility
/// rules. There is no real <c>WM_INITMENU</c> with a MenuStrip, so the rules run when the menu bar
/// activates (<c>MenuActivate</c>) and again when a drop-down opens.
/// </summary>
internal sealed class MenuBuilder(CommandRegistry registry, ILocalizedStrings strings, Func<CommandContext> context)
{
    private readonly CommandRegistry _registry = registry;
    private readonly ILocalizedStrings _strings = strings;
    private readonly Func<CommandContext> _context = context;
    private readonly List<(ToolStripMenuItem Item, MenuContribution Contribution)> _items = [];
    private readonly List<(ToolStripMenuItem Item, Func<CommandContext, bool> IsEnabled)> _submenus = [];

    public MenuStrip Build()
    {
        var strip = new MenuStrip { Dock = DockStyle.None, AutoSize = true, Stretch = true, AccessibleName = "Application" };
        foreach (var menu in _registry.MenusUnder(""))
        {
            var top = new ToolStripMenuItem(_strings.GetString(menu.TextKey));
            Fill(top, menu.Path);
            strip.Items.Add(top);
        }

        strip.MenuActivate += (_, _) =>
        {
            // DestroyWindow can still activate a menu bar left active, after the main window has
            // disposed the Edit control that the enable rules ask.
            if (strip.FindForm() is not { Disposing: true })
            {
                UpdateState();
            }
        };
        return strip;
    }

    /// <summary>The original's <c>UpdateMenuState</c>, run over every item.</summary>
    public void UpdateState()
    {
        var context = _context();
        _registry.RefreshMenuState(context);
        foreach (var (item, isEnabled) in _submenus)
        {
            item.Enabled = isEnabled(context);
        }

        foreach (var (item, contribution) in _items)
        {
            var visible = contribution.IsVisible?.Invoke(context) ?? true;
            item.Visible = visible;
            if (visible && contribution.DynamicText is { } text)
            {
                item.Text = text(context);
            }

            item.Enabled = _registry.Find(contribution.Command)?.CanExecute(context) ?? false;
            item.Checked = contribution.IsChecked?.Invoke(context) ?? false;
        }
    }

    private void Fill(ToolStripMenuItem parent, string path)
    {
        // Submenus sit in group 0 at their own order; items follow their group and order.
        var entries = _registry.MenusUnder(path).Select(m => (Group: 0, m.Order, Menu: (MenuNode?)m, Item: (MenuContribution?)null))
            .Concat(_registry.ItemsOf(path).Select(i => (i.Group, i.Order, Menu: (MenuNode?)null, Item: (MenuContribution?)i)))
            .OrderBy(e => e.Group).ThenBy(e => e.Order)
            .ToList();
        int? group = null;
        foreach (var entry in entries)
        {
            if (group is not null && group != entry.Group)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
            }

            group = entry.Group;
            if (entry.Menu is { } menu)
            {
                var sub = new ToolStripMenuItem(_strings.GetString(menu.TextKey));
                Fill(sub, menu.Path);
                parent.DropDownItems.Add(sub);
                if (menu.IsEnabled is { } isEnabled)
                {
                    _submenus.Add((sub, isEnabled));
                }

                continue;
            }

            var contribution = entry.Item!;
            var item = new ToolStripMenuItem(_strings.GetString(contribution.TextKey))
            {
                Tag = contribution.Command,
                ShortcutKeyDisplayString = contribution.ShortcutTextKey is null ? null : _strings.GetString(contribution.ShortcutTextKey),
            };
            item.Click += (_, _) => _registry.TryExecute(contribution.Command, _context());
            _items.Add((item, contribution));
            parent.DropDownItems.Add(item);
        }

        parent.DropDownOpening += (_, _) => UpdateState();
    }
}
