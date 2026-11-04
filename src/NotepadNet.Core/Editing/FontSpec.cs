namespace NotepadNet.Core.Editing;

/// <summary>
/// The editor font as the original stores it: the <c>LOGFONTW</c> fields kept in the registry
/// plus the point size in tenths, as in the registry. <c>lfHeight</c> is never
/// stored; it is recomputed from <see cref="PointSize10"/> and the window DPI, and
/// <c>lfWidth</c> is always 0.
/// </summary>
public sealed record FontSpec(
    string FaceName,
    int PointSize10,
    int Weight = 0,
    bool Italic = false,
    bool Underline = false,
    bool StrikeOut = false,
    byte CharSet = 0,
    byte OutPrecision = 0,
    byte ClipPrecision = 0,
    byte Quality = 0,
    byte PitchAndFamily = 0,
    int Escapement = 0,
    int Orientation = 0)
{
    /// <summary>Default when neither HKCU nor the machine-wide default font is set.</summary>
    public static FontSpec Default { get; } = new("Lucida Console", 100);

    /// <summary><c>lfHeight = -MulDiv(pointSize10, dpi, 720)</c> (Font command, <c>WM_DPICHANGED</c>).</summary>
    public int HeightForDpi(int dpi) => -Win32Math.MulDiv(PointSize10, dpi, 720);
}

/// <summary>Integer helpers with Win32 semantics.</summary>
public static class Win32Math
{
    /// <summary>
    /// <c>MulDiv</c>: <c>a * b / c</c> computed in 64 bits and rounded half away from zero;
    /// -1 when <paramref name="c"/> is 0 or the result does not fit in 32 bits.
    /// </summary>
    public static int MulDiv(int a, int b, int c)
    {
        if (c == 0)
        {
            return -1;
        }

        var product = (long)a * b;
        var negative = (product < 0) != (c < 0);
        var magnitude = (Math.Abs(product) + (Math.Abs((long)c) / 2)) / Math.Abs((long)c);
        var result = negative ? -magnitude : magnitude;
        return result is < int.MinValue or > int.MaxValue ? -1 : (int)result;
    }

    /// <summary><c>MulDiv(value, dpi, 96)</c>: scales a 96-DPI logical size.</summary>
    public static int Scale(int value, int dpi) => MulDiv(value, dpi, 96);
}
