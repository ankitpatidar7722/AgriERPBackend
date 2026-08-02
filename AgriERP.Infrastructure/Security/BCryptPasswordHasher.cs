using AgriERP.Application.Common.Interfaces;

namespace AgriERP.Infrastructure.Security;

/// <summary>
/// BCrypt with a work factor of 11.
///
/// The work factor is the point of BCrypt: it makes each guess cost real CPU
/// time, so a stolen password table cannot be brute-forced at GPU speed. 11 is
/// roughly 100ms on the kind of hardware a shop counter runs, which is
/// imperceptible on login and ruinous for an attacker doing it billions of
/// times. Raise it as hardware improves; existing hashes carry their own
/// factor and keep verifying.
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // The seeded admin row carries the sentinel '!SEED-PENDING!', which
            // is not a BCrypt hash at all. Returning false rather than throwing
            // means a database that has not been seeded yet fails the login
            // cleanly instead of returning a 500 that looks like an outage.
            return false;
        }
    }
}
