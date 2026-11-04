using System.Runtime.InteropServices;
using NotepadNet.App.Interop;
using NotepadNet.App.Shell;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Settings;

namespace NotepadNet.App.Tests;

/// <summary>View > Transparency on the real window: its layered style, alpha and the wheel chord.</summary>
public sealed partial class OpacityShellTests
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    private static bool IsLayered(TestApp app) =>
        ((long)NativeMethods.GetWindowLongPtr(app.Host.Window.Handle, NativeMethods.GWL_EXSTYLE) & WS_EX_LAYERED) != 0;

    private static byte Alpha(TestApp app)
    {
        GetLayeredWindowAttributes(app.Host.Window.Handle, out _, out var alpha, out var flags).Should().BeTrue();
        (flags & LWA_ALPHA).Should().Be(LWA_ALPHA);
        return alpha;
    }

    private static Message Wheel(nint hwnd, int keys, int delta) =>
        Message.Create(hwnd, NativeMethods.WM_MOUSEWHEEL, (nint)(((delta & 0xFFFF) << 16) | keys), 0);

    private const int CtrlShift = NativeMethods.MK_CONTROL | NativeMethods.MK_SHIFT;

    private static KeyDispatcher Dispatcher(TestApp app) =>
        new(app.Host.Registry, () => app.Host.Context, () => app.Host.Window.Handle, () => KeyModifiers.None);

    [Fact]
    [Trait("Doc", "parity.md#trong-suot")]
    public void An_opaque_window_is_not_layered_and_the_menu_commands_change_its_alpha() => Sta.Run(() =>
    {
        using var app = new TestApp();
        IsLayered(app).Should().BeFalse("people who never use the feature pay nothing for it");

        app.Host.Registry.TryExecute(CommandId.MoreTransparent, app.Host.Context).Should().BeTrue();
        IsLayered(app).Should().BeTrue();
        Alpha(app).Should().Be(229, "90% of 255");

        app.Host.Registry.TryExecute(CommandId.Opaque, app.Host.Context).Should().BeTrue();
        IsLayered(app).Should().BeFalse();
    });

    [Fact]
    [Trait("Doc", "parity.md#trong-suot")]
    public void The_saved_opacity_applies_when_the_window_is_created() => Sta.Run(() =>
    {
        using var app = new TestApp(store: new SavedOpacityStore(50));

        IsLayered(app).Should().BeTrue();
        Alpha(app).Should().Be(127);
    });

    [Fact]
    [Trait("Doc", "parity.md#trong-suot")]
    public void Ctrl_shift_wheel_changes_opacity_and_is_kept_from_the_edit_control() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var keys = Dispatcher(app);
        var edit = app.Editor.Host.Handle;

        var down = Wheel(edit, CtrlShift, -NativeMethods.WHEEL_DELTA);
        keys.PreFilterMessage(ref down).Should().BeTrue("the Edit control would zoom on the Ctrl");
        app.Host.Settings.OpacityPercent.Should().Be(90);

        var up = Wheel(app.Host.Window.Handle, CtrlShift, NativeMethods.WHEEL_DELTA);
        keys.PreFilterMessage(ref up).Should().BeTrue();
        app.Host.Settings.OpacityPercent.Should().Be(100);

        keys.PreFilterMessage(ref up).Should().BeTrue("the chord stays consumed at the limit");
        app.Host.Settings.OpacityPercent.Should().Be(100);
    });

    [Theory]
    [Trait("Doc", "parity.md#trong-suot")]
    [InlineData(0)]
    [InlineData(NativeMethods.MK_CONTROL)]
    [InlineData(NativeMethods.MK_SHIFT)]
    public void Other_wheel_chords_stay_with_the_edit_control(int chord) => Sta.Run(() =>
    {
        using var app = new TestApp();
        var message = Wheel(app.Editor.Host.Handle, chord, -NativeMethods.WHEEL_DELTA);

        Dispatcher(app).PreFilterMessage(ref message).Should().BeFalse();
        app.Host.Settings.OpacityPercent.Should().Be(100);
    });

    [Fact]
    [Trait("Doc", "parity.md#trong-suot")]
    public void The_wheel_chord_ignores_other_windows_and_an_open_menu() => Sta.Run(() =>
    {
        using var app = new TestApp();
        using var other = new Form();
        var keys = Dispatcher(app);

        var foreign = Wheel(other.Handle, CtrlShift, -NativeMethods.WHEEL_DELTA);
        keys.PreFilterMessage(ref foreign).Should().BeFalse();

        keys.MenuActive = true;
        var inMenu = Wheel(app.Host.Window.Handle, CtrlShift, -NativeMethods.WHEEL_DELTA);
        keys.PreFilterMessage(ref inMenu).Should().BeFalse();

        app.Host.Settings.OpacityPercent.Should().Be(100);
    });

    [Fact]
    [Trait("Doc", "parity.md#trong-suot")]
    public void A_high_resolution_wheel_steps_once_per_notch_and_a_turn_back_starts_over() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var keys = Dispatcher(app);
        var handle = app.Host.Window.Handle;
        void Send(int delta)
        {
            var message = Wheel(handle, CtrlShift, delta);
            keys.PreFilterMessage(ref message).Should().BeTrue();
        }

        Send(-60);
        app.Host.Settings.OpacityPercent.Should().Be(100);
        Send(-60);
        app.Host.Settings.OpacityPercent.Should().Be(90);

        Send(-60);
        Send(60);
        Send(-60);
        app.Host.Settings.OpacityPercent.Should().Be(90, "the partial notches went in opposite directions");
    });

    private sealed class SavedOpacityStore(int percent) : ISettingsStore
    {
        public NotepadSettings Load() => new() { OpacityPercent = percent };

        public void Save(NotepadSettings settings, WindowRect? window)
        {
        }
    }
}
