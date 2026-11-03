using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NotepadClassic.App.Shell;
using NotepadClassic.Core.Autosave;
using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Commands;
using NotepadClassic.Core.Documents;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Features;
using NotepadClassic.Core.IO;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Printing;
using NotepadClassic.Core.Search;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Settings;
using NotepadClassic.Core.StatusBar;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Autosave;
using NotepadClassic.Platform.IO;
using NotepadClassic.Platform.Printing;
using NotepadClassic.Platform.Services;
using NotepadClassic.Platform.Text;

namespace NotepadClassic.App;

/// <summary>Composition root: services, feature modules, the main window and its wiring.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly CaretWatcher _caret;

    private AppHost(
        ServiceProvider services, MainForm window, CommandRegistry registry, CommandContext context, MenuBuilder menus, KeyDispatcher keys, CaretWatcher caret,
        AutosaveService autosave)
    {
        Autosave = autosave;
        _services = services;
        Window = window;
        Menus = menus;
        Registry = registry;
        Context = context;
        Keys = keys;
        _caret = caret;
    }

    public MainForm Window { get; }

    public CommandRegistry Registry { get; }

    public CommandContext Context { get; }

    public KeyDispatcher Keys { get; }

    public MenuBuilder Menus { get; }

    public AutosaveService Autosave { get; }

    public bool CaretHookInstalled => _caret.IsInstalled;

    /// <summary>The modeless Find/Replace dialog (tests drive it).</summary>
    public Win32FindReplaceDialog FindDialog => _services.GetRequiredService<Win32FindReplaceDialog>();

    public NotepadSettings Settings => _services.GetRequiredService<NotepadSettings>();

    /// <summary>
    /// Step 18 of <c>InitApplication</c>, after the window is shown: the command line (without the
    /// program name) is parsed and run. <see cref="StartupResult.Exit"/> means the program ends
    /// before the message loop.
    /// </summary>
    public StartupResult RunCommandLine(string arguments, bool untrustedSource)
    {
        var host = new ShellStartupHost(this);
        var coordinator = new StartupCoordinator(
            Context.Documents, _services.GetRequiredService<IFileSystem>(), _services.GetRequiredService<IPathService>(),
            Context.Dialogs, Context.Strings, host);
        var result = coordinator.Run(CommandLineParser.Parse(arguments, untrustedSource));
        // After a restore, so the entry being restored is gone first.
        Autosave.CleanOrphans();
        return result;
    }

    /// <summary>The shell side of the startup sequence.</summary>
    private sealed class ShellStartupHost(AppHost app) : IStartupHost
    {
        public void EnterSetupMode()
        {
            app.Window.EnterSetupMode();
            app.Keys.SetupMode = true;
        }

        public void RequestClose() => app.Window.RequestClose();

        public void Print(PrintRequest request) =>
            app.Context.Printing.Print(request.PrinterName is null ? PrinterChoice.Default : PrinterChoice.Named, request.PrinterName);

        public void Restore(string restartId) => app.Autosave.Restore(restartId);
    }

    /// <summary>Builds the app. <paramref name="configure"/> replaces services (tests use fakes).</summary>
    public static AppHost Create(Action<IServiceCollection>? configure = null, IEnumerable<IFeatureModule>? modules = null)
    {
        var featureModules = (modules ?? BuiltInModules.All).ToList();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalizedStrings>(ResxStrings.Instance);
        services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());
        services.AddSingleton<IRecentFilesStore, InMemoryRecentFilesStore>();
        services.AddSingleton(ReadOptions());
        services.AddSingleton<ICodePageConverter, Win32CodePageConverter>();
        services.AddSingleton<IUnicodeHeuristic, Win32UnicodeHeuristic>();
        services.AddSingleton<EncodingDetector>();
        services.AddSingleton<TextDecoder>();
        services.AddSingleton<DocumentLoader>();
        services.AddSingleton<TextEncoder>();
        services.AddSingleton<IFileSystem, Win32FileSystem>();
        services.AddSingleton<ITextBufferFactory>(LocalTextBufferFactory.Instance);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDateTimeFormatter, Win32DateTimeFormatter>();
        services.AddSingleton<IClipboard, Win32Clipboard>();
        services.AddSingleton<IShell, Win32Shell>();
        services.AddSingleton<IFileTitleProvider, Win32FileTitleProvider>();
        services.AddSingleton<ISystemErrorMessages, Win32SystemErrorMessages>();
        services.AddSingleton<IFileAccessPolicy, AllowAllFileAccessPolicy>();
        services.AddSingleton<ITextSearcher, NlsTextSearcher>();
        services.AddSingleton<IDigitFolder, Win32DigitFolder>();
        services.AddSingleton<IPathService, Win32PathService>();
        services.AddSingleton<IAutosaveStore>(_ => Win32AutosaveStore.FromEnvironment());
        services.AddSingleton<IRestartRegistration, Win32RestartRegistration>();
        services.AddSingleton<IProcessDirectory, Win32ProcessDirectory>();
        services.AddSingleton(sp => new MainForm(sp.GetRequiredService<NotepadSettings>(), sp.GetRequiredService<ILocalizedStrings>()));
        services.AddSingleton<IDialogService>(sp =>
        {
            var window = sp.GetRequiredService<MainForm>();
            var converter = sp.GetRequiredService<ICodePageConverter>();
            return new Win32DialogService(
                () => window.Handle, sp.GetRequiredService<ILocalizedStrings>(), () => converter.AnsiCodePage, () => window.MenuBarHeight);
        });
        services.AddSingleton(sp =>
        {
            var window = sp.GetRequiredService<MainForm>();
            var strings = sp.GetRequiredService<ILocalizedStrings>();
            return new Win32FindReplaceDialog(() => window.Handle, () => strings.GetString(StringKeys.AppName), () => window.MenuBarHeight);
        });
        services.AddSingleton<IFindReplaceDialog>(sp => sp.GetRequiredService<Win32FindReplaceDialog>());
        services.AddSingleton<IPrinterService>(sp =>
        {
            var window = sp.GetRequiredService<MainForm>();
            return new Win32PrinterService(
                () => window.Handle, sp.GetRequiredService<ILocalizedStrings>(), sp.GetRequiredService<IShell>(),
                sp.GetRequiredService<AppOptions>().PageSetupHelpUrl, () => window.MenuBarHeight);
        });
        foreach (var module in featureModules)
        {
            module.ConfigureServices(services);
        }

        configure?.Invoke(services);
        StartupTrace.Mark("Services registered");
        var provider = services.BuildServiceProvider();
        StartupTrace.Mark("Provider built");
        try
        {
            return Wire(provider, featureModules);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private static AppHost Wire(ServiceProvider sp, IEnumerable<IFeatureModule> modules)
    {
        var settings = sp.GetRequiredService<NotepadSettings>();
        StartupTrace.Mark("Settings loaded");
        var strings = sp.GetRequiredService<ILocalizedStrings>();
        var window = sp.GetRequiredService<MainForm>();
        StartupTrace.Mark("Main window created");
        var editor = window.Editor;
        var state = new DocumentState(settings.DefaultEncoding);
        var statusBar = new StatusBarPresenter(window, editor, strings, () => state.CurrentEncoding);
        var timeDate = new TimeDateInserter(sp.GetRequiredService<IClock>(), sp.GetRequiredService<IDateTimeFormatter>());
        var dialogs = sp.GetRequiredService<IDialogService>();
        var shell = sp.GetRequiredService<IShell>();
        var recentFiles = new RecentFilesService(sp.GetRequiredService<IRecentFilesStore>());
        var documents = new DocumentService(
            new DocumentServiceDependencies(
                editor, window, sp.GetRequiredService<IFileSystem>(), sp.GetRequiredService<ITextBufferFactory>(),
                sp.GetRequiredService<EncodingDetector>(), sp.GetRequiredService<DocumentLoader>(), sp.GetRequiredService<TextEncoder>(),
                dialogs, strings, sp.GetRequiredService<IFileTitleProvider>(), sp.GetRequiredService<ISystemErrorMessages>(),
                sp.GetRequiredService<IFileAccessPolicy>(), shell, timeDate, statusBar, recentFiles),
            state);
        var findDialog = sp.GetRequiredService<IFindReplaceDialog>();
        var search = new SearchService(new(
            editor, settings, sp.GetRequiredService<ITextSearcher>(), findDialog, dialogs, statusBar, strings, window));
        var goTo = new GoToService(editor, sp.GetRequiredService<IDigitFolder>());
        var printing = new PrintService(new(
            editor, documents, window, settings, sp.GetRequiredService<IPrinterService>(), dialogs, strings, sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDateTimeFormatter>(), sp.GetRequiredService<IFileTitleProvider>(), sp.GetRequiredService<ISystemErrorMessages>()));
        var context = new CommandContext(
            editor, documents, window, dialogs, sp.GetRequiredService<IClipboard>(), shell, strings, settings, statusBar, timeDate,
            sp.GetRequiredService<AppOptions>(), search, goTo, printing, recentFiles);
        var registry = BuiltInModules.CreateRegistry(modules);
        StartupTrace.Mark("Services resolved");

        var menus = new MenuBuilder(registry, strings, () => context);
        var menu = menus.Build();
        StartupTrace.Mark("Menu built");
        var keys = new KeyDispatcher(registry, () => context, () => window.Handle, KeyDispatcher.CurrentModifiers);
        if (findDialog is Win32FindReplaceDialog native)
        {
            keys.DialogMessage = m => native.IsDialogMessage(m.HWnd, m.Msg, m.WParam, m.LParam);
            window.RegisteredMessage = native.HandleOwnerMessage;
        }

        window.BrowserSearch = () => registry.Find(CommandId.Find)!.Execute(context);
        menu.MenuActivate += (_, _) => keys.MenuActive = true;
        menu.MenuDeactivate += (_, _) => keys.MenuActive = false;
        window.SetMenu(menu);
        StartupTrace.Mark("Menu set and bars measured");

        // In memory only: the font group is written when the user changes the font.
        settings.Font = Editor.EditorSurface.WithInstalledCharSet(settings.Font, editor.Dpi);
        editor.SetFont(settings.Font);
        StartupTrace.Mark("Editor font set");
        editor.ZoomMayHaveChanged += (_, _) => statusBar.UpdateZoom();
        window.ZoomMayHaveChanged += (_, _) => statusBar.UpdateZoom();
        settings.WindowBaseline = WindowPlacement.Apply(window.Handle, settings.WindowPosition);
        window.BeforeCloseQuery = () => sp.GetRequiredService<ISettingsStore>().Save(settings, window.NormalPositionUnaware());
        window.FileDropped = documents.OpenDropped;
        var autosave = new AutosaveService(
            documents, editor, sp.GetRequiredService<IAutosaveStore>(), sp.GetRequiredService<IProcessDirectory>(), dialogs, strings,
            AutosaveSession.FormatId(Guid.NewGuid()));
        _ = autosave.Register(sp.GetRequiredService<IRestartRegistration>());
        window.CloseQuery = autosave.ConfirmClose;
        window.QueryEndSession = autosave.QueryEndSession;
        window.EndSession = autosave.EndSession;
        StartupTrace.Mark("Placement, autosave registered");
        documents.UpdateTitle();
        statusBar.UpdateAll();
        StartupTrace.Mark("Title and status bar set");
        // The hook lives until the host is disposed: WinForms disposes the Edit control when the main
        // window closes while the message loop still delivers queued location events, so those
        // find no control to ask and are dropped.
        var caret = new CaretWatcher(() =>
        {
            if (editor.Host is { IsDisposed: false, IsHandleCreated: true })
            {
                statusBar.UpdateLineColumn(force: false);
            }
        });
        return new AppHost(sp, window, registry, context, menus, keys, caret, autosave);
    }

    /// <summary>Help and Feedback links from the assembly metadata of the app project.</summary>
    private static AppOptions ReadOptions()
    {
        var metadata = typeof(AppHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        return new AppOptions(metadata.GetValueOrDefault("HelpUrl"), metadata.GetValueOrDefault("FeedbackUrl"), metadata.GetValueOrDefault("PageSetupHelpUrl"));
    }

    public void Dispose()
    {
        _caret.Dispose();
        Window.Dispose();
        _services.Dispose();
    }
}
