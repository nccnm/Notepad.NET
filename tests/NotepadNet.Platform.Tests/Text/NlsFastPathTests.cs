using System.Text;
using NotepadNet.Platform.Text;

namespace NotepadNet.Platform.Tests.Text;

/// <summary>
/// The fast path of <see cref="NlsTextSearcher"/> (docs/performance.md, "Find") must return
/// exactly what one <c>FindNLSString</c> call over the whole text returns: index and matched
/// length, forward and backward, with and without Match case. Every case here is checked against
/// the real API on this machine.
/// </summary>
public sealed class NlsFastPathTests
{
    private static readonly NlsTextSearcher Fast = new();
    private static readonly NlsTextSearcher Plain = new(fastPath: false);

    private static void AssertSameAsApi(string text, string pattern, string because = "")
    {
        foreach (var matchCase in new[] { false, true })
        {
            foreach (var fromEnd in new[] { false, true })
            {
                var expectedIndex = NlsTextSearcher.FindNls(text, pattern, matchCase, fromEnd, out var expectedLength);
                var plainIndex = Plain.Find(text, pattern, matchCase, fromEnd, out var plainLength);
                var fastIndex = Fast.Find(text, pattern, matchCase, fromEnd, out var fastLength);
                var label = $"{Show(text)} / {Show(pattern)} matchCase={matchCase} fromEnd={fromEnd} {because}";
                (plainIndex, plainLength).Should().Be((expectedIndex, expectedLength), "the plain searcher is the API: " + label);
                (fastIndex, fastLength).Should().Be((expectedIndex, expectedLength), "the fast path must equal the API: " + label);
            }
        }
    }

    private static string Show(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            b.Append(c is >= ' ' and < '\x7f' ? c.ToString() : $"\\u{(int)c:X4}");
        }

