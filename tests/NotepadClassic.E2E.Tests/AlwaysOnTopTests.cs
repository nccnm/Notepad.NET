using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace NotepadClassic.E2E.Tests;

/// <summary>View > Always on Top through the real exe: the <c>WS_EX_TOPMOST</c> style, the real
/// z-order against another app and the app's own dialogs, and the mix with transparency.</summary>
[Collection(DesktopTests.Name)]
public sealed class AlwaysOnTopTests
{
    private static void ToggleAlwaysOnTop(AppSession app)
    {
        Ui.MenuItem(app.Window, "View").Expand();
        Ui.MenuItem(app.Window, "Always on Top").Invoke();
    }

    private static void MoreTransparent(AppSession app)
    {
        Ui.MenuItem(app.Window, "View").Expand();
        Ui.MenuItem(app.Window, "Transparency").Expand();
        Ui.MenuItem(app.Window, "More Transparent").Invoke();
    }

    private static bool MenuShowsChecked(AppSession app)
    {
        var view = Ui.MenuItem(app.Window, "View");
        view.Expand();
        var isChecked = Ui.IsChecked(Ui.MenuItem(app.Window, "Always on Top"));
        view.Collapse();
        return isChecked;
    }

    private static void CloseAndWait(AppSession app) => app.CloseWithKeyboard();

    private static void UntilTopmost(AppSession app, bool expected, string what) =>
        Wait.Until(() => AppSession.IsTopmost(app.Window) == expected, what);

