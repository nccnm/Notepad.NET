using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.CSharp.RuntimeBinder;

namespace NotepadNet.E2E.Tests;

/// <summary>
/// File > Copy Full Path and File > Open Containing Folder against the real clipboard and Explorer.
/// The user's clipboard is put back after each test (<see cref="SavedClipboard"/>), and only the Explorer
/// windows of the temporary folders are closed.
/// </summary>
[Collection(DesktopTests.Name)]
public sealed partial class FilePathCommandsTests
{
    private const string CopyItem = "Copy Full Path";
    private const string FolderItem = "Open Containing Folder";

    private static void Run(AppSession app, string name)
    {
        Ui.MenuItem(app.Window, "File").Expand();
        var item = Ui.MenuItem(app.Window, name);
        item.IsEnabled.Should().BeTrue();
        item.Invoke();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void An_untitled_document_grays_both_items()
    {
        using var app = AppSession.Start();

        Ui.MenuItem(app.Window, "File").Expand();

        Ui.MenuItem(app.Window, CopyItem).IsEnabled.Should().BeFalse();
        Ui.MenuItem(app.Window, FolderItem).IsEnabled.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_file_opened_by_a_relative_name_copies_its_full_path()
    {
        using var saved = new SavedClipboard();
        var folder = Directory.CreateTempSubdirectory("npport-e2e-").FullName;
        try
        {
            var path = Path.Combine(folder, "note.txt");
            File.WriteAllText(path, "text");
            using var app = AppSession.StartIn(folder, "note.txt");
            Wait.Until(() => app.Text == "text", "the file text");

            Run(app, CopyItem);

            Wait.Until(() => SavedClipboard.ReadText() == path, "the path on the clipboard", SavedClipboard.ReadText);
            app.Text.Should().Be("text");
            app.Title.Should().NotStartWith("*");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_clipboard_held_by_another_program_is_reported()
    {
        using var saved = new SavedClipboard();
        using var app = AppSession.StartWithFile("note.txt", "text", out var path);
        Wait.Until(() => app.Text == "text", "the file text");

        var holder = SavedClipboard.Hold();
        try
        {
            // By keyboard: the message is modal, and a UIA Invoke would wait for it to close.
            app.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_F);
            Keyboard.Type(VirtualKeyShort.KEY_C);
            var dialog = app.WaitForWindow("Notepad.NET");
            Wait.For(() => dialog.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
            Wait.Until(() => app.FindWindow("Notepad.NET") is null, "the message to close");
        }
        finally
        {
            SavedClipboard.Release(holder);
        }

        app.Text.Should().Be("text");
        SavedClipboard.ReadText().Should().NotBe(path, "the copy failed");
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void The_file_is_selected_in_explorer_even_with_a_comma_in_its_name()
    {
        using var app = AppSession.StartWithFile("a, b đ.txt", "text", out var path);
        Wait.Until(() => app.Text == "text", "the file text");
        try
        {
            // The command again while nothing is selected: on the CI runner Explorer ignores the selection for
            // a window it has just opened, with explorer /select as well, and takes it once the window is open.
            Wait.Until(
                () =>
                {
                    Run(app, FolderItem);
                    Thread.Sleep(1500);
                    return SelectedIn(app.Folder) == path;
                },
                "the file selected in Explorer",
                () => DescribeExplorer(app.Folder));
        }
        finally
        {
            CloseExplorer(app.Folder);
        }
    }

    [Fact]
    [Trait("Doc", "parity.md#duong-dan-file")]
    public void A_file_that_is_gone_still_opens_its_folder()
    {
        using var app = AppSession.StartWithFile("note.txt", "text", out var path);
        Wait.Until(() => app.Text == "text", "the file text");
        Thread.Sleep(500); // The watch starts a moment after the load.
        File.Delete(path);
        var notice = app.WaitForWindow("Notepad.NET");
        Wait.For(() => notice.FindFirstDescendant(c => c.ByName("OK"))?.AsButton(), "OK").Invoke();
        Wait.Until(() => app.FindWindow("Notepad.NET") is null, "the notice to close");
        try
        {
            Run(app, FolderItem);

            Wait.Until(() => IsOpen(app.Folder), "the folder in Explorer");
            app.FindWindow("Notepad.NET").Should().BeNull();
        }
        finally
        {
            CloseExplorer(app.Folder);
        }
    }

    private static IEnumerable<dynamic> ExplorerWindows()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic windows = shell.Windows();
        for (var i = 0; i < (int)windows.Count; i++)
        {
            var window = windows.Item(i);
            if (window is not null)
            {
                yield return window;
            }
        }
    }

    /// <summary>A window that is opening or closing answers with one of these for a moment.</summary>
    private static T? Ask<T>(Func<T?> question)
    {
        try
        {
            return question();
        }
        catch (Exception e) when (e is COMException or RuntimeBinderException or InvalidCastException)
        {
            return default;
        }
    }

    private static bool Shows(dynamic window, string folder) =>
        Ask<string>(() => (string)window.Document.Folder.Self.Path) is { } shown && string.Equals(shown, folder, StringComparison.OrdinalIgnoreCase);

    private static bool IsOpen(string folder) => Ask(() => ExplorerWindows().Any(w => (bool)Shows(w, folder)));

    private static string? SelectedIn(string folder) => Ask<string>(() =>
    {
        foreach (var window in ExplorerWindows())
        {
            if (Shows(window, folder))
            {
                var selected = window.Document.SelectedItems();
                return (int)selected.Count == 1 ? (string)selected.Item(0).Path : null;
            }
        }

        return null;
    });

    /// <summary>What Explorer shows, for the message of a test that times out.</summary>
    private static string DescribeExplorer(string folder)
    {
        try
        {
            var seen = new List<string>();
            foreach (var window in ExplorerWindows())
            {
                string shown = Ask<string>(() => (string)window.Document.Folder.Self.Path) ?? "(no folder)";
                int? count = Ask<int?>(() => (int)window.Document.SelectedItems().Count);
                seen.Add($"{shown} selected={count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
            }

            return $"expected folder {folder}; windows: [{string.Join("; ", seen)}]";
        }
        catch (Exception e) when (e is COMException or RuntimeBinderException or InvalidCastException)
        {
            return $"expected folder {folder}; {e.GetType().Name}: {e.Message}";
        }
    }

    private static void CloseExplorer(string folder)
    {
        foreach (var window in ExplorerWindows().ToList())
        {
            if (Shows(window, folder))
            {
                window.Quit();
            }
        }
    }

    /// <summary>Copies what is on the clipboard and writes it back on dispose. Formats without a
    /// memory block, such as a bitmap handle, are rebuilt by Windows from the ones that have one.</summary>
    private sealed partial class SavedClipboard : IDisposable
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 2;
        private static readonly nint MessageOnly = -3;

        private readonly List<(uint Format, byte[] Data)> _formats = [];

        public unsafe SavedClipboard()
        {
            OpenWithRetry(0);
            try
            {
                for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
                {
                    var handle = GetClipboardData(format);
                    var size = handle == 0 ? 0 : (int)GlobalSize(handle);
                    var source = size == 0 ? null : (byte*)GlobalLock(handle);
                    if (source != null)
                    {
                        _formats.Add((format, new ReadOnlySpan<byte>(source, size).ToArray()));
                        GlobalUnlock(handle);
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        public unsafe void Dispose()
        {
            // SetClipboardData needs an owner window.
            var owner = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, MessageOnly, 0, 0, 0);
            try
            {
                OpenWithRetry(owner);
                try
                {
                    EmptyClipboard();
                    foreach (var (format, data) in _formats)
                    {
                        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
                        data.CopyTo(new Span<byte>((void*)GlobalLock(memory), data.Length));
                        GlobalUnlock(memory);
                        SetClipboardData(format, memory);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                DestroyWindow(owner);
            }
        }

        /// <summary>Opens the clipboard the way another program does: with a window. Opened without
        /// one it does not keep other processes out.</summary>
        public static nint Hold()
        {
            var holder = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, MessageOnly, 0, 0, 0);
            OpenWithRetry(holder);
            return holder;
        }

        public static void Release(nint holder)
        {
            CloseClipboard();
            DestroyWindow(holder);
        }

        public static unsafe string ReadText()
        {
            OpenWithRetry(0);
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                var text = handle == 0 ? null : (char*)GlobalLock(handle);
                if (text == null)
                {
                    return "";
                }

                var result = new string(text);
                GlobalUnlock(handle);
                return result;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static void OpenWithRetry(nint owner) => Wait.Until(() => OpenClipboard(owner), "the clipboard");

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenClipboard(nint owner);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseClipboard();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EmptyClipboard();

        [LibraryImport("user32.dll")]
        private static partial uint EnumClipboardFormats(uint format);

        [LibraryImport("user32.dll")]
        private static partial nint GetClipboardData(uint format);

        [LibraryImport("user32.dll")]
        private static partial nint SetClipboardData(uint format, nint memory);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowEx(
            uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint window);

        [LibraryImport("kernel32.dll")]
        private static partial nint GlobalAlloc(uint flags, nuint bytes);

        [LibraryImport("kernel32.dll")]
        private static partial nuint GlobalSize(nint memory);

        [LibraryImport("kernel32.dll")]
        private static partial nint GlobalLock(nint memory);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalUnlock(nint memory);
    }
}
