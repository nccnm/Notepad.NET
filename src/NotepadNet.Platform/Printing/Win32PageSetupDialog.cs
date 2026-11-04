using System.Runtime.InteropServices;
using NotepadNet.Core.Localization;
using NotepadNet.Core.Services;
using NotepadNet.Platform.Interop;
using NotepadNet.Platform.Services;

namespace NotepadNet.Platform.Printing;

/// <summary>
/// The Page Setup dialog: <c>PageSetupDlgW</c> with a template built in code (the common dialog's
/// standard control IDs, laid out like the original's dialog 12, plus Header, Footer and a help
/// link) and the hook of <c>PageSetupHookProc</c>.
/// </summary>
internal static unsafe class Win32PageSetupDialog
{
    public const int HeaderId = 0x1E;
    public const int FooterId = 0x1F;
    public const int HelpLinkId = 0x22;

    /// <summary>Header and footer accept 39 characters (<c>EM_LIMITTEXT 0x27</c>).</summary>
    public const int MaxTextLength = 39;

    private const uint BS_AUTORADIOBUTTON = 0x9;
    private const uint BS_GROUPBOX = 0x7;
    private const uint CBS_DROPDOWNLIST = 0x3;
    private const uint CBS_SORT = 0x100;
    private const uint WS_VSCROLL = 0x0020_0000;
    private const uint SS_GRAYRECT = 0x5;
    private const uint SS_WHITERECT = 0x6;
    private const uint DS_3DLOOK = 0x4;

    [ThreadStatic]
    private static State? t_state;

    /// <summary><c>PSD_MARGINS</c>, the unit of the margins, the hook and the template handle.</summary>
    public static uint Flags(bool inches) =>
        Comdlg32.PSD_MARGINS | (inches ? Comdlg32.PSD_INTHOUSANDTHSOFINCHES : Comdlg32.PSD_INHUNDREDTHSOFMILLIMETERS)
        | Comdlg32.PSD_ENABLEPAGESETUPHOOK | Comdlg32.PSD_ENABLEPAGESETUPTEMPLATEHANDLE;

