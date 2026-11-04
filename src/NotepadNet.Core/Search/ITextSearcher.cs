namespace NotepadNet.Core.Search;

/// <summary>
/// The match function of the original (<c>FindForwardNls</c>/<c>FindBackwardNls</c>):
/// <c>FindNLSString(LOCALE_USER_DEFAULT, FIND_FROMSTART or FIND_FROMEND | (matchCase ? 0 :
/// NORM_IGNORECASE), text, cch, pattern, -1, &amp;cchFound)</c>. The comparison is linguistic, so
/// the matched length can differ from the pattern length.
/// </summary>
public interface ITextSearcher
{
    /// <summary>Finds the first (or with <paramref name="fromEnd"/> the last) match inside
    /// <paramref name="text"/>.</summary>
    /// <param name="matchLength">Length of the match in <paramref name="text"/>, at least 1.</param>
    /// <returns>Index of the match, or -1. A match of length 0 counts as no match, as in the
    /// original (<c>cchFound &lt; 1</c>).</returns>
    int Find(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength);
}
