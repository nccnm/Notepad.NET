using System.Buffers;
using NotepadNet.Core.Editing;
using NotepadNet.Core.Search;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Text;

/// <summary>
/// <c>FindNLSString(LOCALE_USER_DEFAULT, FIND_FROMSTART or FIND_FROMEND | NORM_IGNORECASE unless
/// Match case)</c>, as <c>FindForwardNls</c>/<c>FindBackwardNls</c> call it.
/// The original passes -1 for the NUL-terminated pattern; the port passes its length, which is
/// the same for a pattern without NUL characters.
/// <para>
/// Speed (docs/performance.md, "Find"): <c>FindNLSString</c> scans about 2 ns per character until
/// it meets the first character outside ASCII in the source (or any in the pattern) and then
/// about 7 ns per character for the rest of the call. The port therefore splits the text into
/// <em>simple</em> stretches, made only of <see cref="SimpleCharProfile.SimpleChars"/>, and
/// <em>complex</em> runs, and searches each part with a method that provably returns what one
/// <c>FindNLSString</c> call over the whole text would:
/// </para>
/// <list type="bullet">
/// <item>A simple stretch is searched ordinally (case-insensitively unless Match case). This
/// equals the linguistic search when every simple character is one collation element that is
/// neither ignorable nor part of a contraction, and two simple characters compare equal exactly
/// when they are the same letter in either case (or identical, with Match case).
/// <see cref="SimpleCharProfile"/> checks these facts against <c>FindNLSString</c> itself for the
/// user's locale, once, over every pair of simple characters and every triple of letters; when
/// any check fails the fast path stays off and every search goes to the API.</item>
/// <item>A match that contains a complex character holds at most twice as many simple characters
/// as the pattern has: every collation element in it matches at least one pattern character, and
/// an element holds at most two simple characters (a simple character on its own, or a locale
/// contraction of at most three code units with a complex one, such as "dž" in Croatian). Such a
/// match therefore lies within the complex run widened by <c>2p + 2</c> characters on each side,
/// and that window is searched with <c>FindNLSString</c>. Every cut between regions has two
/// simple characters on both sides, so no element straddles it and the result inside a region is
/// the result inside the whole text.</item>
/// <item>Only a pattern made of simple characters takes the fast path: the bound above needs
/// every pattern character to be one element, and a pattern with other characters would switch
/// the API to its slow path anyway.</item>
/// </list>
/// </summary>
public sealed class NlsTextSearcher : ITextSearcher
{
    private readonly Lazy<SimpleCharProfile> _profile;

    public NlsTextSearcher()
        : this(fastPath: true)
    {
    }

    /// <summary>With <paramref name="fastPath"/> false every search goes straight to <c>FindNLSString</c>.</summary>
    internal NlsTextSearcher(bool fastPath)
    {
        _profile = new(fastPath ? SimpleCharProfile.Probe : () => SimpleCharProfile.Disabled, LazyThreadSafetyMode.ExecutionAndPublication);
        if (fastPath)
        {
            // The probe takes a few tens of milliseconds; it runs while the user is still far from
            // the Find dialog. The first search waits for it when it has not finished.
            _ = Task.Run(() => _profile.Value);
        }
    }

    /// <summary>Whether searches with a simple pattern use the fast path on this machine.</summary>
    public bool FastPathEnabled => _profile.Value.FastPathAllowed;

    public int Find(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength)
    {
        matchLength = 0;
        if (text.IsEmpty || pattern.IsEmpty)
        {
            // FindNLSString rejects empty strings with ERROR_INVALID_PARAMETER and returns -1.
            return -1;
        }

        if (_profile.Value.FastPathAllowed && !pattern.ContainsAnyExcept(SimpleCharProfile.SimpleChars))
        {
            return fromEnd ? FindBackward(text, pattern, matchCase, out matchLength) : FindForward(text, pattern, matchCase, out matchLength);
        }

        return FindNls(text, pattern, matchCase, fromEnd, out matchLength);
    }

