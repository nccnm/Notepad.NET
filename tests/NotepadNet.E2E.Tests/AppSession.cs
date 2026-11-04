using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// One run of the built <c>Notepad.NET.exe</c>: its own temporary folder for documents, a
/// settings hive (<c>NOTEPADNET_SETTINGS_HIVE</c>) and an autosave folder
/// (<c>NOTEPADNET_AUTOSAVE_DIR</c>), so nothing touches the user's registry or profile.
/// Dispose closes the process (killing it when it does not exit) and deletes the folder.
/// </summary>
internal sealed partial class AppSession : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly UIA3Automation _automation = new();
    private readonly Process _process;
    private readonly System.Text.StringBuilder _errors = new();

    private readonly bool _ownsFolder;

    private AppSession(string folder, string arguments, bool ownsFolder = true)
    {
        Folder = folder;
        _ownsFolder = ownsFolder;
        var info = new ProcessStartInfo(ExePath, arguments) { UseShellExecute = false, WorkingDirectory = folder, RedirectStandardError = true };
        info.Environment["NOTEPADNET_SETTINGS_HIVE"] = Path.Combine(folder, "settings.dat");
        info.Environment["NOTEPADNET_AUTOSAVE_DIR"] = Path.Combine(folder, "autosave");
        _process = Process.Start(info) ?? throw new InvalidOperationException("The app did not start.");
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_errors)
                {
                    _errors.AppendLine(e.Data);
                }
            }
        };
        _process.BeginErrorReadLine();
        Window = Wait.For(() => MainWindowOrNull(), "the main window");
    }

    /// <summary>Starts the app with <paramref name="arguments"/> in a new temporary folder.</summary>
    public static AppSession Start(string arguments = "") =>
        new(Directory.CreateTempSubdirectory("npport-e2e-").FullName, arguments);

    /// <summary>Starts the app on a new file in the session folder holding <paramref name="text"/> as UTF-8.</summary>
    /// <param name="keepFolder">The caller deletes the folder (a later run reuses it).</param>
    public static AppSession StartWithFile(string name, string text, out string path, bool keepFolder = false)
    {
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return new(folder, $"\"{path}\"", ownsFolder: !keepFolder);
    }

    /// <summary>Starts another run in the folder of an earlier one (same settings hive and
    /// autosave folder); the folder stays when this run ends.</summary>
    public static AppSession StartIn(string folder, string arguments) => new(folder, arguments, ownsFolder: false);

    public string Folder { get; }

    public int ProcessId => _process.Id;

    public DateTime StartTime => _process.StartTime;

    public Window Window { get; }

    /// <summary>The Edit control, looked up each time: Word Wrap replaces it with a new window.</summary>
    public TextBox Editor => Wait.For(() => Window.FindFirstDescendant(c => c.ByName("Text Editor"))?.AsTextBox(), "the editor");

    public bool HasExited => _process.HasExited;

    /// <summary>The executable built next to this test project (same configuration).</summary>
    public static string ExePath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NotepadNet.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new InvalidOperationException("Repository root not found.");
            }

