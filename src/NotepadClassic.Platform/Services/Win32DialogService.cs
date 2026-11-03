using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Text;
using NotepadClassic.Platform.Interop;

namespace NotepadClassic.Platform.Services;

/// <summary>
/// The native dialogs of the original: <c>IFileOpenDialog</c>/<c>IFileSaveDialog</c> with the
/// "Encoding:" box, the save-changes task dialog, message boxes, <c>ChooseFontW</c> and
/// <c>ShellAboutW</c>.
/// </summary>
/// <param name="owner">Main window handle, read when a dialog opens.</param>
/// <param name="clientTopOffset">Go To placement correction, see <see cref="DialogPlacement"/>.</param>
public sealed unsafe class Win32DialogService(Func<nint> owner, ILocalizedStrings strings, Func<int> ansiCodePage, Func<int>? clientTopOffset = null) : IDialogService
{
    /// <summary>Control ID of the combo box and of its visual group.</summary>
    internal const uint EncodingComboId = 1;
    internal const uint EncodingGroupId = 2;

    private const int SaveButtonId = 6;
    private const int DontSaveButtonId = 7;
    private const int OverwriteButtonId = 6;
    private const int SaveAsButtonId = 7;
    private const int ReloadButtonId = 6;
    private const int KeepButtonId = 7;
    private const int ApplicationIconId = 32512;

    /// <summary><c>TD_WARNING_ICON</c>: <c>MAKEINTRESOURCEW(-1)</c>.</summary>
    private const nint WarningIcon = 0xFFFF;

    private readonly Func<nint> _owner = owner;
    private readonly ILocalizedStrings _strings = strings;
    private readonly Func<int> _ansiCodePage = ansiCodePage;
    private readonly Func<int>? _clientTopOffset = clientTopOffset;

    private string Caption => _strings.GetString(StringKeys.AppName);

    /// <summary>
    /// Task dialog: no icon, title "Notepad Classic", the question as main instruction,
    /// buttons "&amp;Save" (6), "Do&amp;n't Save" (7) and Cancel, Save the default. When the task
    /// dialog fails, a Yes/No/Cancel message box.
    /// </summary>
    public SaveChangesChoice AskSaveChanges(string instruction) =>
        AskTwoButtons(
            instruction, (SaveButtonId, StringKeys.SaveButton), (DontSaveButtonId, StringKeys.DontSaveButton), SaveButtonId, 0,
            User32.MB_YESNOCANCEL | User32.MB_ICONQUESTION) switch
        {
            SaveButtonId => SaveChangesChoice.Save,
            DontSaveButtonId => SaveChangesChoice.DontSave,
            _ => SaveChangesChoice.Cancel,
        };

    /// <summary>
    /// Task dialog: warning icon, title "Notepad Classic", the question as main instruction, buttons
    /// "&amp;Overwrite" (6), "Save &amp;As..." (7) and Cancel, Save As the default. When the task dialog
    /// fails, a Yes/No/Cancel message box on the same question, with No the default: its No is not
    /// overwriting, and leads to Save As.
    /// </summary>
    public FileChangedChoice AskFileChanged(string instruction) =>
        AskTwoButtons(
            instruction, (OverwriteButtonId, StringKeys.OverwriteButton), (SaveAsButtonId, StringKeys.SaveAsButton), SaveAsButtonId, WarningIcon,
            User32.MB_YESNOCANCEL | User32.MB_ICONEXCLAMATION | User32.MB_DEFBUTTON2) switch
        {
            OverwriteButtonId => FileChangedChoice.Overwrite,
            SaveAsButtonId => FileChangedChoice.SaveAs,
            _ => FileChangedChoice.Cancel,
        };

