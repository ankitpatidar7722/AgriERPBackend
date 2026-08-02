using AgriERP.Application.Common.Services;

namespace AgriERP.Application.Tests;

/// <summary>
/// The amount-in-words line on a tax invoice.
///
/// It uses the Indian lakh/crore grouping, not million/billion. A bill reading
/// "One Million Two Hundred Thousand" would be read as an error by every
/// customer who sees it, so the grouping is the thing worth pinning down.
/// </summary>
public class AmountToWordsTests
{
    [Theory]
    [InlineData(0, "Zero Rupees Only")]
    [InlineData(1, "One Rupees Only")]
    [InlineData(15, "Fifteen Rupees Only")]
    [InlineData(20, "Twenty Rupees Only")]
    [InlineData(21, "Twenty One Rupees Only")]
    [InlineData(100, "One Hundred Rupees Only")]
    [InlineData(101, "One Hundred One Rupees Only")]
    [InlineData(999, "Nine Hundred Ninety Nine Rupees Only")]
    public void Converts_small_amounts(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountToWords.Convert(amount));
    }

    [Theory]
    [InlineData(1000, "One Thousand Rupees Only")]
    [InlineData(1770, "One Thousand Seven Hundred Seventy Rupees Only")]
    [InlineData(35400, "Thirty Five Thousand Four Hundred Rupees Only")]
    [InlineData(99999, "Ninety Nine Thousand Nine Hundred Ninety Nine Rupees Only")]
    public void Converts_thousands(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountToWords.Convert(amount));
    }

    /// <summary>
    /// The Indian grouping. 100000 is one lakh, not "one hundred thousand";
    /// 10000000 is one crore, not "ten million".
    /// </summary>
    [Theory]
    [InlineData(100000, "One Lakh Rupees Only")]
    [InlineData(150000, "One Lakh Fifty Thousand Rupees Only")]
    [InlineData(1234567, "Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven Rupees Only")]
    [InlineData(10000000, "One Crore Rupees Only")]
    [InlineData(12345678, "One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Rupees Only")]
    public void Uses_lakh_and_crore_not_million(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountToWords.Convert(amount));
    }

    [Theory]
    [InlineData(1770.50, "One Thousand Seven Hundred Seventy Rupees and Fifty Paise Only")]
    [InlineData(0.75, "Seventy Five Paise Only")]
    [InlineData(1.01, "One Rupees and One Paise Only")]
    public void Includes_paise_when_present(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountToWords.Convert(amount));
    }

    /// <summary>
    /// Paise are rounded, not truncated. Truncating 0.996 would print "Zero
    /// Rupees" beside a figure of 1.00 - the words disagreeing with the number
    /// on the same invoice.
    /// </summary>
    [Fact]
    public void Rounds_paise_rather_than_truncating()
    {
        Assert.Equal("One Rupees Only", AmountToWords.Convert(0.996m));
        Assert.Equal("Ninety Nine Paise Only", AmountToWords.Convert(0.994m));
    }

    [Fact]
    public void Carries_into_rupees_when_paise_round_to_a_hundred()
    {
        // 99.999 -> 100 paise, which must become one more rupee rather than
        // printing an impossible "Hundred Paise".
        var words = AmountToWords.Convert(99.999m);

        Assert.Equal("One Hundred Rupees Only", words);
        Assert.DoesNotContain("Paise", words);
    }

    [Fact]
    public void Handles_a_negative_amount()
    {
        Assert.StartsWith("Minus", AmountToWords.Convert(-500m));
    }

    [Fact]
    public void Never_returns_an_empty_string()
    {
        // The invoice footer prints this verbatim; a blank line there looks
        // like a rendering fault rather than a zero.
        foreach (var amount in new[] { 0m, 0.004m, 1m, 999999999m })
            Assert.False(string.IsNullOrWhiteSpace(AmountToWords.Convert(amount)));
    }
}
