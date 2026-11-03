using System.Runtime.InteropServices;

namespace NotepadClassic.E2E.Tests;

/// <summary>The parent process id of a process (<c>NtQueryInformationProcess</c>, class 0).</summary>
internal static partial class ProcessParent
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2;
        public nint Reserved3;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQueryInformationProcess(nint process, int informationClass, PROCESS_BASIC_INFORMATION* information, int length, int* returned);

    public static unsafe int? Of(nint process)
    {
        PROCESS_BASIC_INFORMATION information;
        int returned;
        return NtQueryInformationProcess(process, 0, &information, sizeof(PROCESS_BASIC_INFORMATION), &returned) == 0
            ? (int)information.InheritedFromUniqueProcessId
            : null;
    }
}
