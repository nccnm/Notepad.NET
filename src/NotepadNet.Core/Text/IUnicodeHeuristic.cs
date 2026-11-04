namespace NotepadNet.Core.Text;

/// <summary>Bits of the result mask of the Win32 <c>IsTextUnicode</c> heuristic that the
/// detection rules look at. Values are the documented <c>IS_TEXT_UNICODE_*</c> constants.</summary>
[Flags]
public enum UnicodeTests
{
    None = 0,
    Ascii16 = 0x0001,
    Statistics = 0x0002,
}

/// <param name="IsUnicode">Return value of the heuristic.</param>
/// <param name="PassedTests">Output mask: the tests that passed, when every test was requested.</param>
public readonly record struct UnicodeTestResult(bool IsUnicode, UnicodeTests PassedTests);

/// <summary>
/// The <c>IsTextUnicode</c> heuristic with every test requested (input mask -1).
/// Implemented by the platform layer; Core only applies the rules around it.
/// </summary>
public interface IUnicodeHeuristic
{
    UnicodeTestResult Test(ReadOnlySpan<byte> data);
}
