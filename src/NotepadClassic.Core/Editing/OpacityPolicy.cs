namespace NotepadClassic.Core.Editing;

/// <summary>View > Transparency rules (port addition, parity.md): the window's opacity in percent.</summary>
public static class OpacityPolicy
{
    public const int Step = 10;

    /// <summary>Low enough to see through, high enough that the window cannot be lost on screen.</summary>
    public const int Minimum = 30;

    public const int Maximum = 100;

    public static int Clamp(int percent) => Math.Clamp(percent, Minimum, Maximum);

    public static int StepBy(int percent, int delta) => Clamp(percent + delta);
}
