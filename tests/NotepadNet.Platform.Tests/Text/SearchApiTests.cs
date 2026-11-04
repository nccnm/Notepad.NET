using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Text;

/// <summary>
/// <c>FindNLSString</c> and <c>FoldStringW</c> as the port calls them (search and
/// <c>GoToDlgProc</c>). The comparison follows the user locale of the
/// machine; the cases below give the same result for the common Latin-script locales.
/// </summary>
public sealed class SearchApiTests
{
    private static readonly NlsTextSearcher Searcher = new();

    private static (int Index, int Length) Find(string text, string pattern, bool matchCase = false, bool fromEnd = false)
    {
        var index = Searcher.Find(text, pattern, matchCase, fromEnd, out var length);
        return (index, length);
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("Tiếng Việt", "TIẾNG", false, 0, 5)]
    [InlineData("Tiếng Việt", "việt", false, 6, 4)]
    [InlineData("Tiếng Việt", "tieng", false, -1, 0)]
    [InlineData("Tiếng Việt", "TIẾNG", true, -1, 0)]
    [InlineData("Tiếng Việt", "Việt", true, 6, 4)]
    [InlineData("ΣΊΣΥΦΟΣ", "σίσυφος", false, 0, 7)]
    [InlineData("ÄÖÜ äöü", "äöü", false, 0, 3)]
    [InlineData("ＡＢＣ", "abc", false, -1, 0)]
    public void Ignore_case_ignores_case_but_not_accents_or_width(string text, string pattern, bool matchCase, int index, int length) =>
        Find(text, pattern, matchCase).Should().Be((index, length));

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Combining_and_precomposed_forms_match_with_their_own_length()
    {
        Find("café x", "café").Should().Be((0, 5));
        Find("café x", "café").Should().Be((0, 4));
        Find("Việt", "Việt").Should().Be((0, 6));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Ignorable_characters_inside_a_match_count_in_its_length() =>
        Find("xab­c", "abc").Should().Be((1, 4));

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Sharp_s_matches_double_s_with_its_own_length()
    {
        // Observed 2026-10-29 with user locale en-US: the match "Straße" is 6 characters long
        // although the pattern has 7.
        Find("Straße", "STRASSE").Should().Be((0, 6));
        Find("Strasse", "straße").Should().Be((0, 7));
        Find("Straße", "STRASSE", matchCase: true).Should().Be((-1, 0));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void From_end_finds_the_last_match()
    {
        Find("abc abc abc", "ABC", fromEnd: true).Should().Be((8, 3));
        Find("abc abc abc", "ABC").Should().Be((0, 3));
    }

    [Fact]
    public void Empty_text_or_pattern_finds_nothing()
    {
        Find("", "a").Should().Be((-1, 0));
        Find("a", "").Should().Be((-1, 0));
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("123", "123")]
    [InlineData("١٢٣", "123")]
    [InlineData("۴۵", "45")]
    [InlineData("१२", "12")]
    [InlineData("７８", "78")]
    [InlineData("12a٣", "12a3")]
    [InlineData("", "")]
    public void Digits_of_any_script_fold_to_ascii(string text, string expected) =>
        new Win32DigitFolder().FoldDigits(text).Should().Be(expected);
}
