using NotepadNet.Core.Text;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Text;

/// <summary><c>IsTextUnicode</c> with every test requested, as the original calls it.</summary>
public sealed class Win32UnicodeHeuristic : IUnicodeHeuristic
{
    public unsafe UnicodeTestResult Test(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return new(false, UnicodeTests.None);
        }

        var tests = -1;
        fixed (byte* buffer = data)
        {
            var isUnicode = Advapi32.IsTextUnicode(buffer, data.Length, ref tests);
            return new(isUnicode, (UnicodeTests)tests);
        }
    }
}
