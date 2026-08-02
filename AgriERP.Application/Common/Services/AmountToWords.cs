using System.Text;

namespace AgriERP.Application.Common.Services;

/// <summary>
/// Spells a rupee amount for the invoice footer.
///
/// Uses the Indian system - thousand, lakh, crore - not the international
/// million/billion grouping. A bill reading "One Million Two Hundred Thousand"
/// would be read as an error by every customer who sees it.
/// </summary>
public static class AmountToWords
{
    private static readonly string[] Ones =
    {
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    public static string Convert(decimal amount)
    {
        if (amount < 0) return "Minus " + Convert(-amount);
        if (amount == 0) return "Zero Rupees Only";

        var rupees = (long)decimal.Truncate(amount);
        // Rounded, not truncated: 0.996 is 100 paise, and dropping it would
        // make the words disagree with the printed figure.
        var paise = (int)Math.Round((amount - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        if (paise == 100)
        {
            rupees++;
            paise = 0;
        }

        var words = new StringBuilder();

        if (rupees > 0)
            words.Append(ConvertWhole(rupees)).Append(" Rupees");

        if (paise > 0)
        {
            if (words.Length > 0) words.Append(" and ");
            words.Append(ConvertWhole(paise)).Append(" Paise");
        }

        return words.Append(" Only").ToString().Trim();
    }

    private static string ConvertWhole(long number)
    {
        if (number == 0) return "Zero";

        var parts = new List<string>();

        // Indian grouping: crore (10^7), lakh (10^5), then thousand, hundred.
        var crore = number / 10_000_000;
        if (crore > 0)
        {
            parts.Add(ConvertBelowThousand(crore) + " Crore");
            number %= 10_000_000;
        }

        var lakh = number / 100_000;
        if (lakh > 0)
        {
            parts.Add(ConvertBelowThousand(lakh) + " Lakh");
            number %= 100_000;
        }

        var thousand = number / 1_000;
        if (thousand > 0)
        {
            parts.Add(ConvertBelowThousand(thousand) + " Thousand");
            number %= 1_000;
        }

        if (number > 0)
            parts.Add(ConvertBelowThousand(number));

        return string.Join(" ", parts);
    }

    private static string ConvertBelowThousand(long number)
    {
        var parts = new List<string>();

        var hundreds = number / 100;
        if (hundreds > 0)
        {
            parts.Add(Ones[hundreds] + " Hundred");
            number %= 100;
        }

        if (number >= 20)
        {
            var tens = Tens[number / 10];
            var ones = number % 10;
            parts.Add(ones > 0 ? $"{tens} {Ones[ones]}" : tens);
        }
        else if (number > 0)
        {
            parts.Add(Ones[number]);
        }

        return string.Join(" ", parts);
    }
}
