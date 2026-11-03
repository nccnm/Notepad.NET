using Microsoft.Extensions.DependencyInjection;
using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Documents;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Settings;
using NotepadClassic.Platform.Services;
using NotepadClassic.Platform.Settings;

namespace NotepadClassic.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        StartupTrace.Mark("Main");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        StartupTrace.Mark("Application configured");

        using var registry = Win32SettingsRegistry.FromEnvironment();
        StartupTrace.Mark("Registry opened");
        using var host = AppHost.Create(services =>
        {
            services.AddSingleton<ISettingsStore>(new RegistrySettingsStore(
                registry,
                new SettingsDefaults(
                    Win32StartupInfo.UsesMetricMeasure(),
                    ResxStrings.Instance.GetString(StringKeys.DefaultHeader),
                    ResxStrings.Instance.GetString(StringKeys.DefaultTrailer))));
            services.AddSingleton<IRecentFilesStore>(new RegistryRecentFilesStore(registry));
        });
        StartupTrace.Mark("Host created");
        Application.AddMessageFilter(host.Keys);
        // The window shows before any document loads, as InitApplication does.
        host.Window.Show();
        StartupTrace.Mark("Window shown");
        host.Window.Editor.Host.Focus();
        var arguments = CommandLineParser.StripProgramName(Win32StartupInfo.CommandLine());
        if (host.RunCommandLine(arguments, Win32StartupInfo.IsUntrustedSource()) == StartupResult.Exit)
        {
            return;
        }

        StartupTrace.Mark("Command line done");
        if (StartupTrace.Enabled)
        {
            EventHandler? idle = null;
            idle = (_, _) =>
            {
                Application.Idle -= idle;
                StartupTrace.Flush();
            };
            Application.Idle += idle;
        }

        Application.Run(host.Window);
    }
}