    /// <summary>One <c>FindNLSString</c> call over <paramref name="text"/>, as the original makes it.</summary>
    internal static unsafe int FindNls(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength)
    {
        matchLength = 0;
        if (text.IsEmpty || pattern.IsEmpty)
        {
            return -1;
        }

        var flags = (fromEnd ? Kernel32Nls.FIND_FROMEND : Kernel32Nls.FIND_FROMSTART) | (matchCase ? 0 : Kernel32Nls.NORM_IGNORECASE);
        int found;
        int index;
        fixed (char* source = text)
        fixed (char* value = pattern)
        {
            index = Kernel32Nls.FindNLSString(Kernel32Nls.LOCALE_USER_DEFAULT, flags, source, text.Length, value, pattern.Length, &found);
        }

        if (index < 0 || found < 1)
        {
            return -1;
        }

        matchLength = found;
        return index;
    }

    private static StringComparison Ordinal(bool matchCase) => matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Longest collation element that can hold a simple character: a locale contraction
    /// of three code units. Every cut therefore keeps this many minus one simple characters on
    /// each side.</summary>
    private const int ContractionLength = 3;

    /// <summary>Simple characters a match with a complex character can reach beyond the complex
    /// run (two per element, one element per pattern character), plus the cut context.</summary>
    private static int Margin(int patternLength) => (2 * patternLength) + ContractionLength - 1;

    /// <summary>
    /// Simple text is looked at in blocks, starting at this many characters and doubling while
    /// the blocks stay simple, so that one call never scans far past its result (Replace All
    /// calls <see cref="Find"/> once per match from the previous match on, and a scan to the end
    /// of the text at each call would make it quadratic) while a search over a long simple text
    /// still spends its time in a few long vectorized calls rather than thousands of short ones,
    /// which the JIT would run unoptimized until tiering catches up.
    /// </summary>
    private const int BlockLength = 4096;

    /// <summary>First match: simple stretches and complex windows from left to right.</summary>
    private static int FindForward(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, out int matchLength)
    {
        var simple = SimpleCharProfile.SimpleChars;
        var comparison = Ordinal(matchCase);
        var n = text.Length;
        var p = pattern.Length;
        var margin = Margin(p);
        var i = 0;
        var blockLength = BlockLength;
        while (i < n)
        {
            var block = text[i..Math.Min(n, i + blockLength)];
            var firstComplex = block.IndexOfAnyExcept(simple);
            if (firstComplex < 0)
            {
                blockLength = Math.Min(blockLength * 2, int.MaxValue / 2);
                // A simple block. Unless it ends the text, a complex character may follow it, so
                // the last margin - p + 1 characters are left to the next round (with the usual
                // overlap of p - 1 for a match straddling the cut).
                var searchable = block.Length == n - i ? block.Length : block.Length - margin + p - 1;
                var k = block[..searchable].IndexOf(pattern, comparison);
                if (k >= 0)
                {
                    matchLength = p;
                    return i + k;
                }

                i += block.Length == n - i ? block.Length : searchable - p + 1;
                continue;
            }

            // The stretch before the window is context-free; a match reaching the complex run
            // starts inside the window. The stretch overlaps the window by p - 1 characters so
            // that a simple match straddling the window's edge is found by one of them.
            var complexStart = i + firstComplex;
            var windowStart = Math.Max(i, complexStart - margin);
            var stretchEnd = complexStart - margin + p - 1;
            if (stretchEnd > i)
            {
                var k = text[i..stretchEnd].IndexOf(pattern, comparison);
                if (k >= 0)
                {
                    matchLength = p;
                    return i + k;
                }
            }

            var runEnd = ComplexRunEnd(text, complexStart, margin + ContractionLength - 1);
            var windowEnd = Math.Min(n, runEnd + margin);
            var index = FindNls(text[windowStart..windowEnd], pattern, matchCase, fromEnd: false, out matchLength);
            if (index >= 0)
            {
                return windowStart + index;
            }

            // Simple matches that fit in the window's right margin were just searched; the next
            // stretch starts with the first one that could reach past the window.
            i = runEnd + margin - p + 1;
            blockLength = BlockLength;
        }

        matchLength = 0;
        return -1;
    }