    /// <summary>"Page Setup", "MS Shell Dlg" 8, 356x196 dialog units. Without a help URL the link
    /// is left out.</summary>
    internal static byte[] BuildTemplate(ILocalizedStrings strings, bool includeHelpLink)
    {
        const uint child = DialogTemplateBuilder.WS_CHILD | DialogTemplateBuilder.WS_VISIBLE;
        const uint group = DialogTemplateBuilder.WS_GROUP;
        const uint tab = DialogTemplateBuilder.WS_TABSTOP;
        const uint button = DialogTemplateBuilder.ButtonClass;
        const uint label = child | group;
        string S(string key) => strings.GetString(key);
        var builder = new DialogTemplateBuilder(
                DialogTemplateBuilder.WS_POPUP | DialogTemplateBuilder.WS_VISIBLE | DialogTemplateBuilder.WS_CAPTION
                    | DialogTemplateBuilder.WS_SYSMENU | DialogTemplateBuilder.DS_MODALFRAME | DialogTemplateBuilder.DS_SETFONT | DS_3DLOOK,
                exStyle: 0,
                new(0, 0, 356, 196),
                S(StringKeys.PageSetupTitle),
                "MS Shell Dlg",
                8)
            .AddItem(0x431, (ushort)button, S(StringKeys.PageSetupPaper), child | group | BS_GROUPBOX, new(8, 8, 224, 56))
            .AddItem(0x441, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupSize), label, new(16, 24, 36, 8))
            .AddItem(0x471, DialogTemplateBuilder.ComboBoxClass, "", child | WS_VSCROLL | group | tab | CBS_SORT | CBS_DROPDOWNLIST, new(64, 23, 160, 160))
            .AddItem(0x442, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupSource), label, new(16, 45, 36, 8))
            .AddItem(0x472, DialogTemplateBuilder.ComboBoxClass, "", child | WS_VSCROLL | group | tab | CBS_SORT | CBS_DROPDOWNLIST, new(64, 42, 160, 160))
            .AddItem(0x430, (ushort)button, S(StringKeys.PageSetupOrientation), child | group | BS_GROUPBOX, new(8, 69, 64, 56))
            .AddItem(0x420, (ushort)button, S(StringKeys.PageSetupPortrait), child | group | tab | BS_AUTORADIOBUTTON, new(16, 82, 52, 12))
            .AddItem(0x421, (ushort)button, S(StringKeys.PageSetupLandscape), child | BS_AUTORADIOBUTTON, new(16, 103, 52, 12))
            .AddItem(0x433, (ushort)button, S(StringKeys.PageSetupMargins), child | group | BS_GROUPBOX, new(80, 69, 152, 56))
            .AddItem(0x44E, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupLeft), label, new(88, 85, 32, 8))
            .AddItem(0x483, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | group | tab, new(120, 82, 28, 12))
            .AddItem(0x44F, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupRight), label, new(164, 85, 32, 8))
            .AddItem(0x485, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | group | tab, new(196, 82, 28, 12))
            .AddItem(0x450, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupTop), label, new(88, 104, 32, 8))
            .AddItem(0x484, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | group | tab, new(120, 103, 28, 12))
            .AddItem(0x451, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupBottom), label, new(164, 104, 32, 8))
            .AddItem(0x486, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | group | tab, new(196, 103, 28, 12))
            .AddItem(0x20, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupHeader), label, new(8, 135, 29, 8))
            .AddItem(HeaderId, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | tab | DialogTemplateBuilder.ES_AUTOHSCROLL, new(56, 134, 174, 12))
            .AddItem(0x21, DialogTemplateBuilder.StaticClass, S(StringKeys.PageSetupFooter), label, new(8, 154, 29, 8))
            .AddItem(FooterId, DialogTemplateBuilder.EditClass, "", child | DialogTemplateBuilder.WS_BORDER | tab | DialogTemplateBuilder.ES_AUTOHSCROLL, new(56, 153, 174, 12));
        if (includeHelpLink)
        {
            builder.AddItem(HelpLinkId, "SysLink", S(StringKeys.PageSetupHelpLink), child | tab, new(56, 170, 153, 8));
        }

        return builder
            .AddItem(User32.IDOK, (ushort)button, S(StringKeys.OkButton), child | group | tab | DialogTemplateBuilder.BS_DEFPUSHBUTTON, new(190, 174, 50, 14))
            .AddItem(User32.IDCANCEL, (ushort)button, S(StringKeys.CancelButton), child | tab, new(244, 174, 50, 14))
            .AddItem(0x402, (ushort)button, S(StringKeys.PageSetupPrinter), child | tab, new(298, 174, 50, 14))
            .AddItem(0xFFFF, (ushort)button, S(StringKeys.PageSetupPreview), child | BS_GROUPBOX, new(240, 8, 108, 158))
            .AddItem(0x438, DialogTemplateBuilder.StaticClass, "", child | SS_WHITERECT, new(254, 46, 80, 80))
            .AddItem(0x439, DialogTemplateBuilder.StaticClass, "", child | SS_GRAYRECT, new(334, 50, 4, 80))
            .AddItem(0x43A, DialogTemplateBuilder.StaticClass, "", child | SS_GRAYRECT, new(262, 122, 80, 4))
            .Build();
    }

    /// <summary>What the hook reads and writes during one dialog.</summary>
    internal sealed class State(string header, string footer, string? helpUrl, IShell shell, int clientTopOffset)
    {
        public string Header { get; } = header;

        public string Footer { get; } = footer;

        public string? HelpUrl { get; } = helpUrl;

        public IShell Shell { get; } = shell;

        public int ClientTopOffset { get; } = clientTopOffset;

        /// <summary>Read from the boxes on <c>WM_DESTROY</c>, so also after Cancel.</summary>
        public string? HeaderResult { get; set; }

        public string? FooterResult { get; set; }
    }

    /// <summary>Makes <paramref name="state"/> the one the hook works on until disposed.</summary>
    public static Scope Enter(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = t_state;
        t_state = state;
        return new(previous);
    }

    internal readonly struct Scope(State? previous) : IDisposable
    {
        public void Dispose() => t_state = previous;
    }

    /// <summary>
    /// <c>WM_INITDIALOG</c>: both boxes take 39 characters and the current text, the header is
    /// selected; the dialog moves below the port's menu bar. <c>WM_DESTROY</c>: reads both boxes.
    /// <c>NM_CLICK</c> or <c>NM_RETURN</c> on the link opens the help URL.
    /// </summary>
    [UnmanagedCallersOnly]
    public static nint Hook(nint dialog, uint msg, nint wParam, nint lParam)
    {
        if (t_state is not { } state)
        {
            return 0;
        }

        switch (msg)
        {
            case User32.WM_INITDIALOG:
                DialogPlacement.ShiftDown(dialog, state.ClientTopOffset);
                foreach (var (id, text) in new[] { (HeaderId, state.Header), (FooterId, state.Footer) })
                {
                    _ = User32.SendDlgItemMessage(dialog, id, User32.EM_LIMITTEXT, MaxTextLength, 0);
                    _ = User32.SetDlgItemText(dialog, id, text);
                }

                _ = User32.SendDlgItemMessage(dialog, HeaderId, User32.EM_SETSEL, 0, 0x27_0000);
                return 1;
            case User32.WM_DESTROY:
                state.HeaderResult = ReadText(dialog, HeaderId);
                state.FooterResult = ReadText(dialog, FooterId);
                return 0;
            case User32.WM_NOTIFY when wParam == HelpLinkId:
                var code = ((User32.NMHDR*)lParam)->code;
                if (code is User32.NM_CLICK or User32.NM_RETURN && !string.IsNullOrEmpty(state.HelpUrl))
                {
                    state.Shell.OpenUri(state.HelpUrl);
                    return 1;
                }

                return 0;
            default:
                return 0;
        }
    }

    /// <summary><c>GetDlgItemTextW(..., 40)</c>.</summary>
    private static string ReadText(nint dialog, int id)
    {
        var buffer = stackalloc char[MaxTextLength + 1];
        var length = (int)User32.GetDlgItemText(dialog, id, buffer, MaxTextLength + 1);
        return new string(buffer, 0, length);
    }
}