#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Path.Combine(directory.FullName, "src", "NotepadNet.App", "bin", configuration, "net10.0-windows", "Notepad.NET.exe");
        }
    }

    private Window? MainWindowOrNull()
    {
        _process.Refresh();
        var handle = _process.MainWindowHandle;
        return handle == 0 ? null : _automation.FromHandle(handle).AsWindow();
    }

    /// <summary>Text of the Edit control (<c>WM_GETTEXT</c> on its handle).</summary>
    public string Text => Native.GetText(Editor.Properties.NativeWindowHandle.Value);

    public string Title => Native.GetText(Window.Properties.NativeWindowHandle.Value);

    /// <summary>
    /// <paramref name="fileName"/> as the title bar shows it: <c>GetFileTitleW</c> drops the
    /// extension when Explorer hides known extensions (the default, and so on CI runners).
    /// </summary>
    public static string FileTitle(string fileName) => Native.FileTitle(fileName);

    /// <summary><c>EM_GETSEL</c> of the Edit control.</summary>
    public (int Start, int End) Selection => Native.GetSelection(Editor.Properties.NativeWindowHandle.Value);

    /// <summary>Alpha of the main window (<c>LWA_ALPHA</c>); null when it is not layered.</summary>
    public byte? WindowAlpha => Native.GetAlpha(Window.Properties.NativeWindowHandle.Value);

    public static nint Handle(Window window) => window.Properties.NativeWindowHandle.Value;

    /// <summary><c>WS_EX_TOPMOST</c> of a top-level window.</summary>
    public static bool IsTopmost(Window window) => Native.IsTopmost(Handle(window));

    /// <summary>Whether <paramref name="upper"/> comes before <paramref name="lower"/> in the real z-order.</summary>
    public static bool IsAbove(Window upper, Window lower) => Native.IsAbove(Handle(upper), Handle(lower));

    /// <summary>Posts <c>WM_CLOSE</c>, which a dialog takes as Cancel; no keyboard focus involved.</summary>
    public static void Close(Window window) => Native.Close(Handle(window));

    /// <summary>The owner window (<c>GW_OWNER</c>).</summary>
    public static nint OwnerOf(Window window) => Native.OwnerOf(Handle(window));

    /// <summary><c>IsWindow</c>, on the handle read while the window was open.</summary>
    public static bool Exists(Window window) => Native.Exists(Handle(window));

    /// <summary>Alpha of a window (<c>LWA_ALPHA</c>); null when it is not layered.</summary>
    public static byte? AlphaOf(Window window) => Native.GetAlpha(Handle(window));

    /// <summary>Puts the cursor in the middle of the main window, read and set in this process's
    /// own DPI context, since a wheel message goes to the window under the cursor.</summary>
    public void MoveCursorIntoWindow() => Native.MoveCursorInto(Window.Properties.NativeWindowHandle.Value);

    /// <summary>A real wheel event; negative is toward the user.</summary>
    public static void Wheel(int delta) => Native.Wheel(delta);

    /// <summary>The five status bar parts, by their UIA names.</summary>
    public IReadOnlyList<string> StatusParts =>
        Window.FindFirstDescendant(c => c.ByName("Status Bar"))?.FindAllChildren().Select(e => e.Name).ToList() ?? [];

    /// <summary>
    /// Brings the window to the front with the caret in the editor, then types each character as a
    /// <c>KEYEVENTF_UNICODE</c> packet. Virtual-key typing goes through the user's input method,
    /// and a Vietnamese Telex one turns "ef" into "è".
    /// </summary>
    public void Type(string text)
    {
        Activate();
        Native.TypeUnicode(text);
        CheckForegroundAfterInput($"typing \"{text}\"");
    }

    /// <summary>Types into whatever has the focus now, a dialog for example, as <see cref="Type"/> does.</summary>
    public static void TypeUnicode(string text) => Native.TypeUnicode(text);

    /// <summary>Presses a shortcut such as Ctrl+S in the main window.</summary>
    public void Press(params VirtualKeyShort[] keys)
    {
        Activate();
        using (Keyboard.Pressing(keys[..^1]))
        {
            Keyboard.Type(keys[^1]);
        }

        CheckForegroundAfterInput("pressing " + string.Join("+", keys));
    }

    /// <summary>
    /// Alt+F4 on a document without unsaved changes, and waits for the exit. Not through
    /// <see cref="Press"/>: its check of the foreground after the keys would see the closing window
    /// hand the foreground over, to nothing or to another app, and call that a theft.
    /// </summary>
    public void CloseWithKeyboard()
    {
        Activate();
        using (Keyboard.Pressing(VirtualKeyShort.ALT))
        {
            Keyboard.Type(VirtualKeyShort.F4);
        }

        WaitForExit();
    }

    /// <summary>
    /// Makes the main window the foreground window with the caret in the editor, and locks the
    /// foreground (<c>LockSetForegroundWindow</c>) so that windows other processes show meanwhile
    /// cannot take it: test projects that run in parallel with this one show real dialogs (Go To,
    /// Page Setup, Find), and when the test run was started from the foreground terminal they are
    /// allowed to come to the front, which sent the keystrokes below to them instead (seen
    /// 2026-10-30 with a foreground monitor). The lock lifts itself when the user presses Alt or
    /// the foreground window goes away, so it never outlives the test.
    /// </summary>
    public void Activate()
    {
        var handle = Window.Properties.NativeWindowHandle.Value;
        Wait.Until(() => Native.BringToFront(handle), "the main window in front");
        Editor.Focus();
    }

    /// <summary>
    /// Input goes to whichever window is in the foreground when it arrives. When that is no
    /// longer a window of the app, the keys went elsewhere: fail now with the thief's name
    /// instead of waiting for an effect that never comes.
    /// </summary>
    private void CheckForegroundAfterInput(string action)
    {
        var foreground = Native.GetForegroundWindow();
        var owner = Native.ProcessIdOf(foreground);
        if (owner != _process.Id)
        {
            string name;
            try
            {
                using var thief = Process.GetProcessById((int)owner);
                name = thief.ProcessName;
            }
            catch (ArgumentException)
            {
                name = "?";
            }

            throw new InvalidOperationException(
                $"While {action}, the foreground window changed to \"{Native.GetText(foreground)}\" of process {owner} ({name}); the keys went there.");
        }
    }

    /// <summary>A top-level window of this process whose title is <paramref name="title"/>.</summary>
    public Window WaitForWindow(string title) => Wait.For(() => FindWindow(title), $"the window \"{title}\"");

    /// <summary>The same window if it is open now, otherwise null.</summary>
    public Window? FindWindow(string title) =>
        _automation.GetDesktop()
            .FindAllChildren(c => c.ByProcessId(_process.Id).And(c.ByName(title)))
            .FirstOrDefault()?.AsWindow()
        ?? Window.FindFirstChild(c => c.ByName(title).And(c.ByControlType(ControlType.Window)))?.AsWindow();

    /// <summary>Waits for the process to end on its own.</summary>
    public void WaitForExit() => Wait.Until(() => _process.HasExited, "the app to exit");

    /// <summary>Fails the test when the app wrote to stderr (an unhandled exception does).</summary>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
        }

        _process.WaitForExit();
        string errors;
        lock (_errors)
        {
            errors = _errors.ToString();
        }

        _process.Dispose();
        _automation.Dispose();
        if (_ownsFolder)
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
                // The exe may hold a file for a moment after it is killed; the folder is under %TEMP%.
            }
        }

        if (errors.Length > 0)
        {
            throw new InvalidOperationException("The app wrote to stderr:" + Environment.NewLine + errors);
        }
    }

    private static partial class Native
    {
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const uint EM_GETSEL = 0x00B0;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

        public static byte? GetAlpha(nint hwnd) =>
            GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & 0x2) != 0 ? alpha : null;

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOPMOST = 0x8;
        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr(nint hwnd, int index);

        [LibraryImport("user32.dll")]
        private static partial nint GetWindow(nint hwnd, uint command);

        [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool Exists(nint hwnd);

        public static bool IsTopmost(nint hwnd) => ((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

        public static nint OwnerOf(nint hwnd) => GetWindow(hwnd, GW_OWNER);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        public static void Close(nint hwnd) => PostMessage(hwnd, 0x0010, 0, 0);

        public static bool IsAbove(nint upper, nint lower)
        {
            for (var window = GetWindow(lower, GW_HWNDPREV); window != 0; window = GetWindow(window, GW_HWNDPREV))
            {
                if (window == upper)
                {
                    return true;
                }
            }

            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(nint hwnd, out RECT rect);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetCursorPos(int x, int y);

        [LibraryImport("user32.dll")]
        private static partial void mouse_event(uint flags, int dx, int dy, int data, nint extraInfo);

        public static void MoveCursorInto(nint hwnd)
        {
            if (!GetWindowRect(hwnd, out var rect) || !SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2))
            {
                throw new InvalidOperationException("Could not move the cursor into the window.");
            }
        }

        public static void Wheel(int delta) => mouse_event(0x0800, 0, 0, delta, 0);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(nint hwnd);

        [LibraryImport("user32.dll")]
        public static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint hwnd, nint processId);

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool BringWindowToTop(nint hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LockSetForegroundWindow(uint lockCode);

        private const uint LSFW_LOCK = 1;
        private const uint LSFW_UNLOCK = 2;

        [LibraryImport("user32.dll")]
        private static unsafe partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);

        public static unsafe uint ProcessIdOf(nint hwnd)
        {
            uint pid = 0;
            _ = GetWindowThreadProcessId(hwnd, &pid);
            return pid;
        }

        /// <summary>
        /// <c>SetForegroundWindow</c> from a background process is refused by the foreground lock;
        /// joining the input queue of the thread that owns the foreground window lifts it. Once
        /// <paramref name="hwnd"/> is in front the foreground is locked against other processes
        /// (see <see cref="AppSession.Activate"/>). True once <paramref name="hwnd"/> is in front.
        /// </summary>
        public static bool BringToFront(nint hwnd)
        {
            var current = GetCurrentThreadId();
            var foreground = GetForegroundWindow();
            var owner = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, 0);
            var attached = owner != 0 && owner != current && AttachThreadInput(current, owner, true);
            try
            {
                if (foreground != hwnd)
                {
                    LockSetForegroundWindow(LSFW_UNLOCK);
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }

                var inFront = GetForegroundWindow() == hwnd;
                if (inFront)
                {
                    LockSetForegroundWindow(LSFW_LOCK);
                }

                return inFront;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(current, owner, false);
                }
            }
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x2;
        private const uint KEYEVENTF_UNICODE = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        /// <summary><c>INPUT</c> with the keyboard member; the union is as large as <c>MOUSEINPUT</c>.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public uint type;

            [FieldOffset(8)]
            public KEYBDINPUT ki;
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static unsafe partial uint SendInput(uint count, INPUT* inputs, int size);

        public static unsafe void TypeUnicode(string text)
        {
            var inputs = stackalloc INPUT[2];
            foreach (var c in text)
            {
                inputs[0] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } };
                inputs[1] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } };
                if (SendInput(2, inputs, sizeof(INPUT)) != 2)
                {
                    throw new InvalidOperationException($"SendInput failed ({Marshal.GetLastPInvokeError()}).");
                }
            }
        }

        public static unsafe string GetText(nint hwnd)
        {
            var length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, 0, 0);
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
            {
                var copied = (int)SendMessage(hwnd, WM_GETTEXT, buffer.Length, (nint)p);
                return new string(p, 0, copied);
            }
        }

        [LibraryImport("comdlg32.dll", EntryPoint = "GetFileTitleW", StringMarshalling = StringMarshalling.Utf16)]
        private static unsafe partial short GetFileTitle(string file, char* buffer, ushort size);

        public static unsafe string FileTitle(string fileName)
        {
            var buffer = stackalloc char[260];
            if (GetFileTitle(fileName, buffer, 260) != 0)
            {
                throw new InvalidOperationException($"GetFileTitleW failed for \"{fileName}\".");
            }

            return new string(buffer);
        }

        public static unsafe (int, int) GetSelection(nint hwnd)
        {
            int start, end;
            SendMessage(hwnd, EM_GETSEL, (nint)(&start), (nint)(&end));
            return (start, end);
        }
    }
}

