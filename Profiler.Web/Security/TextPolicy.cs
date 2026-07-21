using System.Globalization;

namespace Profiler.Web.Security;

/// <summary>
/// Rules for the free text one person shows another: usernames, bios and contact lines. All three
/// are read by a stranger deciding whether to make contact, so they have to render as what they
/// appear to be. Bidirectional overrides can display stored text in a different order, and
/// zero-width or control characters can make two values look identical. Every writing system is
/// deliberately left alone — only these deceptive characters are refused.
/// </summary>
public static class TextPolicy
{
    private static bool IsBidiControl(char c) =>
        c is '‎' or '‏'                 // LTR / RTL mark
          or >= '‪' and <= '‮'          // embedding / override / pop
          or >= '⁦' and <= '⁩';         // isolates

    private static string? FindDeceptiveCharacter(string value, bool allowNewlines)
    {
        foreach (var c in value)
        {
            if (allowNewlines && (c == '\n' || c == '\r')) continue;

            if (IsBidiControl(c))
                return "can't contain text-direction characters";

            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
                return "can't contain invisible or control characters";
        }
        return null;
    }

    /// <summary>Returns an error message if the username is unacceptable, or null if it is fine.</summary>
    public static string? ValidateUsername(string? username) =>
        string.IsNullOrWhiteSpace(username)
            ? null // length and presence are handled by the view model
            : FindDeceptiveCharacter(username, allowNewlines: false) is { } problem
                ? $"Usernames {problem}."
                : null;

    /// <summary>
    /// Validates a public profile field. Bios may span lines; a contact line may not, since a
    /// newline there could push the visible part away from what is actually stored.
    /// </summary>
    public static string? ValidateProfileText(string? value, string fieldLabel, bool allowNewlines) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : FindDeceptiveCharacter(value, allowNewlines) is { } problem
                ? $"{fieldLabel} {problem}."
                : null;
}
