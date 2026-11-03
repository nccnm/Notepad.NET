using NotepadClassic.Core.Editing;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Settings;

/// <summary>Registry keys the settings come from.</summary>
public enum SettingsLocation
{
    /// <summary><c>HKCU\Software\NotepadClassic</c>: the port's own settings, read and written.</summary>
    User,

    /// <summary><c>HKLM\Software\NotepadClassic\DefaultFonts</c>: machine-wide default font.</summary>
    MachineDefaultFonts,

    /// <summary><c>HKCU\Software\Microsoft\Notepad</c>: the original's settings, only ever read (import).</summary>
    Legacy,
}

/// <summary>Opens the registry keys of <see cref="SettingsLocation"/>. Platform implements it on
/// real registry roots; tests use temporary hives or memory.</summary>
public interface ISettingsRegistry
{
    /// <summary>Opens a key for reading (<c>KEY_READ</c>); null when it does not exist or cannot be opened.</summary>
    ISettingsKey? Open(SettingsLocation location);

    /// <summary>Creates or opens the user key for writing (<c>RegCreateKeyW</c>); null on failure.</summary>
    ISettingsKey? CreateUser();
}

/// <summary>One open registry key.</summary>
public interface ISettingsKey : IDisposable
{
    /// <summary><c>RegQueryValueExW</c> into 4 bytes: true only for a <c>REG_DWORD</c> whose data fits.</summary>
    bool TryReadDword(string name, out int value);

    /// <summary>
    /// <c>RegQueryValueExW</c> into a buffer of <paramref name="capacity"/> WCHARs: true only for a
    /// <c>REG_SZ</c> whose data fits. <paramref name="value"/> is the raw data, which may contain a
    /// NUL or lack one.
    /// </summary>
    bool TryReadString(string name, int capacity, out string value);

    /// <summary><c>RegSetValueExW(REG_DWORD)</c>; errors are ignored, as in the original.</summary>
    void WriteDword(string name, int value);

    /// <summary><c>RegSetValueExW(REG_SZ)</c> with the terminating NUL; errors are ignored.</summary>
    void WriteString(string name, string value);
}

/// <summary>Value names, kinds and buffer sizes of the original's registry values.</summary>
public static class SettingsSchema
{
    public const int FaceNameCapacity = 32;
    public const int SearchCapacity = 128;
    public const int HeaderCapacity = 40;

    public const string Escapement = "lfEscapement";
    public const string Orientation = "lfOrientation";
    public const string Weight = "lfWeight";
    public const string Italic = "lfItalic";
    public const string Underline = "lfUnderline";
    public const string StrikeOut = "lfStrikeOut";
    public const string CharSet = "lfCharSet";
    public const string OutPrecision = "lfOutPrecision";
    public const string ClipPrecision = "lfClipPrecision";
    public const string Quality = "lfQuality";
    public const string PitchAndFamily = "lfPitchAndFamily";
    public const string FaceName = "lfFaceName";
    public const string PointSize = "iPointSize";
    public const string Wrap = "fWrap";
    public const string StatusBar = "StatusBar";

    /// <summary>Port addition: not a value of the original, so it is never imported.</summary>
    public const string OpacityPercent = "iOpacityPercent";
    public const string DefaultEncoding = "iDefaultEncoding";
    public const string SaveWindowPositions = "fSaveWindowPositions";
    public const string WindowsOnlyEol = "fWindowsOnlyEOL";
    public const string PasteOriginalEol = "fPasteOriginalEOL";
    public const string Reverse = "fReverse";
    public const string WrapAround = "fWrapAround";
    public const string MatchCase = "fMatchCase";
    public const string SearchString = "searchString";
    public const string ReplaceString = "replaceString";
    public const string Header = "szHeader";
    public const string Trailer = "szTrailer";
    public const string MarginTop = "iMarginTop";
    public const string MarginBottom = "iMarginBottom";
    public const string MarginLeft = "iMarginLeft";
    public const string MarginRight = "iMarginRight";
    public const string WindowPosX = "iWindowPosX";
    public const string WindowPosY = "iWindowPosY";
    public const string WindowPosDX = "iWindowPosDX";
    public const string WindowPosDY = "iWindowPosDY";
    public const string MleIsBroken = "fMLE_is_broken";

    /// <summary>The five DWORDs <c>RegReadDwordOrCreateZero</c> creates as 0 when missing or of the wrong kind.</summary>
    public static IReadOnlyList<string> CreatedAsZero { get; } = [WindowsOnlyEol, PasteOriginalEol, Reverse, WrapAround, MatchCase];

