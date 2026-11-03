using NotepadClassic.App.Interop;
using NotepadClassic.Core.Commands;

namespace NotepadClassic.App.Shell;

/// <summary>
/// The two accelerator tables of the original message loop: <c>GlobalAcc</c> first, then the modeless Find/Replace dialog, then
/// <c>MainAcc</c> only for keys aimed at the main window. A key bound in a table is always
/// consumed; its command runs only when enabled, like the accelerator of a grayed menu item
/// (<c>UpdateMenuState</c>, inference).
/// </summary>
internal sealed class KeyDispatcher(
    CommandRegistry registry, Func<CommandContext> context, Func<nint> mainWindow, Func<KeyModifiers> modifiers) : IMessageFilter
{
    private readonly CommandRegistry _registry = registry;
    private readonly Func<CommandContext> _context = context;
    private readonly Func<nint> _mainWindow = mainWindow;
    private readonly Func<KeyModifiers> _modifiers = modifiers;

    /// <summary>Set while the menu bar is active: the menu handles keys itself, as the original's
    /// menu loop does.</summary>
    public bool MenuActive { get; set; }

    /// <summary><c>IsDialogMessage</c> of the modeless Find/Replace dialog; it sees every message,
    /// not only keys, so Tab, Enter, Esc and mnemonics work inside the dialog.</summary>
    public Func<Message, bool>? DialogMessage { get; set; }

    public static KeyModifiers CurrentModifiers()
    {
        var modifiers = KeyModifiers.None;
        if (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) < 0)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) < 0)
        {
            modifiers |= KeyModifiers.Control;
        }

        if (NativeMethods.GetKeyState(NativeMethods.VK_MENU) < 0)
        {
            modifiers |= KeyModifiers.Alt;
        }

        return modifiers;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEWHEEL && IsOpacityWheel(m))
        {
            OnOpacityWheel((short)NativeMethods.HighWord(m.WParam));
            return true;
        }

        var isKey = (m.Msg == NativeMethods.WM_KEYDOWN || m.Msg == NativeMethods.WM_SYSKEYDOWN) && !MenuActive;
        var chord = isKey ? new KeyChord((ushort)(m.WParam & 0xFFFF), _modifiers()) : default;
        if (isKey && Dispatch(chord, KeyTier.Global))
        {
            return true;
        }

        if (DialogMessage?.Invoke(m) == true)
        {
            return true;
        }

        if (!isKey)
        {
            return false;
        }

        var main = _mainWindow();
        return (m.HWnd == main || NativeMethods.IsChild(main, m.HWnd)) && Dispatch(chord, KeyTier.Main);
    }

    /// <summary><c>/.SETUP</c>: the second table is <c>SlipUpAcc</c>, i.e. MainAcc followed by the
    /// setup-only keys; an earlier entry wins, so MainAcc is looked up first.</summary>
    public bool SetupMode { get; set; }

    /// <summary>Wheel travel not yet worth a step: a high-resolution wheel sends less than
    /// <c>WHEEL_DELTA</c> per message.</summary>
    private int _opacityWheel;

    /// <summary>Ctrl+Shift+wheel over the main window changes its opacity (port addition, as in
    /// Windows Terminal). It never reaches the Edit control, which would zoom on the Ctrl.</summary>
    private bool IsOpacityWheel(in Message m)
    {
        const int Chord = NativeMethods.MK_CONTROL | NativeMethods.MK_SHIFT;
        if (MenuActive || (NativeMethods.LowWord(m.WParam) & Chord) != Chord)
        {
            return false;
        }

        var main = _mainWindow();
        return m.HWnd == main || NativeMethods.IsChild(main, m.HWnd);
    }

    /// <summary>Up is more opaque, down more transparent.</summary>
    private void OnOpacityWheel(int delta)
    {
        // A change of direction drops what was left of the other one.
        _opacityWheel = Math.Sign(_opacityWheel) == -Math.Sign(delta) ? delta : _opacityWheel + delta;
        while (Math.Abs(_opacityWheel) >= NativeMethods.WHEEL_DELTA)
        {
            var up = _opacityWheel > 0;
            _opacityWheel -= up ? NativeMethods.WHEEL_DELTA : -NativeMethods.WHEEL_DELTA;
            _registry.TryExecute(up ? CommandId.LessTransparent : CommandId.MoreTransparent, _context());
        }
    }

    private bool Dispatch(KeyChord chord, KeyTier tier)
    {
        var binding = _registry.FindKey(chord, tier);
        if (binding is null && tier == KeyTier.Main && SetupMode)
        {
            binding = _registry.FindKey(chord, KeyTier.SetupOnly);
        }

        if (binding is null)
        {
            return false;
        }

        _registry.TryExecute(binding.Command, _context());
        return true;
    }
}