        return b.Append('"').ToString();
    }

    [Fact]
    public void The_user_locale_of_this_machine_allows_the_fast_path()
    {
        var profile = SimpleCharProfile.Probe();
        profile.FirstFailure.Should().BeNull("the test machines run a locale without contractions among ASCII characters");
        profile.FastPathAllowed.Should().BeTrue();
        Fast.FastPathEnabled.Should().BeTrue();
        Plain.FastPathEnabled.Should().BeFalse();
    }

    [Fact]
    public void The_probe_refuses_a_locale_where_two_simple_characters_form_a_contraction()
    {
        // "ch" as one letter, as in Czech: neither c nor h is found inside it.
        var profile = SimpleCharProfile.Probe((text, pattern, matchCase, fromEnd, out length) =>
        {
            if (text.Length >= 2 && text.IndexOf("ch", StringComparison.Ordinal) >= 0 && pattern.Length == 1 && pattern[0] is 'c' or 'h')
            {
                length = 0;
                return -1;
            }

            return NlsTextSearcher.FindNls(text, pattern, matchCase, fromEnd, out length);
        });

        profile.FastPathAllowed.Should().BeFalse();
        profile.FirstFailure.Should().StartWith("pair U+0063 U+0068");
    }

    [Fact]
    public void The_probe_refuses_an_ignorable_simple_character()
    {
        // A hyphen that the comparison skips: "a-" finds "a" but the hyphen is not found alone.
        var profile = SimpleCharProfile.Probe((text, pattern, matchCase, fromEnd, out length) =>
        {
            if (pattern.Length == 1 && pattern[0] == '-')
            {
                length = 0;
                return 0;
            }

            return NlsTextSearcher.FindNls(text, pattern, matchCase, fromEnd, out length);
        });

        profile.FastPathAllowed.Should().BeFalse();
        profile.FirstFailure.Should().Contain("U+002D");
    }

    [Fact]
    public void The_probe_refuses_a_three_letter_contraction_whose_pairs_are_fine()
    {
        var profile = SimpleCharProfile.Probe((text, pattern, matchCase, fromEnd, out length) =>
        {
            if (text.Length == 3 && text.Equals("dzs", StringComparison.Ordinal) && pattern.Length == 1)
            {
                length = 0;
                return -1;
            }

            return NlsTextSearcher.FindNls(text, pattern, matchCase, fromEnd, out length);
        });

        profile.FastPathAllowed.Should().BeFalse();
        profile.FirstFailure.Should().StartWith("triple dzs");
    }

    [Fact]
    public void The_probe_refuses_equality_beyond_case_folding()
    {
        // A locale where "v" and "w" are the same letter.
        var profile = SimpleCharProfile.Probe((text, pattern, matchCase, fromEnd, out length) =>
        {
            if (text.Length == 2 && pattern.Length == 1 && text[0] is 'v' && pattern[0] is 'w')
            {
                length = 1;
                return 0;
            }

            return NlsTextSearcher.FindNls(text, pattern, matchCase, fromEnd, out length);
        });

        profile.FastPathAllowed.Should().BeFalse();
        profile.FirstFailure.Should().StartWith("pair U+0076 U+0077");
    }

    [Theory]
    [Trait("Doc", "parity.md#tim-kiem")]
    [InlineData("Tiếng Việt", "ng")]
    [InlineData("Tiếng Việt", "t")]
    [InlineData("Tiếng Việt tiếng việt", "Vi")]
    [InlineData("the quick brown fox tiếng Việt jumps over zqxj the lazy dog", "zqxj")]
    [InlineData("Straße", "strasse")]
    [InlineData("Straße", "sse")]
    [InlineData("Strasse Straße", "e st")]
    [InlineData("xab­c", "abc")]
    [InlineData("­abc", "abc")]
    [InlineData("abc­", "abc")]
    [InlineData("ab­­­c", "abc")]
    [InlineData("a­b a­b", "ab")]
    [InlineData("café x", "cafe")]
    [InlineData("café x", "e x")]
    [InlineData("café x", "e")]
    [InlineData("İstanbul istanbul", "istanbul")]
    [InlineData("ΣΊΣΥΦΟΣ sisyphos", "sisyphos")]
    [InlineData("xᵃbc", "abc")]
    [InlineData("ᵃbc", "abc")]
    [InlineData("abᶜ", "abc")]
    [InlineData("Kelvin kelvin", "kelvin")]
    [InlineData("K", "k")]
    [InlineData("a‍b", "ab")]
    [InlineData("😀x", "x")]
    [InlineData("x😀", "x")]
    [InlineData("ab", "ab")]
    [InlineData("abcabc", "abc")]
    [InlineData("ab­cab­c", "abc")]
    [InlineData("z", "zqxj")]
    [InlineData("ế", "e")]
    [InlineData("ếế", "ếế")]
    [InlineData("džadž", "d")]
    [InlineData("aaáaaa", "aa")]
    [InlineData("--­--", "--")]
    [InlineData("a\tb\r\nc ế a\tb\r\nc", "b\r\nc")]
    public void Edge_cases_match_the_api(string text, string pattern) => AssertSameAsApi(text, pattern);

    [Fact]
    public void Complex_runs_at_every_distance_match_the_api()
    {
        // Two complex characters separated by 0 to 12 simple characters, patterns of 1 to 5
        // characters placed everywhere around them, at the start, middle and end of the text.
        foreach (var gap in Enumerable.Range(0, 13))
        {
            var middle = "ab" + new string('x', gap);
            foreach (var text in new[]
            {
                $"ế{middle}ệ", $"abc ế{middle}ệ abc", $"ế{middle}ệ abc", $"abc ế{middle}ệ",
                $"­{middle}­", $"abcab­{middle}́abcab", $"ß{middle}ß", $"ababᵃ{middle}Kabab",
            })
            {
                foreach (var pattern in new[] { "a", "ab", "abc", "x", "xx", "xa", "bx", "cab", "abcab", "ba", "xxxxx", " ", " a" })
                {
                    AssertSameAsApi(text, pattern, $"gap {gap}");
                }
            }
        }
    }

    private const string SimpleAlphabet = "abcABxs- \t";
    private static readonly string[] ComplexPieces =
    [
        "ế", "ệ", "ß", "­", "́", "ᵃ", "K", "İ", "ı", "σ", "Σ", "ς", "‍", "😀", "ž", "æ", "ﬁ", "\u0001", "ｓ", "゙",
    ];

    [Fact]
    public void Random_texts_match_the_api()
    {
        var random = new Random(20261030);
        var builder = new StringBuilder();
        for (var round = 0; round < 6000; round++)
        {
            builder.Clear();
            var length = random.Next(0, round % 10 == 0 ? 400 : 60);
            var complexRate = random.Next(1, 6);
            while (builder.Length < length)
            {
                if (random.Next(10) < complexRate)
                {
                    builder.Append(ComplexPieces[random.Next(ComplexPieces.Length)]);
                }
                else
                {
                    builder.Append(SimpleAlphabet[random.Next(SimpleAlphabet.Length)]);
                }
            }

            var text = builder.ToString();
            var patternLength = random.Next(1, 7);
            var pattern = string.Create(patternLength, random, (span, r) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = SimpleAlphabet[r.Next(SimpleAlphabet.Length)];
                }
            });
            AssertSameAsApi(text, pattern, $"round {round}");
            // The pattern taken from the text itself, so matches are frequent.
            if (text.Length >= patternLength)
            {
                var start = random.Next(0, text.Length - patternLength + 1);
                var fromText = text.Substring(start, patternLength);
                if (!fromText.AsSpan().ContainsAnyExcept(SimpleCharProfile.SimpleChars))
                {
                    AssertSameAsApi(text, fromText, $"round {round}, pattern from the text");
                }
            }
        }
    }

    [Fact]
    public void A_large_mixed_text_matches_the_api_for_present_and_absent_patterns()
    {
        var random = new Random(7);
        var builder = new StringBuilder(2_000_000);
        while (builder.Length < 2_000_000)
        {
            var word = random.Next(50) == 0 ? ComplexPieces[random.Next(ComplexPieces.Length)] : new string((char)('a' + random.Next(26)), random.Next(1, 8));
            builder.Append(word).Append(random.Next(12) == 0 ? "\r\n" : " ");
        }

        var text = builder.ToString();
        foreach (var pattern in new[] { "zzzzzzzzzz", "qqq", "e", "aa\r\n", "ss", "k", "s", "i", "fi", "ae" })
        {
            AssertSameAsApi(text, pattern);
        }
    }

    [Fact]
    public void Patterns_with_complex_characters_go_through_the_api_unchanged()
    {
        AssertSameAsApi("Straße strasse", "ß");
        AssertSameAsApi("Tiếng Việt", "việt");
        AssertSameAsApi("abc", "ab­c");
    }

    [Fact]
    public void The_fast_path_allocates_nothing()
    {
        var text = string.Concat(Enumerable.Repeat("the quick brown fox tiếng Việt jumps over the lazy dog ", 2000));
        _ = Fast.Find(text, "zqxj", matchCase: false, fromEnd: false, out _);
        var best = long.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            _ = Fast.Find(text, "zqxj", matchCase: false, fromEnd: false, out _);
            _ = Fast.Find(text, "zqxj", matchCase: false, fromEnd: true, out _);
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        best.Should().Be(0);
    }
}
