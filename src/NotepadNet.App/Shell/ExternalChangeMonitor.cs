using NotepadNet.Core.Documents;
using NotepadNet.Core.IO;

namespace NotepadNet.App.Shell;

/// <summary>
/// Tells <see cref="DocumentService.NotifyIfChangedOutside"/> when to look at the document's file: the
/// watcher reports a write, and a short wait lets an editor that writes in steps finish, so one save
/// is one report. Activating the window looks too, for what a watcher can miss (network drives, lost
/// notifications). A report waits while a modal dialog has disabled the window or a menu is open,
/// so dialogs never stack.
/// </summary>
internal sealed class ExternalChangeMonitor : IDisposable
{
    /// <summary>Time from the last write seen to the report, and between tries while it has to wait.</summary>
    private const int SettleMilliseconds = 300;

    private readonly DocumentService _documents;
    private readonly IFileWatcher _watcher;
    private readonly MainForm _window;
    private readonly Func<bool> _menuActive;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = SettleMilliseconds };
    private IDisposable? _watch;
    private string? _watchedPath;
    private bool _disposed;

    public ExternalChangeMonitor(DocumentService documents, IFileWatcher watcher, MainForm window, Func<bool> menuActive)
    {
        _documents = documents;
        _watcher = watcher;
        _window = window;
        _menuActive = menuActive;
        _timer.Tick += (_, _) => OnTick();
        _documents.TitleUpdated += (_, _) =>
        {
            if (!string.Equals(_documents.State.Path, _watchedPath, StringComparison.OrdinalIgnoreCase))
            {
                Schedule();
            }
        };
        _window.Activated += (_, _) => ScheduleIfDocumentHasFile();
        _window.FormClosed += (_, _) => Dispose();
    }

    /// <summary>The path of the running watch, for tests.</summary>
    public string? WatchedPath => _watchedPath;

    private void ScheduleIfDocumentHasFile()
    {
        if (_documents.State.Path is not null)
        {
            Schedule();
        }
    }

    /// <summary>Restarts the wait: a report comes only once writes have stopped.</summary>
    private void Schedule()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>The watcher calls this on a thread of its own.</summary>
    private void OnFileChanged()
    {
        try
        {
            _window.BeginInvoke(Schedule);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // The window is gone, and the report with it.
        }
    }

    private void OnTick()
    {
        if (!_window.Enabled || _menuActive())
        {
            return;
        }

        _timer.Stop();
        // The watch starts here, not at the load, to keep it out of the startup path. The stamp is
        // compared after that, so a write before the watch began is still seen.
        UpdateWatch();
        _documents.NotifyIfChangedOutside();
    }

    private void UpdateWatch()
    {
        var path = _documents.State.Path;
        if (string.Equals(path, _watchedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _watch?.Dispose();
        _watch = path is null ? null : _watcher.Watch(path, OnFileChanged);
        _watchedPath = path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _watch?.Dispose();
        _watch = null;
    }
}
