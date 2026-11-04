using System.ComponentModel;
using System.Runtime.InteropServices;
using NotepadNet.Core.Text;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Text;

/// <summary>Code page conversions through <c>MultiByteToWideChar</c>/<c>WideCharToMultiByte</c>.</summary>
public sealed class Win32CodePageConverter : ICodePageConverter
{
    public int AnsiCodePage => (int)Kernel32.GetACP();

    public unsafe int GetCharCount(int codePage, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        fixed (byte* source = bytes)
        {
            return Check(Kernel32.MultiByteToWideChar((uint)codePage, 0, source, bytes.Length, null, 0));
        }
    }

    public unsafe int GetChars(int codePage, ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        fixed (byte* source = bytes)
        fixed (char* destination = chars)
        {
            return Check(Kernel32.MultiByteToWideChar((uint)codePage, 0, source, bytes.Length, destination, chars.Length));
        }
    }

    public unsafe int GetByteCount(int codePage, ReadOnlySpan<char> chars, bool noBestFit, out bool usedDefaultChar)
    {
        usedDefaultChar = false;
        if (chars.IsEmpty)
        {
            return 0;
        }

        // The API rejects lpUsedDefaultChar for UTF-7/UTF-8/GB18030; the original passes NULL there.
        var canReportDefault = codePage is not (65000 or 65001 or TextEncodingKinds.Gb18030CodePage);
        var used = 0;
        fixed (char* source = chars)
        {
            // Zero is a meaningful answer here (it triggers the loss warning), so no Check().
            var count = Kernel32.WideCharToMultiByte(
                (uint)codePage, Flags(noBestFit), source, chars.Length, null, 0, null, canReportDefault ? &used : null);
            usedDefaultChar = used != 0;
            return count;
        }
    }

    public unsafe int GetBytes(int codePage, ReadOnlySpan<char> chars, Span<byte> bytes, bool noBestFit)
    {
        if (chars.IsEmpty)
        {
            return 0;
        }

        fixed (char* source = chars)
        fixed (byte* destination = bytes)
        {
            return Check(Kernel32.WideCharToMultiByte(
                (uint)codePage, Flags(noBestFit), source, chars.Length, destination, bytes.Length, null, null));
        }
    }

    private static uint Flags(bool noBestFit) => noBestFit ? Kernel32.WC_NO_BEST_FIT_CHARS : 0;

    private static int Check(int result) =>
        result > 0 ? result : throw new Win32Exception(Marshal.GetLastPInvokeError());
}
