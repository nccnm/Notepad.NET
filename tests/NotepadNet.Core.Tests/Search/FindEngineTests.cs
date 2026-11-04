using NotepadNet.Core.Editing;
using NotepadNet.Core.Search;
using NotepadNet.Core.Tests.Fakes;

namespace NotepadNet.Core.Tests.Search;

/// <summary>Search bounds of <c>FindNext</c> and the Replace helpers.</summary>
public sealed class FindEngineTests
{
    private static readonly FakeSearcher Searcher = new();

    private static FindMatch? Locate(string text, int start, int end, string pattern, bool backward, bool wrap = false, bool matchCase = false) =>
        FindEngine.Locate(text, new TextRange(start, end), pattern, backward, new SearchOptions(matchCase, wrap), Searcher);

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3, 3)]
    [InlineData(1, 1, 3)]
    [InlineData(3, 6, 6)]
    public void Down_searches_from_the_selection_end(int start, int end, int expected) =>
        Locate("abcabcabc", start, end, "abc", backward: false).Should().Be(new FindMatch(expected, 3, false));

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Down_stops_at_the_end_without_wrap_and_wraps_to_the_start_with_it()
    {
        Locate("abcabcabc", 6, 9, "abc", backward: false).Should().BeNull();
        Locate("abcabcabc", 6, 9, "abc", backward: false, wrap: true).Should().Be(new FindMatch(0, 3, true));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Down_wrap_range_overlaps_the_end_of_the_selection()
    {
        // [e, len) = "b" has no match; the wrap range is [0, e + (e - s)) = [0, 4), so "ab" at 2
        // is found although it runs past the selection end.
        Locate("xxab", 2, 3, "ab", backward: false, wrap: true).Should().Be(new FindMatch(2, 2, true));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Down_wrap_range_is_clamped_to_the_text()
    {
        // e + (e - s) = 6 + 6 is past the end; the original reads past the NUL, the port stops at it.
        Locate("abcdef", 0, 6, "zz", backward: false, wrap: true).Should().BeNull();
        Locate("xyabcd", 2, 6, "xy", backward: false, wrap: true).Should().Be(new FindMatch(0, 2, true));
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData(6, 9, 3)]
    [InlineData(3, 6, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(9, 9, 6)]
    [InlineData(7, 7, 3)]
    public void Up_searches_before_the_selection_end_minus_one(int start, int end, int expected) =>
        Locate("abcabcabc", start, end, "abc", backward: true).Should().Be(new FindMatch(expected, 3, false));

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Up_with_a_selection_can_overlap_the_selection()
    {
        // L = e - 1 = 2: [0, 2) holds "aa" at 0, which overlaps the selection [1, 3).
        Locate("aaa", 1, 3, "aa", backward: true).Should().Be(new FindMatch(0, 2, false));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Up_stops_at_the_start_without_wrap_and_wraps_from_the_end_with_it()
    {
        Locate("abcabcabc", 0, 3, "abc", backward: true).Should().BeNull();
        Locate("abcabcabc", 0, 3, "abc", backward: true, wrap: true).Should().Be(new FindMatch(6, 3, true));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Up_wrap_range_starts_before_the_last_position()
    {
        // L = e - 1 = 1: [0, 1) has no "ab"; the wrap range [L - (e - s), len) = [0, 2) finds it.
        Locate("ab", 1, 2, "ab", backward: true, wrap: true).Should().Be(new FindMatch(0, 2, true));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Selecting_everything_counts_as_a_caret_at_the_start()
    {
        Locate("abcabc", 0, 6, "abc", backward: false).Should().Be(new FindMatch(0, 3, false));
        Locate("abcabc", 0, 6, "abc", backward: true).Should().BeNull();
        Locate("abcabc", 0, 6, "abc", backward: true, wrap: true).Should().Be(new FindMatch(3, 3, true));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Match_case_is_passed_to_the_comparison()
    {
        Locate("ABC abc", 0, 0, "abc", backward: false, matchCase: true).Should().Be(new FindMatch(4, 3, false));
        Locate("ABC abc", 0, 0, "abc", backward: false).Should().Be(new FindMatch(0, 3, false));
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void The_match_length_comes_from_the_comparison_not_the_pattern() =>
        Locate("xa­bc", 0, 0, "ab", backward: false).Should().Be(new FindMatch(1, 3, false));

    [Fact]
    public void Empty_text_finds_nothing()
    {
        Locate("", 0, 0, "a", backward: false, wrap: true).Should().BeNull();
        Locate("", 0, 0, "a", backward: true, wrap: true).Should().BeNull();
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("xabcx", 1, 4, "abc", true)]
    [InlineData("xABCx", 1, 4, "abc", true)]
    [InlineData("xabcx", 1, 5, "abc", false)]
    [InlineData("xabcx", 0, 4, "abc", false)]
    [InlineData("xabcx", 1, 1, "abc", false)]
    [InlineData("a­b", 0, 3, "ab", true)]
    public void Selection_matches_only_when_it_is_exactly_one_match(string text, int start, int end, string pattern, bool expected) =>
        FindEngine.SelectionMatches(text, new(start, end), pattern, matchCase: false, Searcher).Should().Be(expected);

    [Fact]
    public void Selection_match_respects_match_case() =>
        FindEngine.SelectionMatches("xABCx", new(1, 4), "abc", matchCase: true, Searcher).Should().BeFalse();

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Matches_are_counted_without_overlap() =>
        FindEngine.CountMatches("aaaaa", "aa", matchCase: false, Searcher).Should().Be((2, 4L));

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("one two one", "one", "1", "1 two 1", 2)]
    [InlineData("a-b-c", "-", "", "abc", 2)]
    [InlineData("ONE one", "one", "two", "two two", 2)]
    [InlineData("x­y and xy", "xy", "Z", "Z and Z", 2)]
    [InlineData("a", "a", "aa", "aa", 1)]
    public void Replace_all_builds_the_whole_text(string text, string pattern, string replacement, string expected, int count)
    {
        FindEngine.BuildReplaceAll(text, pattern, replacement, matchCase: false, Searcher, out var n).Should().Be(expected);
        n.Should().Be(count);
    }

    [Fact]
    [Trait("Doc", "parity.md#tim-kiem")]
    public void Replace_all_without_a_match_builds_nothing()
    {
        FindEngine.BuildReplaceAll("abc", "x", "y", matchCase: false, Searcher, out var count).Should().BeNull();
        count.Should().Be(0);
    }

    [Fact]
    public void Replace_all_refuses_a_result_longer_than_a_string()
    {
        FindEngine.BuildReplaceAll("aaaa", "a", "bbb", matchCase: false, Searcher, maxLength: 11, out var count).Should().BeNull();
        count.Should().Be(4);
        FindEngine.BuildReplaceAll("aaaa", "a", "bbb", matchCase: false, Searcher, maxLength: 12, out _).Should().Be("bbbbbbbbbbbb");
    }
}