    /// <summary>Task dialog: warning icon, title "Notepad Classic", "&amp;Reload" (6) and "&amp;Keep" (7), no
    /// Cancel button but Esc is Keep. When the task dialog fails, a Yes/No message box on the same question.</summary>
    public bool AskReload(string instruction, bool preferReload) =>
        AskTwoButtons(
            instruction, (ReloadButtonId, StringKeys.ReloadButton), (KeepButtonId, StringKeys.KeepButton), preferReload ? ReloadButtonId : KeepButtonId, WarningIcon,
            User32.MB_YESNO | User32.MB_ICONEXCLAMATION | (preferReload ? 0 : User32.MB_DEFBUTTON2), cancelButton: false) == ReloadButtonId;

    /// <summary>The id of the button pressed. Ids 6 and 7 are also <c>IDYES</c> and <c>IDNO</c> of the
    /// fallback message box.</summary>
    private int AskTwoButtons(string instruction, (int Id, string TextKey) first, (int Id, string TextKey) second, int defaultButton, nint icon, uint fallbackFlags, bool cancelButton = true)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var title = Marshal.StringToCoTaskMemUni(Caption);
        var text = Marshal.StringToCoTaskMemUni(instruction);
        var firstText = Marshal.StringToCoTaskMemUni(_strings.GetString(first.TextKey));
        var secondText = Marshal.StringToCoTaskMemUni(_strings.GetString(second.TextKey));
        try
        {
            var buttons = stackalloc Comctl32.TASKDIALOG_BUTTON[2];
            buttons[0] = new() { nButtonID = first.Id, pszButtonText = firstText };
            buttons[1] = new() { nButtonID = second.Id, pszButtonText = secondText };
            var config = new Comctl32.TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(Comctl32.TASKDIALOGCONFIG),
                hwndParent = _owner(),
                dwFlags = cancelButton ? 0 : Comctl32.TDF_ALLOW_DIALOG_CANCELLATION,
                dwCommonButtons = cancelButton ? Comctl32.TDCBF_CANCEL_BUTTON : 0,
                pszWindowTitle = title,
                hMainIcon = icon,
                pszMainInstruction = text,
                cButtons = 2,
                pButtons = buttons,
                nDefaultButton = defaultButton,
            };
            int button;
            int hr;
            try
            {
                hr = Comctl32.TaskDialogIndirect(&config, &button, null, null);
            }
            catch (EntryPointNotFoundException)
            {
                // comctl32 version 5 has no task dialogs.
                hr = -1;
                button = 0;
            }

            if (hr < 0)
            {
                button = User32.MessageBox(_owner(), instruction, Caption, fallbackFlags);
            }

