using System.Globalization;

namespace Profiler.Web.Security;

/// <summary>
/// A username is the only thing a stranger sees before deciding whether to contact someone, so it
/// has to render as what it appears to be. This rejects the characters that break that — control
/// characters and the bidirectional overrides that can make text display in a different order than
/// it is stored — while deliberately leaving every writing system alone.
/// </summary>
public static class UsernamePolicy
{
    private static bool IsBidiControl(char c) =>
        c is '‎' or '‏'              // LTR / RTL mark
          or >= '‪' and <= '‮'       // embedding / override / pop
          or >= '⁦' and <= '⁩';      // isolates

    /// <summary>Returns an error message if the username is unacceptable, or null if it is fine.</summary>
    public static string? Validate(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null; // length/required handled by the view model

        foreach (var c in username)
        {
            if (IsBidiControl(c))
                return "Usernames can't contain text-direction characters.";

            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
                return "Usernames can't contain invisible or control characters.";
        }

        return null;
    }
}
