using System.Runtime.InteropServices;
using NotepadNet.Core.Search;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Services;

/// <summary>
/// The modeless COMDLG32 Find/Replace dialog. One <c>FINDREPLACEW</c> and its
/// two 128-WCHAR buffers live in native memory for the life of the app, as the original's
/// globals do; the dialog writes the boxes into them on every button press. The owner window
/// passes its <c>FINDMSGSTRING</c> messages to <see cref="HandleOwnerMessage"/> and its message
/// loop runs <see cref="IsDialogMessage"/>. No fallback copy of the dialog (parity.md).
/// </summary>
/// <param name="owner">Main window handle, read when a dialog opens or a box shows.</param>
/// <param name="caption">Title of the "Cannot find" box.</param>
/// <param name="clientTopOffset">Pixels between the owner's client top and where the original's
/// client area would start (the port's menu bar); see <see cref="DialogPlacement"/>.</param>
public sealed unsafe class Win32FindReplaceDialog(Func<nint> owner, Func<string> caption, Func<int>? clientTopOffset = null) : IFindReplaceDialog, IDisposable
{
    /// <summary><c>wFindWhatLen</c>/<c>wReplaceWithLen</c>: 128 WCHAR, 127 characters and the NUL.</summary>
    public const int BufferLength = SearchService.MaxPatternLength + 1;

    private readonly Func<nint> _owner = owner;
    private readonly Func<string> _caption = caption;
    private readonly Func<int>? _clientTopOffset = clientTopOffset;
    private readonly Comdlg32.FINDREPLACEW* _find = (Comdlg32.FINDREPLACEW*)NativeMemory.AllocZeroed((nuint)sizeof(Comdlg32.FINDREPLACEW));
    private readonly char* _findWhat = (char*)NativeMemory.AllocZeroed(BufferLength, sizeof(char));
    private readonly char* _replaceWith = (char*)NativeMemory.AllocZeroed(BufferLength, sizeof(char));
    private Func<FindDialogRequest, FindResults>? _handler;
    private bool _isReplace;
    private bool _disposed;

    /// <summary>The registered <c>FINDMSGSTRING</c> message.</summary>
    public uint MessageId { get; } = User32.RegisterWindowMessage(Comdlg32.FINDMSGSTRING);

    /// <summary>The open dialog, or 0.</summary>
    public nint Handle { get; private set; }

    public bool IsOpen => Handle != 0;

    public bool ShowFind(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler) => Show(setup, handler, replace: false);

    public bool ShowReplace(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler) => Show(setup, handler, replace: true);

    public void Close()
    {
        if (Handle != 0)
        {
            User32.SendMessage(Handle, User32.WM_CLOSE, 0, 0);
        }
    }

    public void ShowNotFound(string text) =>
        User32.MessageBox(Handle != 0 ? Handle : _owner(), text, _caption(), User32.MB_ICONINFORMATION);

    /// <summary>
    /// The <c>FINDMSGSTRING</c> branch of the owner's window procedure: reads the flags and the
    /// boxes, runs the handler and sends the result back with <c>0x464</c> while the dialog is
    /// still open. False for any other message.
    /// </summary>
    public bool HandleOwnerMessage(int msg, nint lParam)
    {
        if (MessageId == 0 || (uint)msg != MessageId || lParam == 0)
        {
            return false;
        }

        var find = (Comdlg32.FINDREPLACEW*)lParam;
        var flags = find->Flags;
        FindAction? action =
            (flags & Comdlg32.FR_FINDNEXT) != 0 ? FindAction.FindNext :
            (flags & Comdlg32.FR_REPLACE) != 0 ? FindAction.Replace :
            (flags & Comdlg32.FR_REPLACEALL) != 0 ? FindAction.ReplaceAll :
            (flags & Comdlg32.FR_DIALOGTERM) != 0 ? FindAction.Terminate : null;
        var handler = _handler;
        if (action == FindAction.Terminate)
        {
            Handle = 0;
            _handler = null;
        }

        var result = FindResults.None;
        if (action is { } a && handler is not null)
        {
            var request = new FindDialogRequest(
                a,
                ReadBuffer(find->lpstrFindWhat),
                find->lpstrReplaceWith == null ? null : ReadBuffer(find->lpstrReplaceWith),
                (flags & Comdlg32.FR_DOWN) != 0,
                (flags & Comdlg32.FR_MATCHCASE) != 0,
                (flags & Comdlg32.FR_WRAPAROUND) != 0);
            result = handler(request);
        }

        if (Handle != 0)
        {
            User32.SendMessage(Handle, Comdlg32.FindResultMessage, (nint)result, lParam);
        }

        return true;
    }

