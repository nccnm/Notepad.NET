using NotepadNet.Core.Editing;

namespace NotepadNet.Core.Search;

/// <summary>A match: <c>[Start, Start + Length)</c>. <paramref name="Wrapped"/> is set when it was
/// found only after wrapping around the end (or the start) of the text.</summary>
public readonly record struct FindMatch(int Start, int Length, bool Wrapped)
{
    public int End => Start + Length;
}

/// <summary>Search options of one request: the Match case and Wrap around boxes.</summary>
public readonly record struct SearchOptions(bool MatchCase, bool WrapAround);

/// <summary>
/// The search bounds of the original, independent of the editor (<c>FindNext</c>,
/// <c>ReplaceSelectionIfMatch</c>, <c>ReplaceAll</c>, <c>BuildReplaceAllBuffer</c>).
/// </summary>
public static class FindEngine
{
    /// <summary>
    /// <c>FindNext</c> with selection <c>[s, e)</c>. A selection of the whole text counts as
    /// <c>e = 0</c>. Down searches <c>[e, len)</c>, then with wrap <c>[0, e + (e - s))</c>. Up
    /// searches <c>[0, L)</c> from the end where <c>L = e - 1</c> with a selection and <c>e</c>
    /// without, then with wrap <c>[L - (e - s), len)</c>. Deliberate difference (parity.md): the
    /// wrap ranges are clamped to the text; the original can read one character before the
    /// buffer and past its terminating NUL.
    /// </summary>
    public static FindMatch? Locate(
        ReadOnlySpan<char> text, TextRange selection, ReadOnlySpan<char> pattern, bool backward, SearchOptions options, ITextSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        var length = text.Length;
        var start = Math.Clamp(selection.Start, 0, length);
        var end = Math.Clamp(selection.End, 0, length);
        if (start == 0 && end == length)
        {
            end = 0;
        }

        var selected = Math.Max(end - start, 0);
        int index;
        int found;
        if (!backward)
        {
            index = searcher.Find(text[end..], pattern, options.MatchCase, fromEnd: false, out found);
            if (index >= 0)
            {
                return new(end + index, found, Wrapped: false);
            }

            if (!options.WrapAround)
            {
                return null;
            }

            var limit = Math.Min(end + selected, length);
            index = searcher.Find(text[..limit], pattern, options.MatchCase, fromEnd: false, out found);
            return index >= 0 ? new(index, found, Wrapped: true) : null;
        }

        var last = start != end ? end - 1 : end;
        index = searcher.Find(text[..last], pattern, options.MatchCase, fromEnd: true, out found);
        if (index >= 0)
        {
            return new(index, found, Wrapped: false);
        }

        if (!options.WrapAround)
        {
            return null;
        }

        var from = Math.Max(last - selected, 0);
        index = searcher.Find(text[from..], pattern, options.MatchCase, fromEnd: true, out found);
        return index >= 0 ? new(from + index, found, Wrapped: true) : null;
    }

    /// <summary>
    /// Whether the selection is exactly one match: the pattern matches at the selection start and
    /// the match covers the whole selection. An empty selection never matches.
    /// </summary>
    public static bool SelectionMatches(ReadOnlySpan<char> text, TextRange selection, ReadOnlySpan<char> pattern, bool matchCase, ITextSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        var start = Math.Clamp(selection.Start, 0, text.Length);
        var end = Math.Clamp(selection.End, start, text.Length);
        var index = searcher.Find(text[start..end], pattern, matchCase, fromEnd: false, out var found);
        return index == 0 && found == end - start;
    }

    /// <summary>
    /// Counts the non-overlapping matches from the start of the text, each search continuing at
    /// the end of the previous match, and the total length they cover.
    /// </summary>
    public static (int Count, long MatchedLength) CountMatches(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, ITextSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        var count = 0;
        long matched = 0;
        var position = 0;
        while (position < text.Length)
        {
            var index = searcher.Find(text[position..], pattern, matchCase, fromEnd: false, out var found);
            if (index < 0)
            {
                break;
            }

            count++;
            matched += found;
            position += index + found;
        }

        return (count, matched);
    }

    /// <summary>The longest string .NET can allocate, in characters.</summary>
    public const int MaxStringLength = 0x3FFF_FFDF;

    /// <summary>
    /// <c>BuildReplaceAllBuffer</c>: the whole text with every match of
    /// <see cref="CountMatches"/> replaced, built in one pass. Null when there is no match or the
    /// result would be longer than a string can be.
    /// </summary>
    public static string? BuildReplaceAll(
        ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, ReadOnlySpan<char> replacement, bool matchCase, ITextSearcher searcher, out int count) =>
        BuildReplaceAll(text, pattern, replacement, matchCase, searcher, MaxStringLength, out count);

    internal static string? BuildReplaceAll(
        ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, ReadOnlySpan<char> replacement, bool matchCase, ITextSearcher searcher, int maxLength, out int count)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        (count, var matched) = CountMatches(text, pattern, matchCase, searcher);
        if (count == 0)
        {
            return null;
        }

        var total = text.Length - matched + (long)count * replacement.Length;
        if (total > maxLength)
        {
            return null;
        }

        var state = new BuildState(text, pattern, replacement, matchCase, searcher, count);
        return string.Create((int)total, state, static (destination, s) =>
        {
            var source = s.Text;
            var written = 0;
            for (var i = 0; i < s.Count; i++)
            {
                var index = s.Searcher.Find(source, s.Pattern, s.MatchCase, fromEnd: false, out var found);
                source[..index].CopyTo(destination[written..]);
                written += index;
                s.Replacement.CopyTo(destination[written..]);
                written += s.Replacement.Length;
                source = source[(index + found)..];
            }

            source.CopyTo(destination[written..]);
        });
    }

    private readonly ref struct BuildState(
        ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, ReadOnlySpan<char> replacement, bool matchCase, ITextSearcher searcher, int count)
    {
        public ReadOnlySpan<char> Text { get; } = text;

        public ReadOnlySpan<char> Pattern { get; } = pattern;

        public ReadOnlySpan<char> Replacement { get; } = replacement;

        public bool MatchCase { get; } = matchCase;

        public ITextSearcher Searcher { get; } = searcher;

        public int Count { get; } = count;
    }
}
