namespace NotepadNet.App.Shell;

/// <summary>
/// Draws each menu bar item's text where USER32 draws it in a menu bar item of the same size:
/// <c>(width - text width) / 2</c> from the item's left edge, vertically centered in the whole
/// item. A ToolStripMenuItem insets its content and centers with its own rounding, which is off
/// by a pixel at some DPIs. Access keys are underlined only while the menu bar is used from the
/// keyboard (Alt held, or the menu entered with Alt or F10) or when Windows always underlines
/// them; a MenuStrip would follow the window's keyboard-cue state, which stays on after Alt+Tab
/// (the original's menu bar shows no underline then, seen 2026-10-29). The drop-down menus keep
/// the default rendering.
/// </summary>
internal sealed class MenuBarRenderer : ToolStripProfessionalRenderer
{
    /// <summary>Set while the menu bar was entered from the keyboard.</summary>
    public bool KeyboardMode { get; set; }

    private bool ShowAccessKeys =>
        KeyboardMode || SystemInformation.MenuAccessKeysUnderlined || (Control.ModifierKeys & Keys.Alt) != 0;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item is ToolStripMenuItem { IsOnDropDown: false } item && e.Text is { } text)
        {
            var prefix = ShowAccessKeys ? e.TextFormat & ~TextFormatFlags.HidePrefix : e.TextFormat | TextFormatFlags.HidePrefix;
            var format = (prefix & ~(TextFormatFlags.HorizontalCenter | TextFormatFlags.Right)) | TextFormatFlags.NoPadding;
            var width = TextRenderer.MeasureText(e.Graphics, text, e.TextFont, Size.Empty, format).Width;
            var x = Math.Max(0, (item.Width - width) / 2);
            e.TextRectangle = new Rectangle(x, 0, item.Width - x, item.Height);
            e.TextFormat = (format & ~(TextFormatFlags.Top | TextFormatFlags.Bottom)) | TextFormatFlags.VerticalCenter;
        }

        base.OnRenderItemText(e);
    }
}
