using System.Globalization;
using System.Text;

namespace NotepadNet.Core.Printing;

/// <summary>Values the header and footer codes expand to, fixed for one print job.</summary>
/// <param name="FileTitle">"Untitled", or the file name as <c>GetFileTitleW</c> gives it.</param>
/// <param name="Page">Current page, from 1.</param>
/// <param name="Time">Time the job started, <c>GetTimeFormatW(LOCALE_USER_DEFAULT, 0)</c>.</param>
/// <param name="Date">Date the job started, <c>GetDateFormatW(LOCALE_USER_DEFAULT, DATE_LONGDATE)</c>.</param>
public readonly record struct HeaderFooterValues(string FileTitle, int Page, string Time, string Date);

/// <summary>The three columns one header or footer line is drawn in.</summary>
public readonly record struct HeaderFooterColumns(string Left, string Center, string Right);

/// <summary>
/// Expands the <c>&amp;x</c> codes of a header or footer.
/// The code letters come from localized string
/// 45 (en-US <c>fFpPtTdDcCrRlL</c>): pairs for file, page, time, date, center, right, left.
/// Text before any alignment code goes to the center column.
/// </summary>
public static class HeaderFooterExpander
{
    /// <summary>Each column is a 256-WCHAR buffer: at most 255 characters.</summary>
    public const int ColumnCapacity = 256;

    private const int MaxColumnLength = ColumnCapacity - 1;

    private enum Code
    {
        File,
        Page,
        Time,
        Date,
        Center,
        Right,
        Left,
    }

    /// <summary>
    /// Rules kept from the original: an expansion (<c>&amp;f</c>, <c>&amp;p</c>, <c>&amp;t</c>,
    /// <c>&amp;d</c>, <c>&amp;&amp;</c>) goes in only when the column stays under 256 characters,
    /// otherwise the whole expansion is dropped; <c>&amp;p+N</c> prints the page plus the decimal
    /// digits after <c>+</c>; <c>&amp;</c> followed by any other character drops both.
    /// Deliberate fixes (parity.md): a trailing <c>&amp;</c> is dropped instead of reading past the
    /// end of the string, and plain characters stop at 255 per column instead of overrunning it.
    /// </summary>
    public static HeaderFooterColumns Expand(string pattern, string codeLetters, HeaderFooterValues values)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(codeLetters);
        var columns = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        var column = columns[1];
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c != '&')
            {
                if (column.Length < MaxColumnLength)
                {
                    column.Append(c);
                }

                continue;
            }

            if (++i == pattern.Length)
            {
                break;
            }

            var next = pattern[i];
            if (next == '&')
            {
                Insert(column, "&");
                continue;
            }

            var index = codeLetters.IndexOf(next, StringComparison.Ordinal);
            if (index < 0 || index / 2 > (int)Code.Left)
            {
                continue;
            }

            switch ((Code)(index / 2))
            {
                case Code.File:
                    Insert(column, values.FileTitle);
                    break;
                case Code.Page:
                    var page = values.Page;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '+')
                    {
                        i++;
                        var offset = 0;
                        while (i + 1 < pattern.Length && char.IsAsciiDigit(pattern[i + 1]))
                        {
                            i++;
                            offset = unchecked((offset * 10) + (pattern[i] - '0'));
                        }

                        page = unchecked(page + offset);
                    }

                    Insert(column, page.ToString(CultureInfo.InvariantCulture));
                    break;
                case Code.Time:
                    Insert(column, values.Time);
                    break;
                case Code.Date:
                    Insert(column, values.Date);
                    break;
                case Code.Center:
                    column = columns[1];
                    break;
                case Code.Right:
                    column = columns[2];
                    break;
                default:
                    column = columns[0];
                    break;
            }
        }

        return new(columns[0].ToString(), columns[1].ToString(), columns[2].ToString());
    }

    private static void Insert(StringBuilder column, string text)
    {
        if (column.Length + text.Length < ColumnCapacity)
        {
            column.Append(text);
        }
    }
}
