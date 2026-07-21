namespace Profiler.Web.Security;

/// <summary>
/// Minimum-viable password screening, in the spirit of the NIST guidance: length is enforced by
/// the view models, and this rejects the passwords that actually get compromised — well-known
/// choices, anything containing the username, and near-single-character strings.
/// </summary>
public static class PasswordPolicy
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "p@ssword",
        "12345678", "123456789", "1234567890", "87654321", "qwerty123", "qwertyuiop",
        "letmein", "letmein123", "welcome1", "welcome123", "iloveyou", "admin123",
        "abc12345", "football", "baseball", "monkey123", "dragon123", "sunshine",
        "princess", "1q2w3e4r", "zaq12wsx", "trustno1", "changeme", "starwars"
    };

    /// <summary>Returns an error message describing why the password is unacceptable, or null if it is fine.</summary>
    public static string? Validate(string? password, string? username)
    {
        if (string.IsNullOrEmpty(password)) return null; // length/required handled by model validation

        if (CommonPasswords.Contains(password))
            return "That password is one of the most commonly used ones. Please choose something harder to guess.";

        if (!string.IsNullOrWhiteSpace(username) &&
            password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return "Your password must not contain your username.";

        if (password.Distinct().Count() < 4)
            return "That password repeats too few characters. Please choose something harder to guess.";

        return null;
    }
}