    /// <summary>DWORD values copied by the one-time import from the original's key.</summary>
    public static IReadOnlyList<string> ImportedDwords { get; } =
    [
        Escapement, Orientation, Weight, Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality,
        PitchAndFamily, PointSize, Wrap, StatusBar, DefaultEncoding, WindowsOnlyEol, PasteOriginalEol, Reverse, WrapAround,
        MatchCase, MarginTop, MarginBottom, MarginLeft, MarginRight, WindowPosX, WindowPosY, WindowPosDX, WindowPosDY,
    ];

    /// <summary>String values copied by the import, with their buffer capacity.</summary>
    public static IReadOnlyList<(string Name, int Capacity)> ImportedStrings { get; } =
    [
        (FaceName, FaceNameCapacity), (SearchString, SearchCapacity), (ReplaceString, SearchCapacity),
        (Header, HeaderCapacity), (Trailer, HeaderCapacity),
    ];
}

/// <summary>Defaults that depend on the machine rather than the registry.</summary>
/// <param name="MetricMeasure"><c>LOCALE_IMEASURE</c> is not "1": margins in hundredths of a millimeter.</param>
/// <param name="Header">Header when the registry has none (MUI 18 <c>&amp;f</c>).</param>
/// <param name="Trailer">Footer when the registry has none (MUI 19 <c>Page &amp;p</c>).</param>
public sealed record SettingsDefaults(bool MetricMeasure, string Header, string Trailer);

