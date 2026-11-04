using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadNet.PerfHarness;

/// <summary>
/// <c>launch</c>: starts an exe (the port or a copy of the classic <c>notepad.exe</c>) with a generated file and
/// measures the time from <c>CreateProcess</c> to the first shown top-level window
/// (<c>EVENT_OBJECT_SHOW</c>) and to the Edit control reporting the whole text
/// (<c>WM_GETTEXTLENGTH</c>). Without sizes it measures startup to an empty window.
/// <c>editload</c>: loads the same text into an Edit control in this process three ways.
/// </summary>
internal static unsafe class Program
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "npport-perf");

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("""
                launch --exe <path> [--sizes 1,50,500] [--kinds ascii,utf8,utf16] [--runs 7] [--hive <settings hive of the port>]
                editload [--sizes 1,50,500] [--runs 5]
                find --exe <path> [--chars 50000000] [--kinds ascii,mixed] [--pattern zqxj] [--runs 5] [--hive <hive>]
                Sizes are file sizes in MB (10^6 bytes); files are generated under %TEMP%\npport-perf and deleted.
                """);
            return 1;
        }

        var options = Options.Parse(args.Skip(1));
        Directory.CreateDirectory(Root);
        try
        {
            switch (args[0])
            {
                case "launch":
                    Launch(options);
                    break;
                case "editload":
                    RunOnSta(() => EditLoad(options));
                    break;
                case "find":
                    FindNotFound(options);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown mode {args[0]}");
                    return 1;
            }
        }
        finally
        {
            Directory.Delete(Root, recursive: true);
        }

        return 0;
    }

    private sealed record Options(string? Exe, int[] Sizes, string[] Kinds, int Runs, string? Hive, int Chars, string Pattern, int Repeat)
    {
        public static Options Parse(IEnumerable<string> args)
        {
            var map = new Dictionary<string, string>();
            using var e = args.GetEnumerator();
            while (e.MoveNext())
            {
                var key = e.Current;
                map[key] = e.MoveNext() ? e.Current : "";
            }

            return new(
                map.GetValueOrDefault("--exe"),
                map.TryGetValue("--sizes", out var sizes) && sizes.Length > 0 ? [.. sizes.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))] : [],
                map.TryGetValue("--kinds", out var kinds) ? kinds.Split(',') : ["ascii"],
                map.TryGetValue("--runs", out var runs) ? int.Parse(runs, CultureInfo.InvariantCulture) : 7,
                map.GetValueOrDefault("--hive"),
                map.TryGetValue("--chars", out var chars) ? int.Parse(chars, CultureInfo.InvariantCulture) : 50_000_000,
                map.GetValueOrDefault("--pattern") ?? "zqxj",
                map.TryGetValue("--repeat", out var repeat) ? int.Parse(repeat, CultureInfo.InvariantCulture) : 1);
        }
    }

    // ---- Sample files ----

    /// <summary>Writes a file of <paramref name="megabytes"/> MB and returns its path and the
    /// number of UTF-16 code units the editor should end up with.</summary>
    private static (string Path, long Chars) Generate(int megabytes, string kind)
    {
        var target = (long)megabytes * 1_000_000;
        var path = Path.Combine(Root, $"{kind}-{megabytes}.txt");
        var block = Block(kind == "utf8" || kind == "utf16");
        var encoding = kind == "utf16" ? (Encoding)new UnicodeEncoding(false, false) : new UTF8Encoding(false);
        long chars = 0;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            long written = 0;
            if (kind == "utf16")
            {
                stream.Write([0xFF, 0xFE]);
                written = 2;
            }

            var bytes = encoding.GetBytes(block);
            while (written + bytes.Length <= target)
            {
                stream.Write(bytes);
                written += bytes.Length;
                chars += block.Length;
            }

            // Pad with ASCII (UTF-16: two bytes each) to the exact size.
            var unit = kind == "utf16" ? 2 : 1;
            var pad = (int)((target - written) / unit);
            stream.Write(encoding.GetBytes(new string('x', pad)));
            chars += pad;
        }

        return (path, chars);
    }

    /// <summary>64 KiB of ASCII words in CRLF lines; with <paramref name="accents"/>, some
    /// Vietnamese words so UTF-8 has multi-byte sequences.</summary>
    private static string Block(bool accents)
    {
        string[] words = ["the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "notepad", "classic", "port", "window"];
        var random = new Random(42);
        var builder = new StringBuilder();
        var line = 0;
        while (builder.Length < 65536)
        {
            var word = accents && random.Next(40) == 0 ? "tiếng" : words[random.Next(words.Length)];
            builder.Append(word);
            line += word.Length + 1;
            builder.Append(line > 80 ? "\r\n" : " ");
            if (line > 80)
            {
                line = 0;
            }
        }

        return builder.ToString();
    }

    // ---- launch ----

    private static void Launch(Options options)
    {
        if (options.Exe is null || !File.Exists(options.Exe))
        {
            throw new FileNotFoundException("--exe must name an existing executable.", options.Exe);
        }

        Console.WriteLine($"# {options.Exe}");
        Console.WriteLine("| File | Runs | First: window / text (ms) | Median: window / text (ms) | Min: window / text (ms) |");
        Console.WriteLine("|---|---|---|---|---|");
        if (options.Sizes.Length == 0)
        {
            Report("(none)", Measure(options, null, 0));
            return;
        }

        foreach (var kind in options.Kinds)
        {
            foreach (var size in options.Sizes)
            {
                var (path, chars) = Generate(size, kind);
                Report($"{kind} {size} MB", Measure(options, path, chars));
                File.Delete(path);
            }
        }
    }

    private static void Report(string label, List<(double Window, double Text)> runs)
    {
        static string Pair((double Window, double Text) r) => $"{r.Window:F0} / {r.Text:F0}";
        var median = (Median(runs.Skip(1).Select(r => r.Window)), Median(runs.Skip(1).Select(r => r.Text)));
        var min = (runs.Skip(1).Min(r => r.Window), runs.Skip(1).Min(r => r.Text));
        Console.WriteLine($"| {label} | {runs.Count} | {Pair(runs[0])} | {Pair(median)} | {Pair(min)} |");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static List<(double Window, double Text)> Measure(Options options, string? file, long chars)
    {
        var results = new List<(double, double)>();
        for (var i = 0; i < options.Runs + 1; i++)
        {
            results.Add(MeasureOnce(options.Exe!, file, chars, options.Hive));
        }

        return results;
    }

    [ThreadStatic]
    private static nint t_shown;

    [ThreadStatic]
    private static long t_shownAt;

    [UnmanagedCallersOnly]
    private static void OnShow(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (t_shown == 0 && idObject == Native.OBJID_WINDOW && idChild == 0 && Native.GetAncestor(hwnd, Native.GA_ROOT) == hwnd
            && (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION)
        {
            t_shown = hwnd;
            t_shownAt = Stopwatch.GetTimestamp();
        }
    }

    private static (double Window, double Text) MeasureOnce(string exe, string? file, long chars, string? hive)
    {
        var info = new ProcessStartInfo(exe, file is null ? "" : $"\"{file}\"") { UseShellExecute = false };
        // The port keeps settings and autosave data out of the user's profile; the original ignores both.
        info.Environment["NOTEPADNET_SETTINGS_HIVE"] = hive ?? Path.Combine(Root, "settings.dat");
        info.Environment["NOTEPADNET_AUTOSAVE_DIR"] = Path.Combine(Root, "autosave");
        t_shown = 0;
        var start = Stopwatch.GetTimestamp();
        using var process = Process.Start(info)!;
        var hook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, 0, &OnShow, (uint)process.Id, 0, Native.WINEVENT_OUTOFCONTEXT);
        try
        {
            // The hook is set after the process starts; it cannot show a window that early.
            PumpUntil(() => t_shown != 0, TimeSpan.FromSeconds(60));
            var window = Stopwatch.GetElapsedTime(start, t_shownAt).TotalMilliseconds;
            var text = window;
            if (file is not null)
            {
                var edit = FindEdit(t_shown);
                while (true)
                {
                    if (Native.SendMessageTimeout(edit, Native.WM_GETTEXTLENGTH, 0, 0, Native.SMTO_NORMAL, 120_000, out var length) != 0 && length >= chars)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }

                text = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            return (window, text);
        }
        finally
        {
            Native.UnhookWinEvent(hook);
            process.Kill();
            process.WaitForExit();
        }
    }

    private static void PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var end = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!done())
        {
            if (Stopwatch.GetTimestamp() > end)
            {
                throw new TimeoutException("The window did not show.");
            }

            _ = Native.MsgWaitForMultipleObjects(0, null, false, 5, Native.QS_ALLINPUT);
            while (Native.PeekMessage(out var msg, 0, 0, 0, Native.PM_REMOVE))
            {
                Native.TranslateMessage(msg);
                Native.DispatchMessage(msg);
            }
        }
    }

    [ThreadStatic]
    private static nint t_edit;

    [UnmanagedCallersOnly]
    private static int OnChild(nint hwnd, nint lParam)
    {
        var name = stackalloc char[256];
        var length = Native.GetClassName(hwnd, name, 256);
        if (new string(name, 0, length).Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            t_edit = hwnd;
            return 0;
        }

        return 1;
    }

    private static nint FindEdit(nint window)
    {
        t_edit = 0;
        var end = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        while (t_edit == 0 && Stopwatch.GetTimestamp() < end)
        {
            Native.EnumChildWindows(window, &OnChild, 0);
            if (t_edit == 0)
            {
                Thread.Sleep(1);
            }
        }

        return t_edit != 0 ? t_edit : throw new InvalidOperationException("No Edit control in the window.");
    }

    // ---- find ----

    /// <summary>
    /// Find without a match, measured the same way on the port and on the original: the exe is
    /// started empty, the text is put into its Edit control with <c>WM_SETTEXT</c>, Ctrl+F opens
    /// the Find dialog, "Find what" is set, Match case and Wrap around are cleared and Down is
    /// chosen; then the caret goes to 0 and Find Next is clicked with a posted <c>BN_CLICKED</c>.
    /// The time runs from that post to the "Cannot find" box being shown (<c>EVENT_OBJECT_SHOW</c>
    /// of a dialog of the process other than the Find dialog). "mixed" is ASCII with a Vietnamese
    /// word every 400 characters, as in the Benchmarks project.
    /// </summary>
    private static void FindNotFound(Options options)
    {
        if (options.Exe is null || !File.Exists(options.Exe))
        {
            throw new FileNotFoundException("--exe must name an existing executable.", options.Exe);
        }

        Console.WriteLine($"# {options.Exe}, Find \"{options.Pattern}\" without a match, {options.Chars} characters, {options.Repeat} click(s) per process");
        Console.WriteLine("| Text | Click | Runs | First (ms) | Median (ms) | Min (ms) |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var kind in options.Kinds)
        {
            var text = SampleText(options.Chars, kind == "mixed");
            var times = new List<double[]>();
            for (var i = 0; i < options.Runs + 1; i++)
            {
                times.Add(FindOnce(options, text));
            }

            // One row per click within the process: the first click pays any one-time cost.
            for (var click = 0; click < options.Repeat; click++)
            {
                var column = times.Select(t => t[click]).ToList();
                Console.WriteLine($"| {kind} | {click + 1} | {column.Count} | {column[0]:F1} | {Median(column.Skip(1)):F1} | {column.Skip(1).Min():F1} |");
            }
        }
    }

    private static string SampleText(int length, bool mixed)
    {
        string[] words = ["the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "notepad", "classic", "port", "window", "file", "edit", "format", "view", "help", "status", "bar", "line", "column", "encoding", "save", "open"];
        var random = new Random(42);
        var builder = new StringBuilder(length + 64);
        var line = 0;
        while (builder.Length < length)
        {
            var word = words[random.Next(words.Length)];
            builder.Append(word);
            line += word.Length + 1;
            if (line > 70 + random.Next(30))
            {
                builder.Append("\r\n");
                line = 0;
            }
            else
            {
                builder.Append(' ');
            }
        }

        builder.Length = length;
        if (mixed)
        {
            const string accented = "tiếng Việt ";
            for (var i = 400; i + accented.Length < length; i += 400)
            {
                for (var j = 0; j < accented.Length; j++)
                {
                    builder[i + j] = accented[j];
                }
            }
        }

        return builder.ToString();
    }

    [ThreadStatic]
    private static nint t_findDialog;

    [ThreadStatic]
    private static nint t_messageBox;

    [ThreadStatic]
    private static long t_messageBoxAt;

    [UnmanagedCallersOnly]
    private static void OnDialogShow(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != 0 || hwnd == t_findDialog || t_messageBox != 0)
        {
            return;
        }

        var name = stackalloc char[16];
        var length = Native.GetClassName(hwnd, name, 16);
        if (new string(name, 0, length) == "#32770")
        {
            t_messageBox = hwnd;
            t_messageBoxAt = Stopwatch.GetTimestamp();
        }
    }

    private static double[] FindOnce(Options options, string text)
    {
        var info = new ProcessStartInfo(options.Exe!) { UseShellExecute = false };
        info.Environment["NOTEPADNET_SETTINGS_HIVE"] = options.Hive ?? Path.Combine(Root, "settings.dat");
        info.Environment["NOTEPADNET_AUTOSAVE_DIR"] = Path.Combine(Root, "autosave");
        t_shown = 0;
        using var process = Process.Start(info)!;
        var hook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, 0, &OnShow, (uint)process.Id, 0, Native.WINEVENT_OUTOFCONTEXT);
        try
        {
            try
            {
                PumpUntil(() => t_shown != 0, TimeSpan.FromSeconds(60));
            }
            catch (TimeoutException e)
            {
                var state = process.HasExited ? $"exited with code 0x{process.ExitCode:X8}" : "still running";
                var top = FindTopLevel((uint)process.Id, null, null);
                throw new TimeoutException($"{e.Message} The process {process.Id} {state}; top-level window: 0x{top:X}.", e);
            }

            var window = t_shown;
            var edit = FindEdit(window);
            fixed (char* p = text)
            {
                Native.SendMessage(edit, Native.WM_SETTEXT, 0, (nint)p);
            }

            PumpUntil(() => Native.SendMessage(edit, Native.WM_GETTEXTLENGTH, 0, 0) == text.Length, TimeSpan.FromSeconds(120));
            Native.UnhookWinEvent(hook);
            hook = 0;

            // Ctrl+F needs the keyboard, so the window must be in the foreground.
            PumpUntil(() => BringToFront(window), TimeSpan.FromSeconds(10));
            SendCtrlF();
            var pid = (uint)process.Id;
            nint dialog = 0;
            PumpUntil(() => (dialog = FindTopLevel(pid, "#32770", "Find")) != 0, TimeSpan.FromSeconds(10));
            t_findDialog = dialog;
            var findWhat = Native.GetDlgItem(dialog, 1152);
            fixed (char* p = options.Pattern)
            {
                Native.SendMessage(findWhat, Native.WM_SETTEXT, 0, (nint)p);
            }

            // Match case (chx2 = 1041) and Wrap around (the checkbox with that text) off, Down (rad2 = 1057) on.
            Native.SendMessage(Native.GetDlgItem(dialog, 1041), Native.BM_SETCHECK, 0, 0);
            var wrap = FindChildByText(dialog, "Wrap around");
            if (wrap != 0)
            {
                Native.SendMessage(wrap, Native.BM_SETCHECK, 0, 0);
            }

            Native.SendMessage(Native.GetDlgItem(dialog, 1057), Native.BM_CLICK, 0, 0);
            hook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, 0, &OnDialogShow, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
            var findNext = Native.GetDlgItem(dialog, 1);
            var results = new double[options.Repeat];
            for (var click = 0; click < options.Repeat; click++)
            {
                Native.SendMessage(edit, Native.EM_SETSEL, 0, 0);
                t_messageBox = 0;
                var start = Stopwatch.GetTimestamp();
                Native.PostMessageW(dialog, Native.WM_COMMAND, 1, findNext); // BN_CLICKED (0) in the high word, IDOK in the low word
                PumpUntil(() => t_messageBox != 0, TimeSpan.FromSeconds(120));
                results[click] = Stopwatch.GetElapsedTime(start, t_messageBoxAt).TotalMilliseconds;
                Native.PostMessageW(t_messageBox, Native.WM_CLOSE, 0, 0);
                PumpUntil(() => !Native.IsWindow(t_messageBox), TimeSpan.FromSeconds(10));
            }

            return results;
        }
        finally
        {
            if (hook != 0)
            {
                Native.UnhookWinEvent(hook);
            }

            process.Kill();
            process.WaitForExit();
        }
    }

    private static bool BringToFront(nint hwnd)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        var current = Native.GetCurrentThreadId();
        var owner = foreground == 0 ? 0 : Native.GetWindowThreadProcessId(foreground, null);
        var attached = owner != 0 && owner != current && Native.AttachThreadInput(current, owner, true);
        try
        {
            Native.BringWindowToTop(hwnd);
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                Native.AttachThreadInput(current, owner, false);
            }
        }

        return Native.GetForegroundWindow() == hwnd;
    }

    private static void SendCtrlF()
    {
        var inputs = stackalloc Native.INPUT[4];
        inputs[0] = new() { type = Native.INPUT_KEYBOARD, ki = new() { wVk = Native.VK_CONTROL } };
        inputs[1] = new() { type = Native.INPUT_KEYBOARD, ki = new() { wVk = 'F' } };
        inputs[2] = new() { type = Native.INPUT_KEYBOARD, ki = new() { wVk = 'F', dwFlags = Native.KEYEVENTF_KEYUP } };
        inputs[3] = new() { type = Native.INPUT_KEYBOARD, ki = new() { wVk = Native.VK_CONTROL, dwFlags = Native.KEYEVENTF_KEYUP } };
        if (Native.SendInput(4, inputs, sizeof(Native.INPUT)) != 4)
        {
            throw new InvalidOperationException("SendInput failed.");
        }
    }

    [ThreadStatic]
    private static uint t_wantedPid;

    [ThreadStatic]
    private static string? t_wantedClass;

    [ThreadStatic]
    private static string? t_wantedTitle;

    [ThreadStatic]
    private static nint t_found;

    [UnmanagedCallersOnly]
    private static int OnTopLevel(nint hwnd, nint lParam)
    {
        uint pid;
        _ = Native.GetWindowThreadProcessId(hwnd, &pid);
        if (pid != t_wantedPid)
        {
            return 1;
        }

        var buffer = stackalloc char[256];
        var length = Native.GetClassName(hwnd, buffer, 256);
        if (t_wantedClass is not null && new string(buffer, 0, length) != t_wantedClass)
        {
            return 1;
        }

        length = Native.GetWindowText(hwnd, buffer, 256);
        if (t_wantedTitle is not null && new string(buffer, 0, length) != t_wantedTitle)
        {
            return 1;
        }

        t_found = hwnd;
        return 0;
    }

    private static nint FindTopLevel(uint pid, string? className, string? title)
    {
        t_wantedPid = pid;
        t_wantedClass = className;
        t_wantedTitle = title;
        t_found = 0;
        Native.EnumWindows(&OnTopLevel, 0);
        return t_found;
    }

    [UnmanagedCallersOnly]
    private static int OnChildText(nint hwnd, nint lParam)
    {
        var buffer = stackalloc char[256];
        var length = Native.GetWindowText(hwnd, buffer, 256);
        if (new string(buffer, 0, length).Replace("&", "", StringComparison.Ordinal) == t_wantedTitle)
        {
            t_found = hwnd;
            return 0;
        }

        return 1;
    }

    private static nint FindChildByText(nint parent, string text)
    {
        t_wantedTitle = text;
        t_found = 0;
        Native.EnumChildWindows(parent, &OnChildText, 0);
        return t_found;
    }

    // ---- editload ----

    private static void RunOnSta(Action body)
    {
        var thread = new Thread(() => body());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private delegate void LoadMethod(nint edit, byte[] utf8, int chars);

    /// <summary>
    /// The plan's three ways to put decoded UTF-8 into an Edit control (style <c>0x50300104</c>,
    /// no limit): a managed string through <c>WM_SETTEXT</c>; a pinned <c>char[]</c> through
    /// <c>WM_SETTEXT</c>; a <c>LocalAlloc</c> block through <c>EM_SETHANDLE</c> (the original's way,
    /// and the port's). Each timing includes the decode and ends when <c>WM_GETTEXTLENGTH</c>
    /// returns.
    /// </summary>
    private static void EditLoad(Options options)
    {
        var methods = new (string Name, LoadMethod Load)[]
        {
            ("WM_SETTEXT string", static (edit, utf8, chars) =>
            {
                var text = Encoding.UTF8.GetString(utf8);
                fixed (char* p = text)
                {
                    Native.SendMessage(edit, Native.WM_SETTEXT, 0, (nint)p);
                }
            }),
            ("WM_SETTEXT pinned char[]", static (edit, utf8, chars) =>
            {
                var buffer = GC.AllocateUninitializedArray<char>(chars + 1, pinned: true);
                var length = Encoding.UTF8.GetChars(utf8, buffer);
                buffer[length] = '\0';
                fixed (char* p = buffer)
                {
                    Native.SendMessage(edit, Native.WM_SETTEXT, 0, (nint)p);
                }
            }),
            ("EM_SETHANDLE LocalAlloc", static (edit, utf8, chars) =>
            {
                var handle = Native.LocalAlloc(Native.LMEM_MOVEABLE, (nuint)(chars + 1) * 2);
                var p = (char*)Native.LocalLock(handle);
                var length = Encoding.UTF8.GetChars(utf8, new Span<char>(p, chars + 1));
                p[length] = '\0';
                Native.LocalUnlock(handle);
                var old = Native.SendMessage(edit, Native.EM_GETHANDLE, 0, 0);
                Native.SendMessage(edit, Native.EM_SETHANDLE, handle, 0);
                Native.LocalFree(old);
            }),
        };

        Console.WriteLine("| Text | Method | First (ms) | Median (ms) | Min (ms) |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var size in options.Sizes.Length > 0 ? options.Sizes : [1, 50, 500])
        {
            var (path, chars) = Generate(size, "utf8");
            var utf8 = File.ReadAllBytes(path);
            File.Delete(path);
            foreach (var (name, load) in methods)
            {
                var times = new List<double>();
                for (var i = 0; i < options.Runs + 1; i++)
                {
                    times.Add(EditLoadOnce(load, utf8, (int)chars));
                }

                Console.WriteLine($"| UTF-8 {size} MB | {name} | {times[0]:F0} | {Median(times.Skip(1)):F0} | {times.Skip(1).Min():F0} |");
            }
        }
    }

    private static double EditLoadOnce(LoadMethod load, byte[] utf8, int chars)
    {
        var instance = Native.GetModuleHandle(null);
        var parent = Native.CreateWindowEx(0, "STATIC", null, Native.WS_POPUP, 0, 0, 800, 600, 0, 0, instance, 0);
        var edit = Native.CreateWindowEx(0, "EDIT", null, 0x50300104, 0, 0, 800, 600, parent, 0xF, instance, 0);
        Native.SendMessage(edit, Native.EM_LIMITTEXT, 0, 0);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var start = Stopwatch.GetTimestamp();
        load(edit, utf8, chars);
        var length = Native.SendMessage(edit, Native.WM_GETTEXTLENGTH, 0, 0);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Native.DestroyWindow(parent);
        return length == chars ? elapsed : throw new InvalidOperationException($"Edit control has {length} characters, expected {chars}.");
    }
}
