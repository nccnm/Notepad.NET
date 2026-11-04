using NotepadNet.App.Interop;
using NotepadNet.Core.Commands;
using NotepadNet.Core.Settings;

namespace NotepadNet.App.Tests;

/// <summary>View > Always on Top on the real window: its <c>WS_EX_TOPMOST</c> style, also next to transparency.</summary>
public sealed class AlwaysOnTopShellTests
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_LAYERED = 0x00080000;

    private static long ExStyle(TestApp app) =>
        (long)NativeMethods.GetWindowLongPtr(app.Host.Window.Handle, NativeMethods.GWL_EXSTYLE);

    private static bool IsTopmost(TestApp app) => (ExStyle(app) & WS_EX_TOPMOST) != 0;

    private static bool IsLayered(TestApp app) => (ExStyle(app) & WS_EX_LAYERED) != 0;

    /// <summary>A window takes the style once it is on screen, so these tests show it.</summary>
    private static TestApp Shown(TestApp app)
    {
        app.Host.Window.Show();
        return app;
    }

    private static void Run(TestApp app, CommandId id) =>
        app.Host.Registry.TryExecute(id, app.Host.Context).Should().BeTrue();

    [Fact]
    [Trait("Doc", "parity.md#luon-o-tren")]
    public void A_new_window_is_not_topmost_and_the_command_toggles_the_style() => Sta.Run(() =>
    {
        using var app = Shown(new TestApp());
        IsTopmost(app).Should().BeFalse("the default behaves like the original");

        Run(app, CommandId.AlwaysOnTop);
        IsTopmost(app).Should().BeTrue();

        Run(app, CommandId.AlwaysOnTop);
        IsTopmost(app).Should().BeFalse();
    });

    [Fact]
    [Trait("Doc", "parity.md#luon-o-tren")]
    public void The_saved_choice_applies_when_the_window_is_created() => Sta.Run(() =>
    {
        using var app = Shown(new TestApp(store: new SavedStore(new() { AlwaysOnTop = true })));

        IsTopmost(app).Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#luon-o-tren")]
    public void A_choice_made_before_the_window_shows_applies_when_it_shows() => Sta.Run(() =>
    {
        using var app = new TestApp();
        Run(app, CommandId.AlwaysOnTop);
        app.Host.Window.TopMost.Should().BeFalse("nothing is asked of a window that is not on screen");

        app.Host.Window.Show();

        IsTopmost(app).Should().BeTrue();
    });

    [Fact]
    [Trait("Doc", "parity.md#luon-o-tren")]
    public void Changing_the_opacity_keeps_the_window_topmost_and_the_other_way_round() => Sta.Run(() =>
    {
        using var app = Shown(new TestApp());

        Run(app, CommandId.AlwaysOnTop);
        Run(app, CommandId.MoreTransparent);
        IsTopmost(app).Should().BeTrue("the layered style change must not drop it");
        IsLayered(app).Should().BeTrue();

        Run(app, CommandId.Opaque);
        IsTopmost(app).Should().BeTrue();
        IsLayered(app).Should().BeFalse();

        Run(app, CommandId.MoreTransparent);
        Run(app, CommandId.AlwaysOnTop);
        IsTopmost(app).Should().BeFalse();
        IsLayered(app).Should().BeTrue("turning it off must not touch the alpha");
    });

    [Fact]
    [Trait("Doc", "parity.md#luon-o-tren")]
    public void Word_wrap_replacing_the_edit_control_keeps_the_window_topmost() => Sta.Run(() =>
    {
        using var app = Shown(new TestApp());
        Run(app, CommandId.AlwaysOnTop);

        Run(app, CommandId.WordWrap);

        IsTopmost(app).Should().BeTrue();
    });

    private sealed class SavedStore(NotepadSettings settings) : ISettingsStore
    {
        public NotepadSettings Load() => settings;

        public void Save(NotepadSettings settings, WindowRect? window)
        {
        }
    }
}
