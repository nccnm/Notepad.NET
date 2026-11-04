using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Win32;

namespace NotepadNet.ParityProbes;

/// <summary>
/// Re-runs observations of the original that parity.md relies on. Each probe prints what it saw;
/// the exit code is 0 when every observation still holds.
/// <code>
/// dotnet run --project tests/NotepadNet.ParityProbes -c Release -- --exe path [probe ...]
/// </code>
/// <c>--exe</c> names a copy of the classic <c>notepad.exe</c> 10.0.26100.8457: on Windows 11 the
/// one in System32 starts the Store Notepad when that is installed.
/// Probes: <c>goto-wordwrap</c> (Go To is grayed while Word Wrap is on), <c>restart-manager</c>
/// (a Restart Manager shutdown autosaves without asking, the restart restores the dirty text).
/// </summary>
internal static partial class Program
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static int Main(string[] args)
    {
        string? exe = null;
        var probes = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--exe" && i + 1 < args.Length)
            {
                exe = args[++i];
            }
            else
            {
                probes.Add(args[i]);
            }
        }

        if (exe is null || !File.Exists(exe))
        {
            Console.Error.WriteLine("Usage: --exe <path of the classic notepad.exe> [probe ...]");
            return 2;
        }

        if (probes.Count == 0)
        {
            probes.AddRange(["goto-wordwrap", "restart-manager"]);
        }

        var failures = 0;
        using (new RegistryGuard(@"Software\Microsoft\Notepad"))
        {
            foreach (var probe in probes)
            {
                Console.WriteLine($"== {probe}");
                var held = probe switch
                {
                    "goto-wordwrap" => GoToWithWordWrap(exe),
                    "restart-manager" => RestartManager(exe),
                    _ => throw new ArgumentException($"Unknown probe {probe}"),
                };
                Console.WriteLine(held ? "   holds" : "   CHANGED");
                failures += held ? 0 : 1;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    // ---- Probes ----

    /// <summary>parity.md "Go To": with Word Wrap on, Edit > Go To is grayed (observed 2026-10-29).</summary>
    private static bool GoToWithWordWrap(string exe)
    {
        var results = new List<bool>();
        foreach (var wrap in new[] { 1, 0 })
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Notepad", "fWrap", wrap, RegistryValueKind.DWord);
            using var run = new Run(exe, "");
            var editMenu = Run.Wait(() => run.Window.FindFirstDescendant(c => c.ByName("Edit").And(c.ByControlType(ControlType.MenuItem)))?.AsMenuItem());
            editMenu.Expand();
            var goTo = Run.Wait(() => run.Automation.GetDesktop().FindFirstDescendant(c => c.ByName("Go To...\tCtrl+G").Or(c.ByName("Go To..."))));
            Console.WriteLine($"   fWrap={wrap}: Go To enabled = {goTo.IsEnabled}");
            results.Add(goTo.IsEnabled == (wrap == 0));
            editMenu.Collapse();
        }

        return results.All(r => r);
    }

    /// <summary>parity.md "Autosave và Restart Manager": RmShutdown closes a dirty window without a
    /// question, writes Autosave\&lt;GUID&gt;, and the restart restores the text marked dirty.</summary>
    private static bool RestartManager(string exe)
    {
        var folder = Directory.CreateTempSubdirectory("npport-probe-").FullName;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notepad");
        var appDataExisted = Directory.Exists(appData);
        var appDataFiles = appDataExisted ? Directory.GetFiles(appData).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        try
        {
            var path = Path.Combine(folder, "doc.txt");
            File.WriteAllText(path, "old");
            string id;
            using (var run = new Run(exe, $"\"{path}\""))
            {
                var edit = run.Edit;
                Native.SendMessage(edit, Native.EM_SETSEL, 0, -1);
                Native.ReplaceSelection(edit, "new text");
                id = Run.Wait(() => Native.RestartId(run.Process.Id));
                Console.WriteLine($"   restart id {id}");
                Native.RestartManagerShutdown(run.Process.Id, run.Process.StartTime);
                run.Process.WaitForExit((int)Timeout.TotalMilliseconds);
                Console.WriteLine($"   exited without a question: {run.Process.HasExited}");
                if (!run.Process.HasExited)
                {
                    return false;
                }
            }

            var value = Registry.GetValue($@"HKEY_CURRENT_USER\Software\Microsoft\Notepad\Autosave\{id}", path, null) as string;
            Console.WriteLine($"   Autosave value: {value}");
            using var restarted = new Run(exe, "RestartByRestartManager:" + id);
            var restored = Run.Wait(() => Native.GetText(restarted.Edit) == "new text" ? "yes" : null);
            var dirty = Native.SendMessage(restarted.Edit, Native.EM_GETMODIFY, 0, 0) != 0;
            var title = Native.GetText(restarted.Window.Properties.NativeWindowHandle.Value);
            Console.WriteLine($"   restored: {restored}, modified: {dirty}, title: {title}");
            return value is not null && value.StartsWith("5,", StringComparison.Ordinal) && dirty && title.StartsWith("*doc.txt", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            // The original leaves probe.autosave behind; remove what this probe added.
            if (Directory.Exists(appData))
            {
                foreach (var file in Directory.GetFiles(appData).Where(f => !appDataFiles.Contains(f)))
                {
                    File.Delete(file);
                }

                if (!appDataExisted && Directory.GetFiles(appData).Length == 0)
                {
                    Directory.Delete(appData);
                }
            }
        }
    }

    // ---- Driving the original ----

    /// <summary>One run of the original, killed on dispose.</summary>
    private sealed class Run : IDisposable
    {
        public Run(string exe, string arguments)
        {
            Process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })!;
            Window = Wait(() =>
            {
                Process.Refresh();
                return Process.MainWindowHandle == 0 ? null : Automation.FromHandle(Process.MainWindowHandle).AsWindow();
            });
        }

        public UIA3Automation Automation { get; } = new();

        public Process Process { get; }

        public Window Window { get; }

        public nint Edit => Wait(() => Window.FindFirstChild(c => c.ByClassName("Edit")))!.Properties.NativeWindowHandle.Value;

        public static T Wait<T>(Func<T?> probe)
            where T : class
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                if (probe() is { } value)
                {
                    return value;
                }

                if (watch.Elapsed > Timeout)
                {
                    throw new TimeoutException("The original did not reach the expected state.");
                }

                Thread.Sleep(25);
            }
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill();
                Process.WaitForExit();
            }

            Process.Dispose();
            Automation.Dispose();
        }
    }

    /// <summary>
    /// Exports a key of HKCU with reg.exe and, on dispose, deletes the key and imports the export,
    /// then exports again and requires the same bytes (a .reg import does not remove values added
    /// meanwhile, hence the delete).
    /// </summary>
    private sealed class RegistryGuard : IDisposable
    {
        private readonly string _key;
        private readonly string _backup = Path.Combine(Path.GetTempPath(), $"npport-probe-{Environment.ProcessId}.reg");

        public RegistryGuard(string key)
        {
            _key = key;
            RunReg($"export \"HKCU\\{key}\" \"{_backup}\" /y");
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
            RunReg($"import \"{_backup}\"");
            var check = _backup + ".check";
            RunReg($"export \"HKCU\\{_key}\" \"{check}\" /y");
            var same = File.ReadAllBytes(check).AsSpan().SequenceEqual(File.ReadAllBytes(_backup));
            File.Delete(check);
            if (!same)
            {
                throw new InvalidOperationException($"HKCU\\{_key} differs from its backup {_backup}; restore it by hand.");
            }

            File.Delete(_backup);
            Console.WriteLine($"Restored HKCU\\{_key} (same bytes as before).");
        }

        private static void RunReg(string arguments)
        {
            using var reg = Process.Start(new ProcessStartInfo("reg.exe", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
            reg.WaitForExit();
            if (reg.ExitCode != 0)
            {
                throw new InvalidOperationException($"reg {arguments} failed: {reg.StandardError.ReadToEnd()}");
            }
        }
    }

    private static partial class Native
    {
        public const uint EM_SETSEL = 0x00B1;
        public const uint EM_GETMODIFY = 0x00B8;
        private const uint EM_REPLACESEL = 0x00C2;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        public static unsafe void ReplaceSelection(nint edit, string text)
        {
            fixed (char* p = text)
            {
                SendMessage(edit, EM_REPLACESEL, 1, (nint)p);
            }
        }

        public static unsafe string GetText(nint hwnd)
        {
            var length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, 0, 0);
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
            {
                return new string(p, 0, (int)SendMessage(hwnd, WM_GETTEXT, buffer.Length, (nint)p));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public long ProcessStartTime;
        }

        [LibraryImport("rstrtmgr.dll")]
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

        public static unsafe string? RestartId(int processId)
        {
            var process = OpenProcess(0x0400 | 0x0010, false, processId);
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

        public static unsafe void RestartManagerShutdown(int processId, DateTime startTime)
        {
            var key = stackalloc char[64];
            if (RmStartSession(out var session, 0, key) != 0)
            {
                throw new InvalidOperationException("RmStartSession failed.");
            }

            try
            {
                var application = new RM_UNIQUE_PROCESS { dwProcessId = processId, ProcessStartTime = startTime.ToFileTime() };
                if (RmRegisterResources(session, 0, 0, 1, &application, 0, 0) != 0 || RmShutdown(session, 0, 0) != 0)
                {
                    throw new InvalidOperationException("Restart Manager shutdown failed.");
                }
            }
            finally
            {
                _ = RmEndSession(session);
            }
        }
    }
}