    /// <summary>Last match: the same regions from right to left.</summary>
    private static int FindBackward(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, out int matchLength)
    {
        var simple = SimpleCharProfile.SimpleChars;
        var comparison = Ordinal(matchCase);
        var p = pattern.Length;
        var margin = Margin(p);
        var end = text.Length;
        var blockLength = BlockLength;
        while (end > 0)
        {
            var blockStart = Math.Max(0, end - blockLength);
            var block = text[blockStart..end];
            var lastInBlock = block.LastIndexOfAnyExcept(simple);
            if (lastInBlock < 0)
            {
                blockLength = Math.Min(blockLength * 2, int.MaxValue / 2);
                var skip = blockStart == 0 ? 0 : margin - p + 1;
                var k = block[skip..].LastIndexOf(pattern, comparison);
                if (k >= 0)
                {
                    matchLength = p;
                    return blockStart + skip + k;
                }

                end = blockStart == 0 ? 0 : blockStart + skip + p - 1;
                continue;
            }

            var lastComplex = blockStart + lastInBlock;
            var windowEnd = Math.Min(end, lastComplex + 1 + margin);
            var stretchStart = lastComplex + 1 + margin - p + 1;
            if (stretchStart < end)
            {
                var k = text[stretchStart..end].LastIndexOf(pattern, comparison);
                if (k >= 0)
                {
                    matchLength = p;
                    return stretchStart + k;
                }
            }

            var runStart = ComplexRunStart(text, lastComplex, margin + ContractionLength - 1);
            var windowStart = Math.Max(0, runStart - margin);
            var index = FindNls(text[windowStart..windowEnd], pattern, matchCase, fromEnd: true, out matchLength);
            if (index >= 0)
            {
                return windowStart + index;
            }

            end = runStart - margin + p - 1;
            blockLength = BlockLength;
        }

        matchLength = 0;
        return -1;
    }

    /// <summary>
    /// End (exclusive) of the complex run starting at <paramref name="start"/>, merged with any
    /// further complex run that begins less than <paramref name="reach"/> characters after it,
    /// so that the window's end has only simple characters around it.
    /// </summary>
    private static int ComplexRunEnd(ReadOnlySpan<char> text, int start, int reach)
    {
        var simple = SimpleCharProfile.SimpleChars;
        var position = start;
        while (true)
        {
            var nextSimple = text[position..].IndexOfAny(simple);
            var runEnd = nextSimple < 0 ? text.Length : position + nextSimple;
            var gapEnd = Math.Min(text.Length, runEnd + reach);
            var nextComplex = text[runEnd..gapEnd].IndexOfAnyExcept(simple);
            if (nextComplex < 0)
            {
                return runEnd;
            }

            position = runEnd + nextComplex;
        }
    }

    /// <summary>Start of the complex run ending at <paramref name="last"/> (inclusive), merged
    /// with any earlier run that ends less than <paramref name="reach"/> characters before it.</summary>
    private static int ComplexRunStart(ReadOnlySpan<char> text, int last, int reach)
    {
        var simple = SimpleCharProfile.SimpleChars;
        var position = last;
        while (true)
        {
            var previousSimple = text[..(position + 1)].LastIndexOfAny(simple);
            var runStart = previousSimple + 1;
            var gapStart = Math.Max(0, runStart - reach);
            var previousComplex = text[gapStart..runStart].LastIndexOfAnyExcept(simple);
            if (previousComplex < 0)
            {
                return runStart;
            }

            position = gapStart + previousComplex;
        }
    }
}

/// <summary>
/// What <c>FindNLSString</c> does with the simple characters under the user's locale, probed at
/// run time. The fast path of <see cref="NlsTextSearcher"/> is allowed only when, for both flag
/// sets, every simple character is one non-ignorable collation element, no pair of simple
/// characters and no triple of letters forms a contraction, and two simple characters are equal
/// exactly when ordinal (ignore-case) comparison says so.
/// </summary>
public sealed class SimpleCharProfile
{
    /// <summary>TAB, LF, CR and printable ASCII <c>0x20</c>-<c>0x7E</c>.</summary>
    public static SearchValues<char> SimpleChars { get; } = SearchValues.Create(SimpleCharList);

    internal const string SimpleCharList =
        "\t\n\r !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private SimpleCharProfile(bool fastPathAllowed, string? firstFailure)
    {
        FastPathAllowed = fastPathAllowed;
        FirstFailure = firstFailure;
    }

