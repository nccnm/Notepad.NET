using NotepadNet.Core.Services;

namespace NotepadNet.Core.Editing;

/// <summary>
/// Edit > Time/Date (F5) and the <c>.LOG</c> stamp:
/// short time without seconds, a space, short date; the <c>.LOG</c> form adds CRLF on both
/// sides. The text replaces the selection with undo.
/// </summary>
public sealed class TimeDateInserter(IClock clock, IDateTimeFormatter formatter)
{
    private readonly IClock _clock = clock;
    private readonly IDateTimeFormatter _formatter = formatter;

    public string Stamp(bool forLog)
    {
        var now = _clock.LocalNow;
        var stamp = _formatter.FormatShortTime(now) + " " + _formatter.FormatShortDate(now);
        return forLog ? "\r\n" + stamp + "\r\n" : stamp;
    }

    public void Insert(IEditorSurface editor, bool forLog)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.ReplaceSelection(Stamp(forLog), canUndo: true);
    }
}

/// <summary>Reading-order flag for <c>GetDateFormat</c>.</summary>
public static class DateFlagRules
{
    public const uint ShortDate = 0x1;
    public const uint LtrReading = 0x10;
    public const uint RtlReading = 0x20;

    private const int CalendarGregorian = 1;

    /// <summary>Hijri 6, Hebrew 8, Gregorian Arabic 10, Um Al Qura 23 (mask <c>0x800540</c>).</summary>
    private static readonly int[] RtlCalendars = [6, 8, 10, 23];

    /// <param name="userLocaleIsRtl"><c>IsRtlLocale(LOCALE_USER_DEFAULT)</c>: bit 123 of the font signature.</param>
    /// <param name="calendarType"><c>LOCALE_ICALENDARTYPE</c>; only read when the locale is RTL.</param>
    /// <param name="uiLanguageIsRtl">Evaluated lazily: <c>IsRtlLocale(GetUserDefaultUILanguage())</c>.</param>
    public static uint Select(bool userLocaleIsRtl, Func<int> calendarType, Func<bool> uiLanguageIsRtl)
    {
        ArgumentNullException.ThrowIfNull(calendarType);
        ArgumentNullException.ThrowIfNull(uiLanguageIsRtl);
        if (!userLocaleIsRtl)
        {
            return ShortDate;
        }

        var calendar = calendarType();
        var rtl = calendar == CalendarGregorian ? uiLanguageIsRtl() : Array.IndexOf(RtlCalendars, calendar) >= 0;
        return ShortDate | (rtl ? RtlReading : LtrReading);
    }
}
