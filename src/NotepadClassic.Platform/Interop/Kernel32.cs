using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotepadClassic.Platform.Interop;

internal static partial class Kernel32
{
    public const uint GENERIC_READ = 0x8000_0000;
    public const uint GENERIC_WRITE = 0x4000_0000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint FILE_SHARE_DELETE = 0x4;
    public const uint OPEN_EXISTING = 3;
    public const uint OPEN_ALWAYS = 4;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint INVALID_FILE_ATTRIBUTES = 0xFFFF_FFFF;
    public const int ERROR_ALREADY_EXISTS = 183;
    public const uint WC_NO_BEST_FIT_CHARS = 0x400;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetFileAttributes(string lpFileName);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteFile(string lpFileName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetACP();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial int MultiByteToWideChar(
        uint codePage, uint dwFlags, byte* lpMultiByteStr, int cbMultiByte, char* lpWideCharStr, int cchWideChar);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial int WideCharToMultiByte(
        uint codePage, uint dwFlags, char* lpWideCharStr, int cchWideChar, byte* lpMultiByteStr, int cbMultiByte,
        byte* lpDefaultChar, int* lpUsedDefaultChar);
}
