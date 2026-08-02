using AgriERP.Application.Common.Interfaces;

namespace AgriERP.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    /// <summary>Audit timestamps. UTC so they survive a timezone change on the server.</summary>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Business calendar date, local. A bill raised at 11pm IST belongs to that
    /// day's sales, not the next day's in UTC - so expiry windows, due dates
    /// and daily totals all key off local midnight.
    /// </summary>
    public DateTime Today => DateTime.Today;
}
