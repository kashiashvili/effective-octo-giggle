using System.Globalization;
using System.Text;

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

    /// <summary>
    /// Writing systems, coarse enough to be useful here. Japanese is one entry rather than three,
    /// because ordinary Japanese text mixes kanji with both kana; Han and Hangul likewise co-occur in
    /// Korean. Digits, punctuation and the like belong to no script and are allowed anywhere.
    /// </summary>
    private enum Script { None, Latin, Greek, Cyrillic, Japanese, Hangul, Arabic, Hebrew, Other }

    private static Script ScriptOf(char c)
    {
        if (!char.IsLetter(c)) return Script.None;

        return c switch
        {
            <= 'ɏ' => Script.Latin,                          // Latin, incl. accented forms
            >= 'Ͱ' and <= 'Ͽ' => Script.Greek,
            >= 'ἀ' and <= '῿' => Script.Greek,           // polytonic
            >= 'Ѐ' and <= 'ӿ' => Script.Cyrillic,
            >= 'Ԁ' and <= 'ԯ' => Script.Cyrillic,
            >= '֐' and <= '׿' => Script.Hebrew,
            >= '؀' and <= 'ۿ' => Script.Arabic,
            >= '぀' and <= 'ヿ' => Script.Japanese,        // hiragana + katakana
            >= '一' and <= '鿿' => Script.Japanese,        // Han, shared with Chinese/Korean
            >= '가' and <= '힯' => Script.Hangul,
            >= 'ᄀ' and <= 'ᇿ' => Script.Hangul,
            _ => Script.Other
        };
    }

    /// <summary>
    /// Refuses a username that draws on more than one writing system. The username is the only thing
    /// a stranger sees before deciding to make contact, and a Cyrillic "а" is indistinguishable from
    /// a Latin one — so "аdmin" would sit next to "admin" on a match card with nothing to tell them
    /// apart. A name written entirely in Cyrillic, Greek or Japanese is unaffected; only the mixture,
    /// which is what impersonation needs, is refused.
    /// </summary>
    private static bool MixesScripts(string username)
    {
        var seen = Script.None;
        foreach (var c in username)
        {
            var script = ScriptOf(c);
            if (script == Script.None) continue;
            if (seen == Script.None) seen = script;
            else if (script != seen) return true;
        }
        return false;
    }

    /// <summary>
    /// The form two usernames are compared in to decide whether they are "the same person": trimmed,
    /// compatibility-folded (so ligatures and full-width variants collapse) and lower-cased (so case
    /// folds beyond the ASCII that SQLite's NOCASE covers). Accents are deliberately kept — "André"
    /// and "Andre" are different names — this only removes differences that are not really there.
    /// </summary>
    public static string NormalizeForUniqueness(string? username) =>
        (username ?? "").Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    /// <summary>Returns an error message if the username is unacceptable, or null if it is fine.</summary>
    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null; // length and presence are handled by the view model

        if (FindDeceptiveCharacter(username, allowNewlines: false) is { } problem)
            return $"Usernames {problem}.";

        if (MixesScripts(username))
            return "Usernames can't mix alphabets — pick one writing system, so nobody can register a lookalike of your name.";

        return null;
    }

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
