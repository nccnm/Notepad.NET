using NotepadNet.App.Interop;

namespace NotepadNet.App.Shell;

/// <summary>
/// <c>SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)</c> for this process, as <c>NotepadWinMain</c>
/// installs it: every location change, the caret's included, refreshes Ln/Col. No filter on the
/// window or object, as in the original; the status bar cache skips unchanged values.
/// </summary>
internal sealed class CaretWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;
    private nint _hook;

    public CaretWatcher(Action onLocationChange)
    {
        _callback = (_, _, _, _, _, _, _) => onLocationChange();
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, 0, _callback,
            (uint)Environment.ProcessId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    public bool IsInstalled => _hook != 0;

    public void Dispose()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = 0;
        }
    }
}