/// <summary>Condition waits: poll until true or fail after <see cref="AppSession.Timeout"/>.</summary>
internal static class Wait
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(25);

    /// <param name="state">Describes what was seen instead, for the timeout message.</param>
    public static void Until(Func<bool> condition, string what, Func<string>? state = null)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > AppSession.Timeout)
            {
                throw new TimeoutException($"Timed out waiting for {what}." + (state is null ? "" : $" Seen: {state()}"));
            }

            Thread.Sleep(Interval);
        }
    }

    public static T For<T>(Func<T?> probe, string what)
        where T : class
    {
        T? value = null;
        Until(() => (value = probe()) is not null, what);
        return value!;
    }
}

/// <summary>Waited lookups of dialog controls.</summary>
internal static class Ui
{
    public static AutomationElement Child(AutomationElement parent, string automationId) =>
        Wait.For(() => parent.FindFirstChild(c => c.ByAutomationId(automationId)), $"control {automationId}");

    public static AutomationElement Descendant(AutomationElement parent, string automationId) =>
        Wait.For(() => parent.FindFirstDescendant(c => c.ByAutomationId(automationId)), $"control {automationId}");

    /// <summary>The check mark of a menu item as UIA reports it; a ToolStripMenuItem has no toggle pattern when unchecked.</summary>
    public static bool IsChecked(AutomationElement item) =>
        (item.Patterns.LegacyIAccessible.Pattern.State.Value & AccessibilityState.STATE_SYSTEM_CHECKED) != 0;

    public static MenuItem MenuItem(AutomationElement parent, string name) =>
        Wait.For(() => parent.FindFirstDescendant(c => c.ByName(name).And(c.ByControlType(ControlType.MenuItem)))?.AsMenuItem(), $"menu {name}");
}