    public static SimpleCharProfile Disabled { get; } = new(false, "disabled");

    public bool FastPathAllowed { get; }

    /// <summary>The first check that failed, for diagnostics; null when all passed.</summary>
    public string? FirstFailure { get; }

    /// <summary>Runs every check with <c>FindNLSString</c> (about 180 000 calls, tens of milliseconds).</summary>
    public static SimpleCharProfile Probe() => Probe(NlsTextSearcher.FindNls);

    /// <summary>The signature of <c>FindNLSString</c> as the checks call it; tests pass a stand-in.</summary>
    internal delegate int FindFunction(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength);

    /// <summary>The checks against <paramref name="find"/>.</summary>
    internal static SimpleCharProfile Probe(FindFunction find)
    {
        var failure = CheckPairs(find, matchCase: false) ?? CheckPairs(find, matchCase: true) ?? CheckLetterTriples(find);
        return new(failure is null, failure);
    }

    private static bool Equivalent(char a, char b, bool matchCase) =>
        matchCase ? a == b : char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    /// <summary>
    /// For every ordered pair <c>ab</c>: <c>a</c> is found at 0 with length 1 (one element,
    /// not ignorable, <c>ab</c> is no contraction), and <c>b</c> is found at 1 with length 1, or
    /// at 0 when <c>a</c> and <c>b</c> are meant to be equal. Both directions of the API.
    /// </summary>
    private static string? CheckPairs(FindFunction find, bool matchCase)
    {
        Span<char> pair = stackalloc char[2];
        foreach (var a in SimpleCharList)
        {
            foreach (var b in SimpleCharList)
            {
                pair[0] = a;
                pair[1] = b;
                var equal = Equivalent(a, b, matchCase);
                foreach (var fromEnd in new[] { false, true })
                {
                    var first = find(pair, pair[..1], matchCase, fromEnd, out var firstLength);
                    var second = find(pair, pair[1..], matchCase, fromEnd, out var secondLength);
                    // From the end, the last match of a is at 1 when a and b are equal.
                    var expectedFirst = equal && fromEnd ? 1 : 0;
                    var expectedSecond = equal && !fromEnd ? 0 : 1;
                    if (first != expectedFirst || firstLength != 1 || second != expectedSecond || secondLength != 1)
                    {
                        return $"pair U+{(int)a:X4} U+{(int)b:X4} (match case {matchCase}, from end {fromEnd}): {first}/{firstLength} {second}/{secondLength}";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>For every letter triple <c>abc</c>: <c>b</c> is found at 1 (at 0 when it equals
    /// <c>a</c>) with length 1, so no three letters form a contraction.</summary>
    private static string? CheckLetterTriples(FindFunction find)
    {
        Span<char> triple = stackalloc char[3];
        foreach (var a in Letters)
        {
            foreach (var b in Letters)
            {
                foreach (var c in Letters)
                {
                    triple[0] = a;
                    triple[1] = b;
                    triple[2] = c;
                    var index = find(triple, triple[1..2], matchCase: false, fromEnd: false, out var length);
                    var expected = Equivalent(a, b, matchCase: false) ? 0 : 1;
                    if (index != expected || length != 1)
                    {
                        return $"triple {a}{b}{c}: {index}/{length}";
                    }
                }
            }
        }

        return null;
    }
}

/// <summary><c>FoldStringW(MAP_FOLDDIGITS)</c>. The original folds the Go To text in place; the
/// port uses a second buffer, which gives the same result for this one-to-one mapping.</summary>
public sealed class Win32DigitFolder : IDigitFolder
{
    public unsafe string FoldDigits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        fixed (char* source = text)
        {
            var size = Kernel32Nls.FoldString(Kernel32Nls.MAP_FOLDDIGITS, source, text.Length, null, 0);
            if (size <= 0)
            {
                return text;
            }

            var buffer = new char[size];
            fixed (char* destination = buffer)
            {
                var written = Kernel32Nls.FoldString(Kernel32Nls.MAP_FOLDDIGITS, source, text.Length, destination, size);
                return written > 0 ? new string(buffer, 0, written) : text;
            }
        }
    }
}
