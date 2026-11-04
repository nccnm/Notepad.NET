using System.Runtime.InteropServices;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// Autosave through the real Restart Manager: <c>RmShutdown</c> closes a run
/// this test started, without a question, and a run started with the registered
/// <c>RestartByRestartManager:&lt;id&gt;</c> gets the unsaved text back.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed partial class RestartManagerTests
{
    [Fact]
    public void Restart_manager_shutdown_keeps_unsaved_text_and_the_restart_restores_it()
    {
        string folder;
        string id;
        using (var first = AppSession.StartWithFile("doc, comma.txt", "old", out _, keepFolder: true))
        {
            folder = first.Folder;
            Wait.Until(() => first.Text == "old", "the file text");
            first.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.END);
            first.Type(" and new");
            Wait.Until(() => first.Text == "old and new", "the typed text", () => first.Text);
            id = Wait.For(() => Rm.RegisteredRestartId(first.ProcessId), "the restart registration");

            Rm.Shutdown(first.ProcessId, first.StartTime);

            first.WaitForExit();
            File.Exists(Path.Combine(folder, "autosave", id + ".json")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "autosave", id + ".txt")).Should().BeTrue();
        }

        try
        {
            using var restarted = AppSession.StartIn(folder, "RestartByRestartManager:" + id);

            Wait.Until(() => restarted.Text == "old and new", "the restored text", () => restarted.Text);
            restarted.Title.Should().Be("*" + AppSession.FileTitle("doc, comma.txt") + " - Notepad.NET");
            Directory.GetFiles(Path.Combine(folder, "autosave"), id + ".*").Should().BeEmpty("a restored session is deleted");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static partial class Rm
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public long ProcessStartTime;
        }

        [LibraryImport("rstrtmgr.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static unsafe partial int RmStartSession(out uint session, int flags, char* key);

        [LibraryImport("rstrtmgr.dll")]
        private static unsafe partial int RmRegisterResources(
            uint session, uint files, nint fileNames, uint applications, RM_UNIQUE_PROCESS* rgApplications, uint services, nint serviceNames);

        [LibraryImport("rstrtmgr.dll")]
        private static partial int RmShutdown(uint session, uint flags, nint callback);

        [LibraryImport("rstrtmgr.dll")]
        private static partial int RmEndSession(uint session);

        [LibraryImport("kernel32.dll")]
        private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("kernel32.dll")]
        private static unsafe partial int GetApplicationRestartSettings(nint process, char* commandLine, uint* size, uint* flags);

        /// <summary>The id after <c>RestartByRestartManager:</c> registered by the process, or null.</summary>
        public static unsafe string? RegisteredRestartId(int processId)
        {
            const uint PROCESS_VM_READ = 0x0010;
            const uint PROCESS_QUERY_INFORMATION = 0x0400;
            var process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
            try
            {
                var buffer = stackalloc char[1024];
                uint size = 1024;
                uint flags;
                if (GetApplicationRestartSettings(process, buffer, &size, &flags) != 0)
                {
                    return null;
                }

                var commandLine = new string(buffer);
                const string prefix = "RestartByRestartManager:";
                return commandLine.StartsWith(prefix, StringComparison.Ordinal) ? commandLine[prefix.Length..] : null;
            }
            finally
            {
                _ = CloseHandle(process);
            }
        }

        /// <summary>A Restart Manager session over this one process, shut down without force.</summary>
        public static unsafe void Shutdown(int processId, DateTime startTime)
        {
            var key = stackalloc char[64];
            Marshal.ThrowExceptionForHR(HResult(RmStartSession(out var session, 0, key)));
            try
            {
                var application = new RM_UNIQUE_PROCESS { dwProcessId = processId, ProcessStartTime = startTime.ToFileTime() };
                Marshal.ThrowExceptionForHR(HResult(RmRegisterResources(session, 0, 0, 1, &application, 0, 0)));
                Marshal.ThrowExceptionForHR(HResult(RmShutdown(session, 0, 0)));
            }
            finally
            {
                _ = RmEndSession(session);
            }
        }

        private static int HResult(int error) => error == 0 ? 0 : unchecked((int)(0x80070000 | (uint)error));
    }
}
