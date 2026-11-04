using NotepadNet.App.Interop;
using NotepadNet.Core.Settings;

namespace NotepadNet.App.Shell;

/// <summary>
/// Saved window position (steps 9 and 11 of <c>InitApplication</c>,
/// <c>SaveSettings</c>): the <c>iWindowPos*</c> values are DPI-unaware (96-DPI) coordinates, read
/// and written with <c>Get/SetWindowPlacement</c> under <c>DPI_AWARENESS_CONTEXT_UNAWARE</c>.
/// </summary>
internal static class WindowPlacement
{
    /// <summary>
    /// Applies a saved position before the window is shown and returns the baseline for the save
    /// on exit. Only when both X and Y are set: the window first takes X, Y, DX, DY in its own
    /// (per-monitor) pixels as <c>CreateWindowExW</c> would, then the rectangle
    /// <c>{X, Y, X + width, Y + height}</c> is applied DPI-unaware, so DX and DY end up as 96-DPI
    /// sizes too. A size left at <c>CW_USEDEFAULT</c> keeps the default size.
    /// </summary>
    public static WindowRect Apply(nint hwnd, WindowPosition position)
    {
        if (!position.IsSet)
        {
            return default;
        }

        var keepSize = position.Width == NotepadSettings.UseDefault || position.Height == NotepadSettings.UseDefault;
        NativeMethods.SetWindowPos(
            hwnd, 0, position.X, position.Y, position.Width, position.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | (keepSize ? NativeMethods.SWP_NOSIZE : 0));

        var placement = new NativeMethods.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
        {
            return default;
        }

        var normal = placement.rcNormalPosition;
        var baseline = new WindowRect(
            position.X, position.Y, position.X + (normal.Right - normal.Left), position.Y + (normal.Bottom - normal.Top));
        var apply = new NativeMethods.WINDOWPLACEMENT
        {
            length = placement.length,
            rcNormalPosition = new NativeMethods.RECT { Left = baseline.Left, Top = baseline.Top, Right = baseline.Right, Bottom = baseline.Bottom },
        };
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE);
        try
        {
            NativeMethods.SetWindowPlacement(hwnd, apply);
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }

        return baseline;
    }

    /// <summary><c>GetWindowPlacement</c> under <c>DPI_AWARENESS_CONTEXT_UNAWARE</c>; null on failure.</summary>
    public static WindowRect? GetUnaware(nint hwnd)
    {
        var placement = new NativeMethods.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_UNAWARE);
        bool ok;
        try
        {
            ok = NativeMethods.GetWindowPlacement(hwnd, ref placement);
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }

        var r = placement.rcNormalPosition;
        return ok ? new WindowRect(r.Left, r.Top, r.Right, r.Bottom) : null;
    }
}
