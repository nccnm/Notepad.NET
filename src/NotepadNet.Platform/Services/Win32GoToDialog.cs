using System.Runtime.InteropServices;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Localization;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Services;

/// <summary>
/// Edit > Go To (<c>GoToDlgProc</c>): a modal dialog built in code with the layout
/// of the original's dialog 14, so it measures the same, run by <c>DialogBoxIndirectParamW</c>
/// (the native modal loop, so the app's accelerators do not act inside it, as in the original).
/// </summary>
internal static unsafe class Win32GoToDialog
{
    public const int LineNumberId = 0x102;
    public const int LabelId = 0x4B0;

    /// <summary><c>GetDlgItemTextW(..., 100)</c>.</summary>
    private const int InputBufferLength = 100;

    private const int OkCommand = User32.IDOK;
    private const int CancelCommand = User32.IDCANCEL;

    /// <summary>The dialog template: "Go To Line", "MS Shell Dlg" 8, 166x60 dialog units; a label,
    /// a numbers-only box and the Go To/Cancel buttons.</summary>
    internal static byte[] BuildTemplate(ILocalizedStrings strings)
    {
        const uint child = DialogTemplateBuilder.WS_CHILD | DialogTemplateBuilder.WS_VISIBLE;
        return new DialogTemplateBuilder(
                DialogTemplateBuilder.WS_POPUP | DialogTemplateBuilder.WS_CAPTION | DialogTemplateBuilder.WS_SYSMENU
                    | DialogTemplateBuilder.DS_MODALFRAME | DialogTemplateBuilder.DS_SETFONT,
                exStyle: 0,
                new(0, 0, 166, 60),
                strings.GetString(StringKeys.GoToTitle),
                "MS Shell Dlg",
                8)
            .AddItem(LabelId, DialogTemplateBuilder.StaticClass, strings.GetString(StringKeys.GoToLineLabel),
                child | DialogTemplateBuilder.WS_GROUP, new(7, 7, 115, 8))
            .AddItem(LineNumberId, DialogTemplateBuilder.EditClass, "",
                child | DialogTemplateBuilder.WS_BORDER | DialogTemplateBuilder.WS_TABSTOP | DialogTemplateBuilder.ES_NUMBER | DialogTemplateBuilder.ES_AUTOHSCROLL,
                new(7, 18, 151, 14))
            .AddItem(User32.IDOK, DialogTemplateBuilder.ButtonClass, strings.GetString(StringKeys.GoToButton),
                child | DialogTemplateBuilder.WS_TABSTOP | DialogTemplateBuilder.BS_DEFPUSHBUTTON, new(55, 39, 50, 14))
            .AddItem(User32.IDCANCEL, DialogTemplateBuilder.ButtonClass, strings.GetString(StringKeys.CancelButton),
                child | DialogTemplateBuilder.WS_TABSTOP, new(109, 39, 50, 14))
            .Build();
    }

    /// <summary>Shows the dialog; the accepted line, or null for Cancel (or a failed dialog).</summary>
    /// <param name="clientTopOffset">See <see cref="DialogPlacement"/>.</param>
    public static int? Show(nint owner, ILocalizedStrings strings, GoToDialogRequest request, int clientTopOffset = 0)
    {
        var template = BuildTemplate(strings);
        var state = new State(request, clientTopOffset);
        var handle = GCHandle.Alloc(state);
        try
        {
            nint result;
            fixed (byte* bytes = template)
            {
                result = User32.DialogBoxIndirectParam(
                    Kernel32Ui.GetModuleHandle(null), bytes, owner, &DialogProc, GCHandle.ToIntPtr(handle));
            }

            state.Error?.Throw();
            return result == 0 ? state.Line : null;
        }
        finally
        {
            handle.Free();
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
        if (pointer == 0 || GCHandle.FromIntPtr(pointer).Target is not State state)
        {
            return 0;
        }

        try
        {
            return state.Handle(dialog, msg, wParam);
        }
#pragma warning disable CA1031 // An exception must not cross the native dialog loop; it is rethrown after it ends.
        catch (Exception e)
#pragma warning restore CA1031
        {
            state.Error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
            User32.EndDialog(dialog, -2);
            return 1;
        }
    }

    private sealed class State(GoToDialogRequest request, int clientTopOffset)
    {
        public int? Line { get; private set; }

        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Error { get; set; }

        /// <summary>
        /// <c>WM_INITDIALOG</c>: the initial text, focus to the dialog, TRUE (the dialog manager
        /// then focuses the first tab stop). OK reads at most 99 characters and either ends with 0
        /// or shows the error and keeps the dialog open; Cancel ends with 1. The original compares
        /// the whole <c>wParam</c>, so only <c>BN_CLICKED</c>-style notifications count.
        /// </summary>
        public nint Handle(nint dialog, uint msg, nint wParam)
        {
            if (msg == User32.WM_INITDIALOG)
            {
                DialogPlacement.ShiftDown(dialog, clientTopOffset);
                User32.SetDlgItemText(dialog, LineNumberId, request.InitialText);
                User32.SetFocus(dialog);
                return 1;
            }

            if (msg != User32.WM_COMMAND)
            {
                return 0;
            }

            switch ((long)wParam)
            {
                case OkCommand:
                    var buffer = stackalloc char[InputBufferLength];
                    var length = (int)User32.GetDlgItemText(dialog, LineNumberId, buffer, InputBufferLength);
                    var check = request.Check(new string(buffer, 0, length));
                    if (check.Line is { } line)
                    {
                        Line = line;
                        User32.EndDialog(dialog, 0);
                        return 1;
                    }

                    User32.MessageBox(dialog, request.ErrorText, request.ErrorCaption, User32.MB_OK);
                    User32.SetDlgItemText(dialog, LineNumberId, check.ReplacementText ?? "");
                    User32.SetFocus(dialog);
                    return 0;
                case CancelCommand:
                    User32.EndDialog(dialog, 1);
                    return 1;
                case LineNumberId:
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
