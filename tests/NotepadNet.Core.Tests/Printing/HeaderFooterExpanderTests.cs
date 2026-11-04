using NotepadNet.Core.Printing;

namespace NotepadNet.Core.Tests.Printing;

/// <summary>Header and footer codes (<c>ExpandHeaderFooterCodes</c>).</summary>
public sealed class HeaderFooterExpanderTests
{
    private const string Codes = "fFpPtTdDcCrRlL";
    private static readonly HeaderFooterValues Values = new("a.txt", 3, "2:44:00 PM", "Thursday, October 29, 2026");

    private static HeaderFooterColumns Expand(string pattern, HeaderFooterValues? values = null) =>
        HeaderFooterExpander.Expand(pattern, Codes, values ?? Values);

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("&f", "a.txt")]
    [InlineData("&F", "a.txt")]
    [InlineData("&p", "3")]
    [InlineData("&P", "3")]
    [InlineData("&t", "2:44:00 PM")]
    [InlineData("&T", "2:44:00 PM")]
    [InlineData("&d", "Thursday, October 29, 2026")]
    [InlineData("&D", "Thursday, October 29, 2026")]
    [InlineData("Page &p of &f", "Page 3 of a.txt")]
    public void Codes_expand_in_either_case_into_the_center_column(string pattern, string center) =>
        Expand(pattern).Should().Be(new HeaderFooterColumns("", center, ""));

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("&lL&cC&rR", "L", "C", "R")]
    [InlineData("&LL&CC&RR", "L", "C", "R")]
    [InlineData("&lA&cB&lC", "AC", "B", "")]
    [InlineData("mid&rright&lleft", "left", "mid", "right")]
    public void Alignment_codes_switch_columns_and_text_appends(string pattern, string left, string center, string right) =>
        Expand(pattern).Should().Be(new HeaderFooterColumns(left, center, right));

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("&p+10", "13")]
    [InlineData("&p+0", "3")]
    [InlineData("&p+", "3")]
    [InlineData("&p+2x", "5x")]
    [InlineData("&p-1", "3-1")]
    [InlineData("&p+٣", "3٣")]
    public void Page_plus_n_adds_the_decimal_digits_after_the_plus(string pattern, string center) =>
        Expand(pattern).Center.Should().Be(center);

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Page_offsets_wrap_like_32_bit_integers() =>
        Expand("&p+4294967296", Values with { Page = 1 }).Center.Should().Be("1");

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("a && b", "a & b")]
    [InlineData("&&&p", "&3")]
    [InlineData("x&qy", "xy")]
    [InlineData("&!&9", "")]
    public void Double_ampersand_is_one_and_unknown_codes_drop_both_characters(string pattern, string center) =>
        Expand(pattern).Center.Should().Be(center);

    [Theory]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    [InlineData("Page &", "Page ")]
    [InlineData("&", "")]
    public void A_trailing_ampersand_is_dropped_instead_of_reading_past_the_end(string pattern, string center) =>
        Expand(pattern).Center.Should().Be(center);

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void An_expansion_that_would_reach_256_characters_is_dropped_whole()
    {
        var title = new string('t', 250);
        var values = Values with { FileTitle = title };

        Expand("&f&f", values).Center.Should().Be(title);
        Expand("&f12345", values).Center.Should().Be(title + "12345", "255 characters fit");
        Expand("&f1234&p", values).Center.Should().Be(title + "1234" + "3", "254 + 1 = 255 still fits");
        Expand("&f12345&p", values).Center.Should().Be(title + "12345", "255 + 1 would be 256");
        Expand("&f12345&&", values).Center.Should().Be(title + "12345");
        Expand("&f&l&f", values).Should().Be(new HeaderFooterColumns(title, title, ""), "each column has its own limit");
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Plain_characters_stop_at_255_instead_of_overrunning_the_column()
    {
        var values = Values with { FileTitle = new string('t', 254) };

        Expand("&fabc&rR", values).Should().Be(new HeaderFooterColumns("", new string('t', 254) + "a", "R"));
    }

    [Fact]
    [Trait("Doc", "parity.md#in-va-page-setup")]
    public void Code_letters_come_from_the_localized_string()
    {
        const string german = "dDsSzZaAmMrRlL";

        HeaderFooterExpander.Expand("&d &s &a &f", german, Values).Center.Should().Be("a.txt 3 Thursday, October 29, 2026 ");
        HeaderFooterExpander.Expand("&mM&rR", german, Values).Should().Be(new HeaderFooterColumns("", "M", "R"));
        HeaderFooterExpander.Expand("&x", Codes + "xX", Values).Center.Should().BeEmpty("only the first 14 letters are codes");
    }
}
