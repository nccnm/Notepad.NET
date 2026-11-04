using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NotepadNet.App.Interop;

namespace NotepadNet.App.Shell;

/// <summary>
/// Geometry of the original's native bars at a DPI, read from the system rather than from
/// formulas: the original draws a USER32 menu bar and an <c>msctls_statusbar32</c>, whose
/// metrics come from inside USER32/comctl32 and the user's non-client metrics.
/// </summary>
internal static class NativeBarMetrics
{
    /// <summary>
    /// Left edge, width and height of each item of a Win32 menu bar with these texts (the left
    /// edge relative to the client area, the top is the menu bar's): a hidden window with a real
    /// menu is created over <paramref name="bounds"/> (so it has the same DPI), measured with
    /// <c>GetMenuItemRect</c> and destroyed. Null when the probe fails.
    /// </summary>
    public static (int Left, int Width, int Height)[]? MenuItems(IReadOnlyList<string> texts, Rectangle bounds)
    {
        var probe = NativeMethods.CreateWindowEx(
            0, "Static", null, NativeMethods.WS_OVERLAPPEDWINDOW, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0, 0, 0, 0);
        if (probe == 0)
        {
            return null;
        }

        var menu = NativeMethods.CreateMenu();
        try
        {
            if (menu == 0)
            {
                return null;
            }

            for (var i = 0; i < texts.Count; i++)
            {
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, i + 1, texts[i]);
            }

            if (!NativeMethods.SetWindowMenu(probe, menu))
            {
                return null;
            }

            var origin = new NativeMethods.POINT();
            NativeMethods.ClientToScreen(probe, ref origin);
            var items = new (int Left, int Width, int Height)[texts.Count];
            for (var i = 0; i < texts.Count; i++)
            {
                if (!NativeMethods.GetMenuItemRect(probe, menu, (uint)i, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return null;
                }

                items[i] = (rect.Left - origin.X, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return items;
        }
        finally
        {
            // DestroyWindow destroys the attached menu too; a menu never attached is freed here.
            if (menu != 0 && NativeMethods.GetMenu(probe) != menu)
            {
                NativeMethods.DestroyMenu(menu);
            }

            NativeMethods.DestroyWindow(probe);
        }
    }

    /// <summary>
    /// Where <c>msctls_statusbar32</c> starts a part's text, relative to the part's left edge
    /// and the status window's top, when it uses <paramref name="font"/>: a hidden status bar
    /// on <paramref name="parent"/> paints a reference text into a bitmap
    /// (<c>WM_PRINTCLIENT</c>), and its ink is compared with the same text drawn by
    /// <see cref="TextRenderer"/> at (0, 0). Null when the probe fails.
    /// </summary>
    public static unsafe Point? StatusTextOrigin(nint parent, Font font)
    {
        const string Reference = "H";
        var probe = NativeMethods.CreateWindowEx(0, "msctls_statusbar32", null, NativeMethods.WS_CHILD, 0, 0, 0, 0, parent, 0, 0, 0);
        if (probe == 0)
        {
            return null;
        }

        try
        {
            var hfont = font.ToHfont();
            try
            {
                NativeMethods.SendMessage(probe, NativeMethods.WM_SETFONT, hfont, 0);
                NativeMethods.SendMessage(probe, NativeMethods.WM_SIZE, 0, 0);
                if (!NativeMethods.GetClientRect(probe, out var client) || client.Right <= 0 || client.Bottom <= 0)
                {
                    return null;
                }

                var oneWidePart = -1;
                NativeMethods.SendMessage(probe, NativeMethods.SB_SETPARTS, 1, (nint)(&oneWidePart));
                fixed (char* text = Reference)
                {
                    NativeMethods.SendMessage(probe, NativeMethods.SB_SETTEXTW, 0, (nint)text);
                }

                using var native = new Bitmap(client.Right, client.Bottom);
                using (var g = Graphics.FromImage(native))
                {
                    g.Clear(Color.White);
                    var hdc = g.GetHdc();
                    try
                    {
                        NativeMethods.SendMessage(probe, NativeMethods.WM_PRINTCLIENT, hdc, NativeMethods.PRF_CLIENT | NativeMethods.PRF_ERASEBKGND);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                using var drawn = new Bitmap(client.Right, client.Bottom);
                using (var g = Graphics.FromImage(drawn))
                {
                    g.Clear(Color.White);
                    TextRenderer.DrawText(g, Reference, font, Point.Empty, Color.Black, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }

                // Only the left half: the gripper sits at the right end.
                var half = client.Right / 2;
                return Ink(native, half) is { } a && Ink(drawn, half) is { } b ? new Point(a.X - b.X, a.Y - b.Y) : null;
            }
            finally
            {
                NativeMethods.DeleteObject(hfont);
            }
        }
        finally
        {
            NativeMethods.DestroyWindow(probe);
        }
    }

    /// <summary>Top-left corner of the dark pixels in the first <paramref name="width"/> columns.
    /// Reads the pixels in one <c>LockBits</c> (<c>GetPixel</c> per pixel took about 18 ms of the
    /// startup at 144 DPI).</summary>
    private static unsafe Point? Ink(Bitmap bitmap, int width)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int left = int.MaxValue, top = int.MaxValue;
            for (var y = 0; y < data.Height; y++)
            {
                var row = (byte*)data.Scan0 + (long)y * data.Stride;
                for (var x = 0; x < width; x++)
                {
                    // BGRA in memory.
                    var pixel = row + x * 4;
                    if (pixel[0] + pixel[1] + pixel[2] < 300)
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                    }
                }
            }

            return left == int.MaxValue ? null : new Point(left, top);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>The menu bar (<c>lfMenuFont</c>) and status bar (<c>lfStatusFont</c>) fonts of
    /// the non-client metrics at <paramref name="dpi"/>; null when they cannot be read.</summary>
    public static unsafe (Font Menu, Font Status)? Fonts(int dpi)
    {
        var metrics = stackalloc byte[NativeMethods.NonClientMetricsSize];
        *(uint*)metrics = NativeMethods.NonClientMetricsSize;
        if (!NativeMethods.SystemParametersInfoForDpi(NativeMethods.SPI_GETNONCLIENTMETRICS, NativeMethods.NonClientMetricsSize, metrics, 0, (uint)dpi))
        {
            return null;
        }

        var menu = Marshal.PtrToStructure<NativeMethods.LOGFONT>((nint)(metrics + NativeMethods.NonClientMetricsMenuFont))!;
        var status = Marshal.PtrToStructure<NativeMethods.LOGFONT>((nint)(metrics + NativeMethods.NonClientMetricsStatusFont))!;
        return (Font.FromLogFont(menu), Font.FromLogFont(status));
    }
}