    /// <summary>Opens a dialog the way a user does; returns its title.</summary>
    private static string OpenDialog(AppSession app, string dialog)
    {
        switch (dialog)
        {
            case "Find":
                app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                return "Find";
            case "Replace":
                app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
                return "Replace";
            case "Go To":
                app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
                return "Go To Line";
            case "Open":
                app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_O);
                return "Open";
            case "Save As":
                app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);
                return "Save As";
            case "Font":
                // ChooseFont is modal: a UIA Invoke would wait for it to close.
                app.Press(VirtualKeyShort.ALT, VirtualKeyShort.KEY_O);
                Keyboard.Type(VirtualKeyShort.KEY_F);
                return "Font";
            default:
                throw new ArgumentOutOfRangeException(nameof(dialog));
        }
    }

    [Fact]
    public void Always_on_top_from_the_menu_sets_the_style_and_survives_restarts()
    {
        string folder;
        using (var app = AppSession.StartWithFile("t.txt", "text", out _, keepFolder: true))
        {
            folder = app.Folder;
            Wait.Until(() => app.Text == "text", "the file text");
            AppSession.IsTopmost(app.Window).Should().BeFalse("the default behaves like the original");
            MenuShowsChecked(app).Should().BeFalse();

            ToggleAlwaysOnTop(app);
            UntilTopmost(app, true, "WS_EX_TOPMOST after turning it on");
            MenuShowsChecked(app).Should().BeTrue();
            CloseAndWait(app);
        }

        try
        {
            using (var second = AppSession.StartIn(folder, ""))
            {
                UntilTopmost(second, true, "WS_EX_TOPMOST after a restart");
                MenuShowsChecked(second).Should().BeTrue();

                ToggleAlwaysOnTop(second);
                UntilTopmost(second, false, "no WS_EX_TOPMOST after turning it off");
                MenuShowsChecked(second).Should().BeFalse();
                CloseAndWait(second);
            }

            using var third = AppSession.StartIn(folder, "");
            Wait.Until(() => third.Title.Length > 0, "the title");
            AppSession.IsTopmost(third.Window).Should().BeFalse("turning it off is saved as well");
            MenuShowsChecked(third).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Only_an_always_on_top_window_stays_above_another_active_app()
    {
        using var app = AppSession.Start();
        using var other = AppSession.Start();

        ToggleAlwaysOnTop(app);
        UntilTopmost(app, true, "WS_EX_TOPMOST");
        other.Activate();
        Wait.Until(() => AppSession.IsAbove(app.Window, other.Window), "the window above the active app");

        ToggleAlwaysOnTop(app);
        UntilTopmost(app, false, "no WS_EX_TOPMOST");
        other.Activate();
        Wait.Until(() => AppSession.IsAbove(other.Window, app.Window), "the active app above the window");
    }

    [Theory]
    [InlineData("Find")]
    [InlineData("Replace")]
    [InlineData("Go To")]
    [InlineData("Font")]
    [InlineData("Open")]
    [InlineData("Save As")]
    public void Dialogs_open_above_an_always_on_top_window(string dialog)
    {
        using var app = AppSession.StartWithFile("d.txt", "text", out _);
        Wait.Until(() => app.Text == "text", "the file text");
        ToggleAlwaysOnTop(app);
        UntilTopmost(app, true, "WS_EX_TOPMOST");

        var window = app.WaitForWindow(OpenDialog(app, dialog));

        AppSession.OwnerOf(window).Should().Be(AppSession.Handle(app.Window), "an owned window cannot go behind its owner");
        Wait.Until(() => AppSession.IsAbove(window, app.Window), "the dialog above the main window");
        AppSession.IsTopmost(window).Should().BeTrue("otherwise the owner would cover it next to other apps");

        AppSession.Close(window);
        Wait.Until(() => !AppSession.Exists(window), "the dialog to close");
        AppSession.IsTopmost(app.Window).Should().BeTrue("closing a dialog leaves the choice alone");
    }

    [Fact]
    public void An_open_find_dialog_follows_the_toggle_and_stays_above()
    {
        using var app = AppSession.StartWithFile("d.txt", "text", out _);
        using var other = AppSession.Start();
        Wait.Until(() => app.Text == "text", "the file text");

        var find = app.WaitForWindow(OpenDialog(app, "Find"));
        AppSession.IsTopmost(find).Should().BeFalse();

        ToggleAlwaysOnTop(app);
        UntilTopmost(app, true, "WS_EX_TOPMOST");
        Wait.Until(() => AppSession.IsTopmost(find), "the open dialog to become topmost as well");
        Wait.Until(() => AppSession.IsAbove(find, app.Window), "the dialog above the main window");
        other.Activate();
        Wait.Until(() => AppSession.IsAbove(find, app.Window) && AppSession.IsAbove(app.Window, other.Window), "dialog, window, other app, in that order");

        ToggleAlwaysOnTop(app);
        UntilTopmost(app, false, "no WS_EX_TOPMOST");
        Wait.Until(() => !AppSession.IsTopmost(find), "the open dialog to stop being topmost");
        Wait.Until(() => AppSession.IsAbove(find, app.Window), "the dialog still above the main window");
    }

    [Fact]
    public void Always_on_top_and_transparency_work_together_and_both_survive_a_restart()
    {
        string folder;
        using (var app = AppSession.StartWithFile("t.txt", "text", out _, keepFolder: true))
        {
            folder = app.Folder;
            Wait.Until(() => app.Text == "text", "the file text");

            ToggleAlwaysOnTop(app);
            UntilTopmost(app, true, "WS_EX_TOPMOST");
            MoreTransparent(app);
            Wait.Until(() => app.WindowAlpha == 229, "90% opacity", () => $"{app.WindowAlpha}");
            AppSession.IsTopmost(app.Window).Should().BeTrue("the layered style change must not drop it");

            var find = app.WaitForWindow(OpenDialog(app, "Find"));
            Wait.Until(() => AppSession.IsAbove(find, app.Window), "the dialog above the see-through window");
            AppSession.IsTopmost(find).Should().BeTrue();
            AppSession.AlphaOf(find).Should().BeNull("dialogs stay opaque");
            AppSession.Close(find);
            Wait.Until(() => !AppSession.Exists(find), "the dialog to close");
            CloseAndWait(app);
        }

        try
        {
            using var again = AppSession.StartIn(folder, "");
            UntilTopmost(again, true, "WS_EX_TOPMOST after a restart");
            Wait.Until(() => again.WindowAlpha == 229, "the saved opacity", () => $"{again.WindowAlpha}");

            ToggleAlwaysOnTop(again);
            UntilTopmost(again, false, "no WS_EX_TOPMOST");
            again.WindowAlpha.Should().Be(229, "turning it off must not touch the alpha");

            ToggleAlwaysOnTop(again);
            UntilTopmost(again, true, "WS_EX_TOPMOST again");
            Ui.MenuItem(again.Window, "View").Expand();
            Ui.MenuItem(again.Window, "Transparency").Expand();
            Ui.MenuItem(again.Window, "Opaque").Invoke();
            Wait.Until(() => again.WindowAlpha is null, "the opaque window", () => $"{again.WindowAlpha}");
            AppSession.IsTopmost(again.Window).Should().BeTrue("leaving the layered style must not drop it");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
