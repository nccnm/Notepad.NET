using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>Returns a scripted <c>IsTextUnicode</c> answer and records the input length.</summary>
internal sealed class FakeUnicodeHeuristic(bool isUnicode = false, UnicodeTests passed = UnicodeTests.None) : IUnicodeHeuristic
{
    public int Calls { get; private set; }

    public int LastLength { get; private set; } = -1;

    public UnicodeTestResult Test(ReadOnlySpan<byte> data)
    {
        Calls++;
        LastLength = data.Length;
        return new(isUnicode, passed);
    }
}
