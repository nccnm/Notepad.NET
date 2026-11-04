using System.Runtime.InteropServices;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Printing;
using NotepadNet.Platform.Interop;
using NotepadNet.Platform.Services;

namespace NotepadNet.Platform.Printing;

/// <summary>
/// The modeless "Now Printing" box (<c>PrintAbortDlgProc</c>), built in code with
/// the layout of the original's dialog 11. It disables the owner while it lives. Any
/// <c>WM_COMMAND</c> (Cancel, Esc, the close box) cancels the job and closes the box;
/// <c>WM_INITMENU</c> grays Close in its system menu.
/// </summary>
public sealed unsafe class Win32PrintProgress : IPrintProgress
{
    public const int FileNameId = 0x14;
    public const int PageId = 0x15;

    private const uint SS_CENTER = 0x1;
    private const uint SS_PATHELLIPSIS = 0x8000;

    private readonly nint _owner;
    private readonly ILocalizedStrings _strings;
    private readonly string _fileLabel;
    private readonly int _clientTopOffset;
    private GCHandle _self;

    /// <param name="clientTopOffset">See <see cref="DialogPlacement"/>.</param>
    public Win32PrintProgress(nint owner, ILocalizedStrings strings, string fileLabel, int clientTopOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(fileLabel);
        _owner = owner;
        _strings = strings;
        _fileLabel = fileLabel;
        _clientTopOffset = clientTopOffset;
        _ = User32.EnableWindow(owner, false);
        _self = GCHandle.Alloc(this);
        var template = BuildTemplate(strings);
        fixed (byte* bytes = template)
        {
            Handle = User32.CreateDialogIndirectParam(
                Kernel32Ui.GetModuleHandle(null), bytes, owner, &DialogProc, GCHandle.ToIntPtr(_self));
        }
    }

    /// <summary>The dialog window, 0 once it is closed.</summary>
    public nint Handle { get; private set; }

    public bool Cancelled { get; private set; }

    /// <summary>"Notepad.NET", "MS Shell Dlg" 8, 200x64 dialog units: Cancel, the "Now
    /// Printing" line, the file name (path ellipsis) and the page line, all centered.</summary>
    internal static byte[] BuildTemplate(ILocalizedStrings strings)
    {
        const uint child = DialogTemplateBuilder.WS_CHILD | DialogTemplateBuilder.WS_VISIBLE;
        return new DialogTemplateBuilder(
                DialogTemplateBuilder.WS_POPUP | DialogTemplateBuilder.WS_VISIBLE | DialogTemplateBuilder.WS_CAPTION
                    | DialogTemplateBuilder.WS_SYSMENU | DialogTemplateBuilder.DS_MODALFRAME | DialogTemplateBuilder.DS_SETFONT,
                exStyle: 0,
                new(20, 20, 200, 64),
                strings.GetString(StringKeys.AppName),
                "MS Shell Dlg",
                8)
            .AddItem(User32.IDCANCEL, DialogTemplateBuilder.ButtonClass, strings.GetString(StringKeys.CancelButton),
                child | DialogTemplateBuilder.WS_GROUP | DialogTemplateBuilder.WS_TABSTOP | DialogTemplateBuilder.BS_DEFPUSHBUTTON, new(84, 44, 32, 14))
            .AddItem(0xFFFF, DialogTemplateBuilder.StaticClass, strings.GetString(StringKeys.NowPrinting),
                child | DialogTemplateBuilder.WS_GROUP | SS_CENTER, new(0, 8, 200, 8))
            .AddItem(FileNameId, DialogTemplateBuilder.StaticClass, "",
                child | DialogTemplateBuilder.WS_GROUP | SS_PATHELLIPSIS | SS_CENTER, new(0, 18, 200, 8))
            .AddItem(PageId, DialogTemplateBuilder.StaticClass, "",
                child | DialogTemplateBuilder.WS_GROUP | SS_CENTER, new(0, 28, 200, 8))
            .Build();
    }

    public void ShowPage(int page)
    {
        if (Handle != 0)
        {
            _ = User32.SetDlgItemText(Handle, PageId, MessageText.Printf(_strings.GetString(StringKeys.PageNumber), page));
        }
    }

    [UnmanagedCallersOnly]
    private static nint DialogProc(nint dialog, uint msg, nint wParam, nint lParam)
    {
        if (msg == User32.WM_INITDIALOG)
        {
            User32.SetWindowLongPtr(dialog, User32.DWLP_USER, lParam);
        }

        var pointer = User32.GetWindowLongPtr(dialog, User32.DWLP_USER);
        if (pointer == 0 || GCHandle.FromIntPtr(pointer).Target is not Win32PrintProgress self)
        {
            return 0;
        }

        switch (msg)
        {
            case User32.WM_INITDIALOG:
                DialogPlacement.ShiftDown(dialog, self._clientTopOffset);
                _ = User32.SetDlgItemText(dialog, FileNameId, self._fileLabel);
                return 1;
            case User32.WM_COMMAND:
                self.Cancel();
                return 1;
            case User32.WM_INITMENU:
                _ = User32.EnableMenuItem(User32.GetSystemMenu(dialog, false), User32.SC_CLOSE, User32.MF_BYCOMMAND | User32.MF_GRAYED);
                return 1;
            default:
                return 0;
        }
    }

    private void Cancel()
    {
        Cancelled = true;
        Close();
    }

    private void Close()
    {
        _ = User32.EnableWindow(_owner, true);
        if (Handle != 0)
        {
            var handle = Handle;
            Handle = 0;
            _ = User32.DestroyWindow(handle);
        }
    }

    public void Dispose()
    {
        Close();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}
