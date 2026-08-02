namespace AgriERP.Application.Common.Services;

/// <summary>The four tax components of one line or one document.</summary>
public readonly record struct TaxSplit(decimal Cgst, decimal Sgst, decimal Igst, decimal Cess)
{
    public decimal Total => Cgst + Sgst + Igst + Cess;

    public static readonly TaxSplit Zero = new(0m, 0m, 0m, 0m);
}

public interface IGstCalculator
{
    TaxSplit Split(decimal taxableAmount, decimal gstPercent, bool isInterState, decimal cessPercent = 0m);

    /// <summary>Rounds a money value to paise the way Indian billing does.</summary>
    decimal Money(decimal value);

    /// <summary>
    /// Round-off to the nearest rupee, returned as the adjustment to add.
    /// A bill of 1,234.60 returns 0.40 so the printed total is a round 1,235.
    /// </summary>
    decimal RoundOffAdjustment(decimal grandTotal);
}

public class GstCalculator : IGstCalculator
{
    public TaxSplit Split(decimal taxableAmount, decimal gstPercent, bool isInterState, decimal cessPercent = 0m)
    {
        if (gstPercent <= 0 && cessPercent <= 0)
            return TaxSplit.Zero;

        var totalTax = Money(taxableAmount * gstPercent / 100m);
        var cess = cessPercent > 0 ? Money(taxableAmount * cessPercent / 100m) : 0m;

        if (isInterState)
            return new TaxSplit(0m, 0m, totalTax, cess);

        // CGST is rounded, then SGST takes the remainder rather than being
        // rounded independently. Rounding both halves separately can leave
        // CGST + SGST one paisa away from the total tax, and that single paisa
        // is exactly what makes a GSTR-1 filing fail reconciliation.
        var cgst = Money(totalTax / 2m);
        var sgst = totalTax - cgst;

        return new TaxSplit(cgst, sgst, 0m, cess);
    }

    // AwayFromZero, not banker's rounding. .NET's default MidpointRounding
    // would turn 0.125 into 0.12, which is not what a shop or a tax officer
    // expects when they check the arithmetic by hand.
    public decimal Money(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public decimal RoundOffAdjustment(decimal grandTotal)
        => Math.Round(grandTotal, 0, MidpointRounding.AwayFromZero) - grandTotal;
}