            return button;
        }
        finally
        {
            Marshal.FreeCoTaskMem(title);
            Marshal.FreeCoTaskMem(text);
            Marshal.FreeCoTaskMem(firstText);
            Marshal.FreeCoTaskMem(secondText);
        }
    }

    public FileDialogResult ShowOpen()
    {
        var hr = CreateDialog<IFileOpenDialog>(FileDialogIds.CLSID_FileOpenDialog, out var dialog);
        if (hr < 0)
        {
            return FileDialogResult.Fail(hr);
        }

        var customize = (IFileDialogCustomize)dialog;
        hr = AddEncodingCombo(customize, forOpen: true);
        if (hr >= 0)
        {
            hr = customize.SetSelectedControlItem(EncodingComboId, (uint)TextEncodingKind.Detect);
        }

        if (hr >= 0)
        {
            hr = ConfigureAndShow(dialog, _strings.GetString(StringKeys.OpenTitle), out var path);
            if (hr >= 0 && customize.GetSelectedControlItem(EncodingComboId, out var item) >= 0)
            {
                return FileDialogResult.Ok(path!, (TextEncodingKind)item);
            }
        }

        return Failure(hr);
    }

    public FileDialogResult ShowSaveAs(string suggestedName, TextEncodingKind initialEncoding)
    {
        ArgumentNullException.ThrowIfNull(suggestedName);
        var hr = CreateDialog<IFileSaveDialog>(FileDialogIds.CLSID_FileSaveDialog, out var dialog);
        if (hr < 0)
        {
            return FileDialogResult.Fail(hr);
        }

        var customize = (IFileDialogCustomize)dialog;
        hr = AddEncodingCombo(customize, forOpen: false);
        if (hr >= 0)
        {
            SelectSaveEncoding(customize, initialEncoding);
            SuggestName(dialog, suggestedName);
            hr = ConfigureAndShow(dialog, _strings.GetString(StringKeys.SaveAsTitle), out var path);
            if (hr >= 0 && customize.GetSelectedControlItem(EncodingComboId, out var item) >= 0)
            {
                return FileDialogResult.Ok(path!, (TextEncodingKind)item);
            }
        }

        return Failure(hr);
    }

    public MessageResult ShowMessage(string text, MessageKind kind)
    {
        var type = kind switch
        {
            MessageKind.WarningOkCancel => User32.MB_OKCANCEL | User32.MB_ICONEXCLAMATION,
            MessageKind.WarningYesNoCancel => User32.MB_YESNOCANCEL | User32.MB_ICONEXCLAMATION,
            MessageKind.SystemError => User32.MB_SYSTEMMODAL | User32.MB_ICONHAND,
            MessageKind.ShutdownBlocked => User32.MB_SYSTEMMODAL,
            _ => User32.MB_ICONEXCLAMATION,
        };
        if (kind == MessageKind.ShutdownBlocked)
        {
            _ = User32.MessageBeep(0);
            _ = User32.MessageBeep(0);
        }

        return User32.MessageBox(_owner(), text, Caption, type) switch
        {
            User32.IDCANCEL => MessageResult.Cancel,
            User32.IDYES => MessageResult.Yes,
            User32.IDNO => MessageResult.No,
            _ => MessageResult.Ok,
        };
    }

    /// <summary>
    /// <c>ChooseFontW</c> under <c>DPI_AWARENESS_CONTEXT_SYSTEM_AWARE</c> with flags
    /// <c>0x01010041</c> and <c>SCREEN_FONTTYPE</c>; the initial height comes from the screen DPI.
    /// </summary>
    public FontSpec? ChooseFont(FontSpec current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var previous = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE);
        try
        {
            var screen = User32.GetDC(0);
            var dpi = Gdi32.GetDeviceCaps(screen, User32.LOGPIXELSY);
            _ = User32.ReleaseDC(0, screen);

            var font = ToLogFont(current, current.HeightForDpi(dpi));
            var choose = new Comdlg32.CHOOSEFONTW
            {
                lStructSize = sizeof(Comdlg32.CHOOSEFONTW),
                hwndOwner = _owner(),
                lpLogFont = &font,
                Flags = ChooseFontFlags,
                nFontType = Comdlg32.SCREEN_FONTTYPE,
            };
            var address = (nint)(&choose);
            if (!DialogPlacement.WhileShowing(ClientTopOffset, () => Comdlg32.ChooseFont((Comdlg32.CHOOSEFONTW*)address)))
            {
                return null;
            }

            return FromLogFont(font, choose.iPointSize);
        }
        finally
        {
            if (previous != 0)
            {
                User32.SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    /// <summary><c>CF_SCREENFONTS | CF_INITTOLOGFONTSTRUCT | CF_FORCEFONTEXIST | CF_NOVERTFONTS</c>.</summary>
    internal const uint ChooseFontFlags = Comdlg32.CF_SCREENFONTS | Comdlg32.CF_INITTOLOGFONTSTRUCT | Comdlg32.CF_FORCEFONTEXIST | Comdlg32.CF_NOVERTFONTS;

    public int? ShowGoTo(GoToDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Win32GoToDialog.Show(_owner(), _strings, request, ClientTopOffset);
    }

    /// <summary>Pixels to move dialogs down; see <see cref="DialogPlacement"/>.</summary>
    private int ClientTopOffset => _clientTopOffset?.Invoke() ?? 0;

    public void ShowAbout()
    {
        var icon = User32.LoadImage(Kernel32Ui.GetModuleHandle(null), ApplicationIconId, User32.IMAGE_ICON, 0, 0, User32.LR_DEFAULTSIZE | User32.LR_SHARED);
        Shell32.ShellAbout(_owner(), Caption, "", icon);
    }

    internal static Comdlg32.LOGFONTW ToLogFont(FontSpec spec, int height)
    {
        var font = new Comdlg32.LOGFONTW
        {
            lfHeight = height,
            lfEscapement = spec.Escapement,
            lfOrientation = spec.Orientation,
            lfWeight = spec.Weight,
            lfItalic = spec.Italic ? (byte)1 : (byte)0,
            lfUnderline = spec.Underline ? (byte)1 : (byte)0,
            lfStrikeOut = spec.StrikeOut ? (byte)1 : (byte)0,
            lfCharSet = spec.CharSet,
            lfOutPrecision = spec.OutPrecision,
            lfClipPrecision = spec.ClipPrecision,
            lfQuality = spec.Quality,
            lfPitchAndFamily = spec.PitchAndFamily,
        };
        var face = spec.FaceName.AsSpan(0, Math.Min(spec.FaceName.Length, 31));
        face.CopyTo(new Span<char>(font.lfFaceName, 32));
        font.lfFaceName[face.Length] = '\0';
        return font;
    }

    internal static FontSpec FromLogFont(Comdlg32.LOGFONTW font, int pointSize10) => new(
        new string(font.lfFaceName),
        pointSize10,
        font.lfWeight,
        font.lfItalic != 0,
        font.lfUnderline != 0,
        font.lfStrikeOut != 0,
        font.lfCharSet,
        font.lfOutPrecision,
        font.lfClipPrecision,
        font.lfQuality,
        font.lfPitchAndFamily,
        font.lfEscapement,
        font.lfOrientation);

    /// <summary>
    /// <c>AddEncodingComboToFileDialog</c>: group "&amp;Encoding:", combo 1, items ANSI, UTF-16 LE,
    /// UTF-16 BE, UTF-8, UTF-8 with BOM (item IDs are the encoding codes), "Auto-Detect" (0) first
    /// for Open, "GB18030" (6) last only when the ANSI code page is 936. A failing step stops the
    /// chain, but the group is always closed.
    /// </summary>
    internal int AddEncodingCombo(IFileDialogCustomize customize, bool forOpen)
    {
        var hr = customize.StartVisualGroup(EncodingGroupId, _strings.GetString(StringKeys.EncodingLabel));
        try
        {
            if (hr < 0)
            {
                return hr;
            }

            hr = customize.AddComboBox(EncodingComboId);
            foreach (var (code, key) in ComboItems(forOpen))
            {
                if (hr < 0)
                {
                    return hr;
                }

                hr = customize.AddControlItem(EncodingComboId, (uint)code, key);
            }

            if (hr >= 0 && _ansiCodePage() == 936)
            {
                hr = customize.AddControlItem(EncodingComboId, (uint)TextEncodingKind.Gb18030, "GB18030");
            }

            return hr;
        }
        finally
        {
            customize.EndVisualGroup();
        }
    }

    private IEnumerable<(TextEncodingKind Code, string Text)> ComboItems(bool forOpen)
    {
        if (forOpen)
        {
            yield return (TextEncodingKind.Detect, _strings.GetString(StringKeys.EncodingAutoDetect));
        }

        yield return (TextEncodingKind.Ansi, _strings.GetString(StringKeys.EncodingAnsi));
        yield return (TextEncodingKind.Utf16LE, _strings.GetString(StringKeys.EncodingUtf16LE));
        yield return (TextEncodingKind.Utf16BE, _strings.GetString(StringKeys.EncodingUtf16BE));
        yield return (TextEncodingKind.Utf8, _strings.GetString(StringKeys.EncodingUtf8));
        yield return (TextEncodingKind.Utf8Bom, _strings.GetString(StringKeys.EncodingUtf8Bom));
    }

    /// <summary>Preselects the encoding; when the box has no such item, UTF-8.</summary>
    internal static void SelectSaveEncoding(IFileDialogCustomize customize, TextEncodingKind encoding)
    {
        if (customize.SetSelectedControlItem(EncodingComboId, (uint)encoding) < 0)
        {
            customize.SetSelectedControlItem(EncodingComboId, (uint)TextEncodingKind.Utf8);
        }
    }

    /// <summary>A bare name goes to <c>SetFileName</c>; a path becomes the "save as" item.</summary>
    private static void SuggestName(IFileSaveDialog dialog, string name)
    {
        if (name.AsSpan().IndexOfAny('\\', ':') < 0)
        {
            dialog.SetFileName(name);
            return;
        }

        if (Shell32.SHCreateItemFromParsingName(name, 0, FileDialogIds.IID_IShellItem, out var pointer) < 0)
        {
            return;
        }

        try
        {
            var item = ComInterfaceMarshaller<IShellItem>.ConvertToManaged((void*)pointer)!;
            dialog.SetSaveAsItem(item);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary><c>ConfigureAndShowFileDialog</c>: title, default extension <c>txt</c>, two file
    /// types, <c>FOS_FORCEFILESYSTEM</c>, show, file system path of the result.</summary>
    private int ConfigureAndShow(IFileDialog dialog, string title, out string? path)
    {
        path = null;
        var hr = dialog.SetTitle(title);
        if (hr >= 0)
        {
            hr = dialog.SetDefaultExtension("txt");
        }

        if (hr >= 0)
        {
            hr = SetFileTypes(dialog);
        }

        if (hr >= 0)
        {
            hr = dialog.GetOptions(out var options);
            if (hr >= 0)
            {
                hr = dialog.SetOptions(options | FileDialogIds.FOS_FORCEFILESYSTEM);
            }
        }

        if (hr >= 0)
        {
            hr = DialogPlacement.WhileShowing(ClientTopOffset, () => dialog.Show(_owner()));
        }

        if (hr >= 0)
        {
            hr = dialog.GetResult(out var item);
            if (hr >= 0 && item is not null)
            {
                hr = item.GetDisplayName(FileDialogIds.SIGDN_FILESYSPATH, out var name);
                if (hr >= 0)
                {
                    path = Marshal.PtrToStringUni(name);
                    Marshal.FreeCoTaskMem(name);
                }
            }
        }

        return hr;
    }

    private int SetFileTypes(IFileDialog dialog)
    {
        var text = Marshal.StringToCoTaskMemUni(_strings.GetString(StringKeys.TextDocumentsFilter));
        var textSpec = Marshal.StringToCoTaskMemUni("*.txt");
        var all = Marshal.StringToCoTaskMemUni(_strings.GetString(StringKeys.AllFilesFilter));
        var allSpec = Marshal.StringToCoTaskMemUni("*.*");
        try
        {
            var specs = stackalloc COMDLG_FILTERSPEC[2];
            specs[0] = new() { pszName = text, pszSpec = textSpec };
            specs[1] = new() { pszName = all, pszSpec = allSpec };
            return dialog.SetFileTypes(2, specs);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
            Marshal.FreeCoTaskMem(textSpec);
            Marshal.FreeCoTaskMem(all);
            Marshal.FreeCoTaskMem(allSpec);
        }
    }

    internal static int CreateDialog<T>(Guid clsid, out T dialog)
        where T : class
    {
        dialog = null!;
        var iid = typeof(T).GUID;
        var hr = Ole32.CoCreateInstance(clsid, 0, Ole32.CLSCTX_INPROC_SERVER, iid, out var pointer);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            dialog = ComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!;
            return 0;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static FileDialogResult Failure(int hr) =>
        hr == FileDialogIds.ERROR_CANCELLED_HRESULT ? FileDialogResult.Cancel() : FileDialogResult.Fail(hr);
}