/// <summary>
/// The original's <c>LoadSettings</c> and <c>SaveSettings</c>
/// on the port's own key:
/// <list type="bullet">
/// <item>a missing or wrongly typed value takes its default; the five values of
/// <see cref="SettingsSchema.CreatedAsZero"/> are then also written as 0;</item>
/// <item>the font face and size default to <see cref="SettingsLocation.MachineDefaultFonts"/>,
/// then <c>Lucida Console</c> 10 pt; <c>lfHeight</c>/<c>lfWidth</c> are never stored;</item>
/// <item>saving writes only the groups whose "changed" flag is set, the window position only when
/// it moved, and never <c>iDefaultEncoding</c>, <c>fWindowsOnlyEOL</c>, <c>fPasteOriginalEOL</c>,
/// <c>fSaveWindowPositions</c> or <c>fMLE_is_broken</c>.</item>
/// </list>
/// When the port's key does not exist yet, valid values of the original's key are copied into it
/// once (<see cref="ImportLegacy"/>); the original's key is never written.
/// </summary>
public sealed class RegistrySettingsStore(ISettingsRegistry registry, SettingsDefaults defaults) : ISettingsStore
{
    private readonly ISettingsRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SettingsDefaults _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));

    public NotepadSettings Load()
    {
        ImportLegacy();
        var (defaultFace, defaultSize) = MachineDefaultFont();
        using var key = _registry.Open(SettingsLocation.User);
        int Dword(string name, int fallback) => key is not null && key.TryReadDword(name, out var v) ? v : fallback;
        string Text(string name, int capacity, string fallback) =>
            key is not null && key.TryReadString(name, capacity, out var raw) ? TrimRegistryString(raw, capacity) : fallback;

        var margins = _defaults.MetricMeasure ? PageMargins.MetricDefault : PageMargins.UsDefault;
        var settings = new NotepadSettings
        {
            Font = new FontSpec(
                Text(SettingsSchema.FaceName, SettingsSchema.FaceNameCapacity, defaultFace),
                Dword(SettingsSchema.PointSize, defaultSize),
                Dword(SettingsSchema.Weight, 0),
                (byte)Dword(SettingsSchema.Italic, 0) != 0,
                (byte)Dword(SettingsSchema.Underline, 0) != 0,
                (byte)Dword(SettingsSchema.StrikeOut, 0) != 0,
                (byte)Dword(SettingsSchema.CharSet, 0),
                (byte)Dword(SettingsSchema.OutPrecision, 0),
                (byte)Dword(SettingsSchema.ClipPrecision, 0),
                (byte)Dword(SettingsSchema.Quality, 0),
                (byte)Dword(SettingsSchema.PitchAndFamily, 0),
                Dword(SettingsSchema.Escapement, 0),
                Dword(SettingsSchema.Orientation, 0)),
            WordWrap = Dword(SettingsSchema.Wrap, 0) != 0,
            DefaultEncoding = (TextEncodingKind)Dword(SettingsSchema.DefaultEncoding, (int)TextEncodingKind.Utf8),
            StatusBar = Dword(SettingsSchema.StatusBar, 1) != 0,
            OpacityPercent = OpacityPolicy.Clamp(Dword(SettingsSchema.OpacityPercent, OpacityPolicy.Maximum)),
            SearchString = Text(SettingsSchema.SearchString, SettingsSchema.SearchCapacity, ""),
            ReplaceString = Text(SettingsSchema.ReplaceString, SettingsSchema.SearchCapacity, ""),
            Header = Text(SettingsSchema.Header, SettingsSchema.HeaderCapacity, _defaults.Header),
            Trailer = Text(SettingsSchema.Trailer, SettingsSchema.HeaderCapacity, _defaults.Trailer),
            MarginsInInches = !_defaults.MetricMeasure,
            Margins = new PageMargins(
                Dword(SettingsSchema.MarginLeft, margins.Left),
                Dword(SettingsSchema.MarginTop, margins.Top),
                Dword(SettingsSchema.MarginRight, margins.Right),
                Dword(SettingsSchema.MarginBottom, margins.Bottom)),
            WindowPosition = new WindowPosition(
                Dword(SettingsSchema.WindowPosX, NotepadSettings.UseDefault),
                Dword(SettingsSchema.WindowPosY, NotepadSettings.UseDefault),
                Dword(SettingsSchema.WindowPosDX, NotepadSettings.UseDefault),
                Dword(SettingsSchema.WindowPosDY, NotepadSettings.UseDefault)),
        };

        var zeros = ReadOrCreateZero(key);
        settings.WindowsOnlyEol = zeros[0] != 0;
        settings.PasteOriginalEol = zeros[1] != 0;
        settings.Reverse = zeros[2] != 0;
        settings.WrapAround = zeros[3] != 0;
        settings.MatchCase = zeros[4] != 0;
        return settings;
    }

    /// <summary><c>RegReadDwordOrCreateZero</c> for the five values, creating the key when needed.</summary>
    private int[] ReadOrCreateZero(ISettingsKey? key)
    {
        var names = SettingsSchema.CreatedAsZero;
        var values = new int[names.Count];
        var missing = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            if (key is not null && key.TryReadDword(names[i], out var value))
            {
                values[i] = value;
            }
            else
            {
                missing.Add(names[i]);
            }
        }

        if (missing.Count > 0)
        {
            using var writable = _registry.CreateUser();
            foreach (var name in missing)
            {
                writable?.WriteDword(name, 0);
            }
        }

        return values;
    }

    private (string Face, int Size) MachineDefaultFont()
    {
        var fallback = FontSpec.Default;
        using var key = _registry.Open(SettingsLocation.MachineDefaultFonts);
        if (key is null)
        {
            return (fallback.FaceName, fallback.PointSize10);
        }

        var face = key.TryReadString(SettingsSchema.FaceName, SettingsSchema.FaceNameCapacity, out var raw)
            ? TrimRegistryString(raw, SettingsSchema.FaceNameCapacity)
            : fallback.FaceName;
        var size = key.TryReadDword(SettingsSchema.PointSize, out var points) ? points : fallback.PointSize10;
        return (face, size);
    }

    /// <summary>
    /// A <c>REG_SZ</c> as the original's fixed buffer holds it: up to the first NUL, at most
    /// <paramref name="capacity"/> - 1 characters. <c>RegQueryValueExW</c> already refuses data that
    /// fills the buffer without a NUL (observed, Platform tests), so the cut is only a guard.
    /// </summary>
    public static string TrimRegistryString(string raw, int capacity)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var nul = raw.IndexOf('\0', StringComparison.Ordinal);
        var text = nul >= 0 ? raw[..nul] : raw;
        return text.Length >= capacity ? text[..(capacity - 1)] : text;
    }

    /// <summary>
    /// Port decision (parity.md): when <c>HKCU\Software\NotepadClassic</c> does not exist, every
    /// value of the original's key that the port reads, and that has the right kind and size, is
    /// copied into it, without asking. It runs once, since the key exists afterwards. Values the
    /// port does not use (<c>ShowStoreBanner</c>, <c>fSaveWindowPositions</c>,
    /// <c>fMLE_is_broken</c>) are left behind.
    /// </summary>
    /// <returns>Whether anything was imported.</returns>
    public bool ImportLegacy()
    {
        using (var existing = _registry.Open(SettingsLocation.User))
        {
            if (existing is not null)
            {
                return false;
            }
        }

        using var legacy = _registry.Open(SettingsLocation.Legacy);
        if (legacy is null)
        {
            return false;
        }

        var dwords = new List<(string, int)>();
        foreach (var name in SettingsSchema.ImportedDwords)
        {
            if (legacy.TryReadDword(name, out var value))
            {
                dwords.Add((name, value));
            }
        }

        var strings = new List<(string, string)>();
        foreach (var (name, capacity) in SettingsSchema.ImportedStrings)
        {
            if (legacy.TryReadString(name, capacity, out var raw))
            {
                strings.Add((name, TrimRegistryString(raw, capacity)));
            }
        }

        if (dwords.Count == 0 && strings.Count == 0)
        {
            return false;
        }

        using var user = _registry.CreateUser();
        if (user is null)
        {
            return false;
        }

        foreach (var (name, value) in dwords)
        {
            user.WriteDword(name, value);
        }

        foreach (var (name, value) in strings)
        {
            user.WriteString(name, value);
        }

        return true;
    }

    public void Save(NotepadSettings settings, WindowRect? window)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var moved = window is { } rect && rect != settings.WindowBaseline;
        if (!moved && !settings.FontChanged && !settings.ViewChanged && !settings.PageSetupChanged && !settings.SearchChanged)
        {
            return;
        }

        using var key = _registry.CreateUser();
        if (key is null)
        {
            return;
        }

        if (settings.FontChanged)
        {
            var f = settings.Font;
            key.WriteDword(SettingsSchema.Escapement, f.Escapement);
            key.WriteDword(SettingsSchema.Orientation, f.Orientation);
            key.WriteDword(SettingsSchema.Weight, f.Weight);
            key.WriteDword(SettingsSchema.Italic, f.Italic ? 1 : 0);
            key.WriteDword(SettingsSchema.Underline, f.Underline ? 1 : 0);
            key.WriteDword(SettingsSchema.StrikeOut, f.StrikeOut ? 1 : 0);
            key.WriteDword(SettingsSchema.CharSet, f.CharSet);
            key.WriteDword(SettingsSchema.OutPrecision, f.OutPrecision);
            key.WriteDword(SettingsSchema.ClipPrecision, f.ClipPrecision);
            key.WriteDword(SettingsSchema.Quality, f.Quality);
            key.WriteDword(SettingsSchema.PitchAndFamily, f.PitchAndFamily);
            key.WriteString(SettingsSchema.FaceName, Cut(f.FaceName, SettingsSchema.FaceNameCapacity));
            key.WriteDword(SettingsSchema.PointSize, f.PointSize10);
        }

        if (settings.ViewChanged)
        {
            key.WriteDword(SettingsSchema.Wrap, settings.WordWrap ? 1 : 0);
            key.WriteDword(SettingsSchema.StatusBar, settings.StatusBar ? 1 : 0);
            key.WriteDword(SettingsSchema.OpacityPercent, settings.OpacityPercent);
        }

        if (settings.PageSetupChanged)
        {
            key.WriteString(SettingsSchema.Header, Cut(settings.Header, SettingsSchema.HeaderCapacity));
            key.WriteString(SettingsSchema.Trailer, Cut(settings.Trailer, SettingsSchema.HeaderCapacity));
            key.WriteDword(SettingsSchema.MarginTop, settings.Margins.Top);
            key.WriteDword(SettingsSchema.MarginBottom, settings.Margins.Bottom);
            key.WriteDword(SettingsSchema.MarginLeft, settings.Margins.Left);
            key.WriteDword(SettingsSchema.MarginRight, settings.Margins.Right);
        }

        if (moved)
        {
            var r = window!.Value;
            key.WriteDword(SettingsSchema.WindowPosX, r.Left);
            key.WriteDword(SettingsSchema.WindowPosY, r.Top);
            key.WriteDword(SettingsSchema.WindowPosDX, r.Width);
            key.WriteDword(SettingsSchema.WindowPosDY, r.Height);
        }

        if (settings.SearchChanged)
        {
            key.WriteDword(SettingsSchema.Reverse, settings.Reverse ? 1 : 0);
            key.WriteDword(SettingsSchema.WrapAround, settings.WrapAround ? 1 : 0);
            key.WriteDword(SettingsSchema.MatchCase, settings.MatchCase ? 1 : 0);
            key.WriteString(SettingsSchema.SearchString, Cut(settings.SearchString, SettingsSchema.SearchCapacity));
            key.WriteString(SettingsSchema.ReplaceString, Cut(settings.ReplaceString, SettingsSchema.SearchCapacity));
        }
    }

    private static string Cut(string value, int capacity) => value.Length >= capacity ? value[..(capacity - 1)] : value;
}
