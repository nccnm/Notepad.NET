using System.Runtime.InteropServices;
using NotepadClassic.App.Interop;
using NotepadClassic.App.Shell;
using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Features;
using NotepadClassic.Core.Settings;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Services;

namespace NotepadClassic.App.Tests;

/// <summary>
/// Phase d in the real shell: command line, <c>/.SETUP</c> and <c>SlipUpAcc</c>, drag and drop,
/// saved window position, settings saved on <c>WM_CLOSE</c>. Settings stay in memory.
/// </summary>
public sealed unsafe partial class StartupShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-startup-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static Message KeyDown(nint hwnd, ushort key) => Message.Create(hwnd, NativeMethods.WM_KEYDOWN, key, 0);

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Command_line_opens_a_file_with_the_forced_encoding() => Sta.Run(() =>
    {
        using var app = new TestApp();
        File.WriteAllBytes(PathOf("w.txt"), [0x48, 0x00, 0x69, 0x00]);

        app.Host.RunCommandLine($"/W \"{PathOf("w.txt")}\"", untrustedSource: false).Should().Be(StartupResult.Run);

        app.Text.Should().Be("Hi");
        app.Host.Context.Documents.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf16LE);
        app.Host.Window.Text.Should().Be(new Win32FileTitleProvider().GetTitle(PathOf("w.txt")) + " - Notepad Classic");
    });

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void A_missing_name_gets_txt_and_cancel_in_the_create_question_ends_the_program() => Sta.Run(() =>
    {
        using var app = new TestApp();

        // FakeDialogs answers OK to every message box, which is neither Yes nor Cancel: nothing created.
        app.Host.RunCommandLine(PathOf("notes"), untrustedSource: false).Should().Be(StartupResult.Run);

        app.Dialogs.Messages.Should().ContainSingle().Which.Should().Contain(PathOf("notes.txt"));
        File.Exists(PathOf("notes.txt")).Should().BeFalse();
    });

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Setup_mode_sets_the_window_style_and_the_slip_up_keys() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var window = app.Host.Window;
        var closes = 0;
        window.CloseQuery = () =>
        {
            closes++;
            return false;
        };

        app.Host.RunCommandLine("/.SETUP", untrustedSource: false);

        window.SetupMode.Should().BeTrue();
        app.Host.Keys.SetupMode.Should().BeTrue();
        var style = (int)NativeMethods.GetWindowLongPtr(window.Handle, NativeMethods.GWL_STYLE);
        (style & MainForm.WS_MINIMIZEBOX).Should().Be(0);
        (style & 0x00CC0000).Should().Be(0x00CC0000, "caption, system menu and sizing frame stay");

        var keys = new KeyDispatcher(app.Host.Registry, () => app.Host.Context, () => window.Handle, () => KeyModifiers.Control) { SetupMode = true };
        var plain = new KeyDispatcher(app.Host.Registry, () => app.Host.Context, () => window.Handle, () => KeyModifiers.None) { SetupMode = true };
        app.Editor.ReplaceSelection("typed", canUndo: true);

        var undo = KeyDown(app.Editor.Host.Handle, 'Z');
        keys.PreFilterMessage(ref undo).Should().BeTrue();
        app.Text.Should().BeEmpty("Ctrl+Z is still Undo: MainAcc comes first in SlipUpAcc");
        var copy = KeyDown(app.Editor.Host.Handle, 'C');
        keys.PreFilterMessage(ref copy).Should().BeTrue();
        Application.DoEvents();
        closes.Should().Be(0);

        var ctrlD = KeyDown(app.Editor.Host.Handle, 'D');
        keys.PreFilterMessage(ref ctrlD).Should().BeTrue();
        Application.DoEvents();
        closes.Should().Be(1);

        var esc = KeyDown(app.Editor.Host.Handle, VirtualKeys.Escape);
        plain.PreFilterMessage(ref esc).Should().BeTrue();
        Application.DoEvents();
        closes.Should().Be(2);

        NativeMethods.SendMessage(window.Handle, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, 0);
        NativeMethods.IsIconic(window.Handle).Should().BeFalse("minimize is swallowed in /.SETUP mode");
    });

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Escape_and_ctrl_d_do_nothing_outside_setup_mode() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var keys = new KeyDispatcher(app.Host.Registry, () => app.Host.Context, () => app.Host.Window.Handle, () => KeyModifiers.Control);
        var ctrlD = KeyDown(app.Editor.Host.Handle, 'D');

        keys.PreFilterMessage(ref ctrlD).Should().BeFalse();
        app.Host.Registry.KeyBindings.Where(k => k.Tier == KeyTier.SetupOnly).Select(k => (k.Chord.VirtualKey, k.Command))
            .Should().BeEquivalentTo(new[]
            {
                (VirtualKeys.Escape, CommandId.Exit), ((ushort)'C', CommandId.Exit), ((ushort)'D', CommandId.Exit), ((ushort)'Z', CommandId.Exit),
            });
    });

    [Theory]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    [InlineData(0xF020, true)]
    [InlineData(0xF022, true)]
    [InlineData(0xF040, true)]
    [InlineData(0xF050, true)]
    [InlineData(0xF030, false)]
    [InlineData(0xF060, false)]
    [InlineData(0xF120, false)]
    public void Setup_mode_swallows_minimize_and_window_cycling(int command, bool swallowed) =>
        MainForm.IsSetupBlockedCommand(command).Should().Be(swallowed);

    // ---- Drag and drop ----

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    /// <summary>An <c>HDROP</c>: <c>DROPFILES</c> (20 bytes, wide) and a double-NUL list of names.</summary>
    private static nint CreateDrop(params string[] files)
    {
        var names = string.Concat(files.Select(f => f + "\0")) + "\0";
        var memory = GlobalAlloc(0x42, (nuint)(20 + (names.Length * 2)));
        var p = (byte*)GlobalLock(memory);
        *(int*)p = 20;
        *(int*)(p + 16) = 1;
        fixed (char* source = names)
        {
            Buffer.MemoryCopy(source, p + 20, names.Length * 2, names.Length * 2);
        }

        GlobalUnlock(memory);
        return memory;
    }

    [Fact]
    [Trait("Doc", "parity.md#keo-tha")]
    public void Dropping_files_opens_only_the_first_with_detection() => Sta.Run(() =>
    {
        using var app = new TestApp();
        File.WriteAllBytes(PathOf("first.txt"), [0xEF, 0xBB, 0xBF, 0x31]);
        File.WriteAllText(PathOf("second.txt"), "2");
        app.Host.Context.Documents.State.CurrentEncoding = TextEncodingKind.Ansi;

        NativeMethods.SendMessage(app.Host.Window.Handle, NativeMethods.WM_DROPFILES, CreateDrop(PathOf("first.txt"), PathOf("second.txt")), 0);

        app.Text.Should().Be("1");
        app.Host.Context.Documents.State.Path.Should().Be(PathOf("first.txt"));
        app.Host.Context.Documents.State.CurrentEncoding.Should().Be(TextEncodingKind.Utf8Bom);
    });

    [Fact]
    [Trait("Doc", "parity.md#keo-tha")]
    public void Dropping_a_missing_file_shows_the_file_error() => Sta.Run(() =>
    {
        using var app = new TestApp();

        NativeMethods.SendMessage(app.Host.Window.Handle, NativeMethods.WM_DROPFILES, CreateDrop(PathOf("gone.txt")), 0);

        app.Dialogs.Messages.Should().ContainSingle();
        app.Host.Context.Documents.State.IsUntitled.Should().BeTrue();
    });

    // ---- Window position and settings on close ----

    private sealed class PresetStore(NotepadSettings settings) : ISettingsStore
    {
        public List<(NotepadSettings Settings, WindowRect? Window)> Saves { get; } = [];

        public NotepadSettings Load() => settings;

        public void Save(NotepadSettings s, WindowRect? window) => Saves.Add((s, window));
    }

    [Fact]
    [Trait("Doc", "parity.md#cua-so-va-edit-control")]
    public void A_saved_position_is_applied_dpi_unaware_and_unchanged_it_is_not_saved_again() => Sta.Run(() =>
    {
        var store = new PresetStore(new NotepadSettings { WindowPosition = new WindowPosition(40, 50, 700, 500) });
        using var app = new TestApp(store: store);
        var window = app.Host.Window;

        app.Host.Settings.WindowBaseline.Should().Be(new WindowRect(40, 50, 740, 550));
        window.NormalPositionUnaware().Should().Be(new WindowRect(40, 50, 740, 550));
        var dpi = (int)NativeMethods.GetDpiForWindow(window.Handle);
        NativeMethods.GetWindowRect(window.Handle, out var physical);
        (physical.Right - physical.Left).Should().BeCloseTo(Core.Editing.Win32Math.Scale(700, dpi), 1);

        window.Close();

        store.Saves.Should().ContainSingle().Which.Window.Should().Be(app.Host.Settings.WindowBaseline);
    });

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Default_position_has_an_empty_baseline_and_settings_are_saved_before_the_question() => Sta.Run(() =>
    {
        var store = new PresetStore(new NotepadSettings());
        using var app = new TestApp(store: store);
        app.Editor.ReplaceSelection("unsaved", canUndo: true);
        File.WriteAllText(app.AutosaveFolder, "a file where the folder should be");
        app.Dialogs.SaveChanges.Enqueue(Core.Services.SaveChangesChoice.Cancel);

        app.Host.Settings.WindowBaseline.Should().Be(default(WindowRect));
        app.Host.Window.Close();
        File.Delete(app.AutosaveFolder);

        store.Saves.Should().ContainSingle().Which.Window.Should().NotBeNull().And.NotBe(default(WindowRect));
        app.Host.Window.IsDisposed.Should().BeFalse();
    });
}
