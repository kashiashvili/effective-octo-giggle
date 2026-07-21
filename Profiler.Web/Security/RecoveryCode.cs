using System.Security.Cryptography;
using System.Text;

namespace Profiler.Web.Security;

/// <summary>
/// The account's only recovery path. No email address is collected — that is the point of the
/// product — so instead the user is handed a code once and asked to keep it. Only its BCrypt hash is
/// stored, so a database copy does not let anyone in.
///
/// Alphabet is Crockford's base32: no I, L, O or U, so nothing in a written-down code reads as
/// something else. Twenty characters is 100 bits, far past anything a rate-limited endpoint could be
/// brute-forced through.
/// </summary>
public static class RecoveryCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 20;
    private const int GroupSize = 5;

    /// <summary>Generates a code in its display form, e.g. <c>4KM7Q-8ZTX2-...</c>.</summary>
    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        var display = new StringBuilder();
        for (var i = 0; i < Length; i++)
        {
            if (i > 0 && i % GroupSize == 0) display.Append('-');
            display.Append(chars[i]);
        }
        return display.ToString();
    }

    /// <summary>
    /// Reduces a code to what is actually compared, so that someone typing it back from paper is not
    /// defeated by case, spacing, or the handful of characters that look alike. Excluded letters are
    /// folded onto the digit they resemble rather than rejected.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";

        var normalized = new StringBuilder();
        foreach (var raw in code.ToUpperInvariant())
        {
            var c = raw switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                'U' => 'V',
                _ => raw
            };
            if (Alphabet.Contains(c)) normalized.Append(c);
        }
        return normalized.ToString();
    }

    public static string Hash(string code) => BCrypt.Net.BCrypt.HashPassword(Normalize(code));

    public static bool Verify(string? supplied, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        var normalized = Normalize(supplied);
        if (normalized.Length == 0) return false;

        try { return BCrypt.Net.BCrypt.Verify(normalized, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}
