using NotepadNet.Core.Editing;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Settings;

/// <summary>
/// Settings the original keeps in <c>HKCU\Software\Microsoft\Notepad</c>, with the defaults of
/// the original's registry values. The port keeps them in its own key, see
/// <see cref="RegistrySettingsStore"/>.
/// </summary>
public sealed class NotepadSettings
{
    /// <summary><c>CW_USEDEFAULT</c>, the default of the <c>iWindowPos*</c> values.</summary>
    public const int UseDefault = unchecked((int)0x8000_0000);

    /// <summary><c>lfFaceName</c>/<c>iPointSize</c> and the other <c>LOGFONTW</c> values.</summary>
    public FontSpec Font { get; set; } = FontSpec.Default;

    /// <summary><c>fWrap</c>, default 0.</summary>
    public bool WordWrap { get; set; }

    /// <summary><c>StatusBar</c>, default 1.</summary>
    public bool StatusBar { get; set; } = true;

    /// <summary><c>iOpacityPercent</c>, default 100 (port addition, the original has no such value).</summary>
    public int OpacityPercent { get; set; } = OpacityPolicy.Maximum;

    /// <summary><c>fAlwaysOnTop</c>, default 0 (port addition, the original has no such value).</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary><c>iDefaultEncoding</c>, default 5 (UTF-8). Never written by the original.</summary>
    public TextEncodingKind DefaultEncoding { get; set; } = TextEncodingKind.Utf8;

    /// <summary><c>fWindowsOnlyEOL</c>: the editor accepts only CRLF as a line break.</summary>
    public bool WindowsOnlyEol { get; set; }

    /// <summary><c>fPasteOriginalEOL</c>: pasted text keeps its own line breaks.</summary>
    public bool PasteOriginalEol { get; set; }

    /// <summary>"Font changed" flag: the font group is saved on exit.</summary>
    public bool FontChanged { get; set; }

    /// <summary>"Word Wrap or Status Bar changed" flag.</summary>
    public bool ViewChanged { get; set; }

    /// <summary><c>searchString</c>: the Find what buffer, at most
    /// <see cref="Search.SearchService.MaxPatternLength"/> characters.</summary>
    public string SearchString { get; set; } = "";

    /// <summary><c>replaceString</c>: the Replace with buffer, same limit.</summary>
    public string ReplaceString { get; set; } = "";

    /// <summary><c>fReverse</c>: the Find dialog searches up.</summary>
    public bool Reverse { get; set; }

    /// <summary><c>fMatchCase</c>.</summary>
    public bool MatchCase { get; set; }

    /// <summary><c>fWrapAround</c>.</summary>
    public bool WrapAround { get; set; }

    /// <summary>
    /// "Find settings changed" flag: set when Find or Replace opens. The
    /// search group is saved on exit only then, and Find Next/Previous search directly only then.
    /// </summary>
    public bool SearchChanged { get; set; }

    /// <summary><c>szHeader</c>, at most <see cref="SettingsSchema.HeaderCapacity"/> - 1 characters.</summary>
    public string Header { get; set; } = "&f";

    /// <summary><c>szTrailer</c> (the footer), same limit.</summary>
    public string Trailer { get; set; } = "Page &p";

    /// <summary><c>iMarginLeft/Top/Right/Bottom</c> in the unit of <see cref="MarginsInInches"/>.</summary>
    public PageMargins Margins { get; set; } = PageMargins.UsDefault;

    /// <summary>
    /// Margins are in thousandths of an inch (<c>PSD_INTHOUSANDTHSOFINCHES</c>, <c>LOCALE_IMEASURE</c>
    /// "1") rather than hundredths of a millimeter. Comes from the locale, never from the registry.
    /// </summary>
    public bool MarginsInInches { get; set; } = true;

    /// <summary>"Page Setup changed" flag.</summary>
    public bool PageSetupChanged { get; set; }

    /// <summary><c>iWindowPosX/Y/DX/DY</c> as read at startup, in 96-DPI (DPI-unaware) coordinates.</summary>
    public WindowPosition WindowPosition { get; set; } = WindowPosition.Default;

    /// <summary>
    /// Normal-position rectangle applied at startup: the
    /// position is saved on exit only when the window's rectangle differs from it. Empty when the
    /// window opened at the default position, so the first exit always saves it.
    /// </summary>
    public WindowRect WindowBaseline { get; set; }
}

/// <summary>Page margins as <c>PAGESETUPDLGW.rtMargin</c> holds them.</summary>
public readonly record struct PageMargins(int Left, int Top, int Right, int Bottom)
{
    /// <summary>US measure: 0.75 in left and right, 1 in top and bottom.</summary>
    public static PageMargins UsDefault { get; } = new(750, 1000, 750, 1000);

    /// <summary>Metric measure: 20 mm left and right, 25 mm top and bottom.</summary>
    public static PageMargins MetricDefault { get; } = new(2000, 2500, 2000, 2500);
}

/// <summary>The four <c>iWindowPos*</c> values; <see cref="NotepadSettings.UseDefault"/> when unset.</summary>
public readonly record struct WindowPosition(int X, int Y, int Width, int Height)
{
    public static WindowPosition Default { get; } =
        new(NotepadSettings.UseDefault, NotepadSettings.UseDefault, NotepadSettings.UseDefault, NotepadSettings.UseDefault);

    /// <summary>The saved position is applied only when both X and Y are set.</summary>
    public bool IsSet => X != NotepadSettings.UseDefault && Y != NotepadSettings.UseDefault;
}

/// <summary>A window rectangle (<c>WINDOWPLACEMENT.rcNormalPosition</c>).</summary>
public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public interface ISettingsStore
{
    NotepadSettings Load();

    /// <summary>
    /// <c>SaveSettings</c>, on <c>WM_CLOSE</c> before the save-changes question: writes only the
    /// groups whose "changed" flag is set, and the window position when
    /// <paramref name="window"/> differs from <see cref="NotepadSettings.WindowBaseline"/>.
    /// </summary>
    /// <param name="window">The window's normal position in DPI-unaware coordinates, or null when
    /// it could not be read.</param>
    void Save(NotepadSettings settings, WindowRect? window);
}

/// <summary>Settings that live only for the process: defaults on load, saves are kept in memory.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public NotepadSettings? LastSaved { get; private set; }

    public WindowRect? LastWindow { get; private set; }

    public NotepadSettings Load() => new();

    public void Save(NotepadSettings settings, WindowRect? window)
    {
        LastSaved = settings;
        LastWindow = window;
    }
}
