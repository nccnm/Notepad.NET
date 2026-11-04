using System.Runtime.InteropServices;

namespace NotepadNet.Platform.Interop;

internal static partial class Kernel32Restart
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterApplicationRestart(string pwzCommandline, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(
        string lpDirectoryName, out ulong lpFreeBytesAvailableToCaller, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
}

internal static partial class Kernel32RestartQuery
{
    [LibraryImport("kernel32.dll")]
    public static unsafe partial int GetApplicationRestartSettings(nint hProcess, char* pwzCommandline, uint* pcchSize, uint* pdwFlags);

    [LibraryImport("kernel32.dll")]
    public static partial int UnregisterApplicationRestart();

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();
}
