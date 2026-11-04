namespace NotepadNet.Core.Editing;

/// <summary>View > Zoom rules (commands <c>0x22</c>-<c>0x24</c> and the zoom part of the status bar).</summary>
public static class ZoomPolicy
{
    public const int Step = 10;
    public const int Minimum = 10;
    public const int Maximum = 500;
    public const int Default = 100;

    /// <summary>Percentage shown in the status bar: a zero numerator or denominator counts as
    /// 1/1; otherwise <c>num * 100 / den</c> in unsigned integer division.</summary>
    public static int Percent(ZoomRatio zoom) =>
        zoom.Numerator == 0 || zoom.Denominator == 0
            ? Default
            : (int)(unchecked((uint)zoom.Numerator * 100u) / (uint)zoom.Denominator);

    /// <summary>
    /// Zoom after one step in or out, clamped to [10, 500]. Deliberate difference (parity.md):
    /// the original subtracts unsigned, so a zoom under 10% (not reachable from the menu)
    /// wraps around before the clamp; the port clamps it to 10%.
    /// </summary>
    public static ZoomRatio StepBy(ZoomRatio current, int delta) =>
        new(Math.Clamp(Percent(current) + delta, Minimum, Maximum), 100);

    public static ZoomRatio Restore() => new(Default, 100);
}
