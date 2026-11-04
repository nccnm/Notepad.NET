using NotepadNet.App.Interop;

namespace NotepadNet.App.Shell;

/// <summary>
/// Draws the parts of the status bar where <c>msctls_statusbar32</c> draws them: each text at
/// <see cref="TextOrigin"/>, measured on a hidden real status bar, and the themed sizing gripper
/// (<c>Status</c>, <c>SP_GRIPPER</c>) at the bottom-right corner, which comctl32 shows because
/// the main window is sizable. Compared with the original at 96-192 DPI (2026-10-29);
/// everything else is the default rendering.
/// </summary>
internal sealed class StatusBarRenderer(Func<int> dpi) : ToolStripSystemRenderer
{
    private readonly Func<int> _dpi = dpi;

    /// <summary>
    /// Where comctl32 starts a part's text: x from the part rectangle's left edge, y from the top
    /// of the status window (<see cref="NativeBarMetrics.StatusTextOrigin"/>). Null draws the
    /// default way.
    /// </summary>
    public Point? TextOrigin { get; set; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item is ToolStripStatusLabel label && TextOrigin is { } origin)
        {
            // Every label but the first starts at the separator (see LayoutChildren), three pixels
            // left of the part rectangle comctl32 reports and draws text from.
            var x = origin.X + (label.Owner?.Items.IndexOf(label) > 0 ? 3 : 0);
            var y = origin.Y - label.Bounds.Y;
            e.TextRectangle = new Rectangle(x, y, Math.Max(0, label.Width - x), Math.Max(0, label.Height - y));
            e.TextFormat = (e.TextFormat & ~(TextFormatFlags.HorizontalCenter | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom))
                | TextFormatFlags.NoPadding;
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var strip = e.ToolStrip;
        var theme = NativeMethods.OpenThemeDataForDpi(strip.Handle, "Status", (uint)_dpi());
        if (theme == 0)
        {
            base.OnRenderStatusStripSizingGrip(e);
            return;
        }

        try
        {
            // As comctl32 does: a rectangle of the part's draw size in the bottom-right corner.
            var client = new NativeMethods.RECT { Left = 0, Top = 0, Right = strip.Width, Bottom = strip.Height };
            var hdc = e.Graphics.GetHdc();
            try
            {
                if (NativeMethods.GetThemePartSize(theme, hdc, NativeMethods.SP_GRIPPER, 0, client, NativeMethods.TS_DRAW, out var size) >= 0)
                {
                    var bounds = client with { Left = client.Right - size.Width, Top = client.Bottom - size.Height };
                    _ = NativeMethods.DrawThemeBackground(theme, hdc, NativeMethods.SP_GRIPPER, 0, bounds, 0);
                }
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
        }
        finally
        {
            _ = NativeMethods.CloseThemeData(theme);
        }
    }
}