    /// <summary><c>IsDialogMessageW</c> for the open dialog; false when none is open.</summary>
    public bool IsDialogMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (Handle == 0)
        {
            return false;
        }

        var pos = User32.GetMessagePos();
        var message = new User32.MSG
        {
            hwnd = hwnd,
            message = (uint)msg,
            wParam = wParam,
            lParam = lParam,
            time = User32.GetMessageTime(),
            ptX = (short)(pos & 0xFFFF),
            ptY = (short)(pos >> 16),
        };
        return User32.IsDialogMessage(Handle, &message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != 0 && User32.IsWindow(Handle))
        {
            User32.DestroyWindow(Handle);
        }

        Handle = 0;
        NativeMemory.Free(_find);
        NativeMemory.Free(_findWhat);
        NativeMemory.Free(_replaceWith);
    }

    private bool Show(FindDialogSetup setup, Func<FindDialogRequest, FindResults> handler, bool replace)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteBuffer(_findWhat, setup.FindWhat);
        WriteBuffer(_replaceWith, setup.ReplaceWith ?? "");
        var flags = Comdlg32.FR_HIDEWHOLEWORD | Comdlg32.FR_SHOWWRAPAROUND;
        if (setup.Down)
        {
            flags |= Comdlg32.FR_DOWN;
        }

        if (setup.MatchCase)
        {
            flags |= Comdlg32.FR_MATCHCASE;
        }

        if (setup.WrapAround)
        {
            flags |= Comdlg32.FR_WRAPAROUND;
        }

        var offset = _clientTopOffset?.Invoke() ?? 0;
        if (offset != 0)
        {
            flags |= Comdlg32.FR_ENABLEHOOK;
        }

        *_find = new Comdlg32.FINDREPLACEW
        {
            lStructSize = sizeof(Comdlg32.FINDREPLACEW),
            hwndOwner = _owner(),
            hInstance = Kernel32Ui.GetModuleHandle(null),
            Flags = flags,
            lpstrFindWhat = _findWhat,
            lpstrReplaceWith = replace ? _replaceWith : null,
            wFindWhatLen = BufferLength,
            wReplaceWithLen = replace ? (ushort)BufferLength : (ushort)0,
            lCustData = offset,
            lpfnHook = offset != 0 ? &Hook : null,
        };
        _isReplace = replace;
        _handler = handler;
        Handle = replace ? Comdlg32.ReplaceText(_find) : Comdlg32.FindText(_find);
        if (Handle == 0)
        {
            _handler = null;
            return false;
        }

        return true;
    }

    /// <summary>Moves the dialog by <c>lCustData</c> while it initializes; TRUE lets it show.</summary>
    [UnmanagedCallersOnly]
    private static nint Hook(nint dialog, uint msg, nint wParam, nint lParam)
    {
        if (msg != User32.WM_INITDIALOG)
        {
            return 0;
        }

        DialogPlacement.ShiftDown(dialog, (int)((Comdlg32.FINDREPLACEW*)lParam)->lCustData);
        return 1;
    }

    /// <summary>Whether the open dialog is Replace (for tests and diagnostics).</summary>
    public bool IsReplace => IsOpen && _isReplace;

    private static void WriteBuffer(char* buffer, string text)
    {
        var length = Math.Min(text.Length, BufferLength - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(buffer, BufferLength));
        buffer[length] = '\0';
    }

    private static string ReadBuffer(char* buffer)
    {
        var span = new ReadOnlySpan<char>(buffer, BufferLength);
        var end = span.IndexOf('\0');
        return new string(end < 0 ? span : span[..end]);
    }
}
