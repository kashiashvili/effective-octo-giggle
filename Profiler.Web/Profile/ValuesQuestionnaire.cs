using System.Text.Json;
using System.Text.Json.Serialization;

namespace Profiler.Web.Profile;

/// <summary>
/// One person's derived values and worldview profile: four value priorities (Schwartz's higher-order
/// dimensions, relative to the person's own mean) and two world beliefs, each quantised to −2..+2.
/// This is everything that is stored; the answers it came from are discarded (docs/DESIGN_VALUES.md).
/// </summary>
public sealed record ValuesProfile(
    [property: JsonPropertyName("o")] int Openness,
    [property: JsonPropertyName("c")] int Conservation,
    [property: JsonPropertyName("t")] int Transcendence,
    [property: JsonPropertyName("e")] int Enhancement,
    [property: JsonPropertyName("s")] int Safe,
    [property: JsonPropertyName("n")] int Enticing)
{
    [JsonIgnore] public int[] Priorities => new[] { Openness, Conservation, Transcendence, Enhancement };
    [JsonIgnore] public int[] World => new[] { Safe, Enticing };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ValuesProfile? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<ValuesProfile>(json);
            return p != null && p.Priorities.Concat(p.World).All(v => v is >= ValuesQuestionnaire.MinLevel and <= ValuesQuestionnaire.MaxLevel) ? p : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The values and worldview questionnaire — the derivation only. Grounded in Schwartz's theory of
/// basic human values (ten values summarised by four higher-order priorities, scored relative to the
/// person's own mean) and in primal world beliefs (Safe, Enticing), in our own plain words: no licensed
/// instrument is reused. Raw answers go in, six small integers come out, the caller stores nothing else.
/// Deliberately non-clinical: a self-declared ordering of what matters, presented to matches as coarse,
/// explained alignment — never a score, a personality label, or a hard filter.
/// </summary>
public static class ValuesQuestionnaire
{
    /// <summary>Scheme version, stored beside a derived profile so a future item change is detectable.</summary>
    public const string Version = "schwartz-v2";

    /// <summary>Answers run 1..7 (importance for part 1, agreement for part 2).</summary>
    public const int MinAnswer = 1;
    public const int MaxAnswer = 7;
    private const double Midpoint = 4.0;

    /// <summary>Every stored dimension is quantised to this range.</summary>
    public const int MinLevel = -2;
    public const int MaxLevel = 2;

    public enum Dimension { Openness, Conservation, Transcendence, Enhancement, Safe, Enticing }

    /// <summary>One of the ten basic values, rated for importance. Weights say how it feeds the four priorities.</summary>
    public record ValueItem(string Key, string Name, string Description, double Openness, double Conservation, double Transcendence, double Enhancement);

    /// <summary>A world-belief statement, rated for agreement. Reverse items describe the opposite pole.</summary>
    public record WorldItem(string Key, string Statement, Dimension Dimension, bool Reverse);

    /// <summary>
    /// Part 1: "How important is each of these to you, as a guiding principle in your life?" One item per
    /// basic value in the order of the circular continuum. Hedonism counts half to openness and half to
    /// self-enhancement, where the theory places it.
    /// </summary>
    public static readonly IReadOnlyList<ValueItem> ValueItems = new[]
    {
        new ValueItem("independence", "Independence", "Thinking for yourself, choosing your own path, staying curious.", 1, 0, 0, 0),
        new ValueItem("excitement", "Excitement", "Variety, new experiences, a bit of adventure in life.", 1, 0, 0, 0),
        new ValueItem("enjoyment", "Enjoyment", "Pleasure, comfort, having a good time.", 0.5, 0, 0, 0.5),
        new ValueItem("achievement", "Achievement", "Being capable, ambitious and good at what you do.", 0, 0, 0, 1),
        new ValueItem("influence", "Influence", "Status, being in charge, having a say over how things go.", 0, 0, 0, 1),
        new ValueItem("security", "Security", "Safety and stability for yourself, your family and your surroundings.", 0, 1, 0, 0),
        new ValueItem("fitting-in", "Fitting in", "Following the rules and not upsetting other people.", 0, 1, 0, 0),
        new ValueItem("tradition", "Tradition", "Keeping the customs and habits you were brought up with.", 0, 1, 0, 0),
        new ValueItem("loyalty", "Loyalty", "Looking after the people close to you and being someone they can rely on.", 0, 0, 1, 0),
        new ValueItem("fairness", "Fairness for everyone", "Understanding people who are different from you, and looking after the natural world.", 0, 0, 1, 0),
    };

    /// <summary>Part 2: "How much do you agree?" Two statements per world belief, one of each reversed.</summary>
    public static readonly IReadOnlyList<WorldItem> WorldItems = new[]
    {
        new WorldItem("safe-place", "By and large, the world is a safe place.", Dimension.Safe, Reverse: false),
        new WorldItem("trust", "Most people can't really be trusted.", Dimension.Safe, Reverse: true),
        new WorldItem("interesting", "There's something interesting almost everywhere, if you look.", Dimension.Enticing, Reverse: false),
        new WorldItem("dull", "Most days, the world feels pretty dull.", Dimension.Enticing, Reverse: true),
    };

    public static IEnumerable<string> AllKeys => ValueItems.Select(i => i.Key).Concat(WorldItems.Select(i => i.Key));

    /// <summary>
    /// Reduces a complete set of answers to a profile, or null if any answer is missing or out of range.
    /// Value ratings are centred on the person's own mean first, so the result is the person's
    /// priorities rather than how generously they use the scale (Schwartz's scale-use correction).
    /// </summary>
    public static ValuesProfile? Derive(IReadOnlyDictionary<string, int>? answers)
    {
        if (answers is null) return null;
        foreach (var key in AllKeys)
        {
            if (!answers.TryGetValue(key, out var a)) return null;
            if (a < MinAnswer || a > MaxAnswer) return null;
        }

        var mean = ValueItems.Average(i => answers[i.Key]);
        double o = 0, c = 0, t = 0, e = 0, wo = 0, wc = 0, wt = 0, we = 0;
        foreach (var item in ValueItems)
        {
            var centred = answers[item.Key] - mean;
            o += item.Openness * centred; wo += item.Openness;
            c += item.Conservation * centred; wc += item.Conservation;
            t += item.Transcendence * centred; wt += item.Transcendence;
            e += item.Enhancement * centred; we += item.Enhancement;
        }

        double safe = 0, enticing = 0;
        foreach (var item in WorldItems)
        {
            var centred = answers[item.Key] - Midpoint;
            if (item.Reverse) centred = -centred;
            if (item.Dimension == Dimension.Safe) safe += centred / 2; else enticing += centred / 2;
        }

        return new ValuesProfile(
            Quantise(o / wo), Quantise(c / wc), Quantise(t / wt), Quantise(e / we),
            Quantise(safe / 1.5), Quantise(enticing / 1.5));
    }

    // Centred priorities sit mostly within ±2 on a 7-point scale; world beliefs span ±3 and are scaled down.
    private static int Quantise(double score) =>
        Math.Clamp((int)Math.Round(score, MidpointRounding.AwayFromZero), MinLevel, MaxLevel);

    // ---- Comparison ---------------------------------------------------------------------------------

    /// <summary>Mean absolute difference across the four priorities (0..4).</summary>
    public static double PriorityDistance(ValuesProfile a, ValuesProfile b) =>
        a.Priorities.Zip(b.Priorities, (x, y) => Math.Abs(x - y)).Average();

    /// <summary>Mean absolute difference across the two world beliefs (0..4).</summary>
    public static double WorldDistance(ValuesProfile a, ValuesProfile b) =>
        a.World.Zip(b.World, (x, y) => Math.Abs(x - y)).Average();

    /// <summary>0 = similar, 1 = some overlap, 2 = different.</summary>
    public static int Tier(double distance) => distance <= 0.5 ? 0 : distance <= 1.25 ? 1 : 2;

    /// <summary>
    /// Ordering key for the "Similar outlook first" sort — smaller is closer, <see cref="int.MaxValue"/>
    /// when either side has no profile. Keyed to the shown tiers (priorities first, then worldview), so
    /// the sort never draws a distinction the card does not display; interest order breaks ties.
    /// </summary>
    public static int AlignmentRank(ValuesProfile? a, ValuesProfile? b)
    {
        if (a is null || b is null) return int.MaxValue;
        return Tier(PriorityDistance(a, b)) * 3 + Tier(WorldDistance(a, b));
    }

    /// <summary>Coarse, wordy alignment of priorities — never a number. Null if either side has no profile.</summary>
    public static string? AlignmentLabel(ValuesProfile? a, ValuesProfile? b)
    {
        if (a is null || b is null) return null;
        return Tier(PriorityDistance(a, b)) switch
        {
            0 => "Similar priorities",
            1 => "Some overlap in priorities",
            _ => "Different priorities",
        };
    }

    /// <summary>
    /// The reason behind the label, in the same words the person read on the questionnaire: the priority
    /// both put first, or the one they differ on most. Null if either side has no profile.
    /// </summary>
    public static string? AlignmentReason(ValuesProfile? a, ValuesProfile? b)
    {
        if (a is null || b is null) return null;
        var topA = TopPriority(a);
        var topB = TopPriority(b);
        if (topA == topB && a.Priorities[(int)topA] > 0 && b.Priorities[(int)topB] > 0)
            return $"you both put {PriorityName(topA)} first";

        var gaps = a.Priorities.Zip(b.Priorities, (x, y) => Math.Abs(x - y)).ToArray();
        var widest = Array.IndexOf(gaps, gaps.Max());
        return gaps[widest] == 0
            ? "your priorities line up across the board"
            : $"you differ most on {PriorityName((Dimension)widest)}";
    }

    /// <summary>"similar view of the world" / "different view of the world", or null when either side lacks one.</summary>
    public static string? WorldLabel(ValuesProfile? a, ValuesProfile? b)
    {
        if (a is null || b is null) return null;
        return Tier(WorldDistance(a, b)) switch
        {
            0 => "similar view of the world",
            1 => "a partly similar view of the world",
            _ => "a different view of the world",
        };
    }

    private static Dimension TopPriority(ValuesProfile p)
    {
        var best = 0;
        for (var i = 1; i < 4; i++) if (p.Priorities[i] > p.Priorities[best]) best = i;
        return (Dimension)best;
    }

    // ---- Self-description ---------------------------------------------------------------------------

    /// <summary>Plain names for the four priorities and two world beliefs, used on cards and in the person's own summary.</summary>
    public static string PriorityName(Dimension d) => d switch
    {
        Dimension.Openness => "new experiences and independence",
        Dimension.Conservation => "stability, tradition and fitting in",
        Dimension.Transcendence => "caring for people and the planet",
        Dimension.Enhancement => "achievement and influence",
        Dimension.Safe => "seeing the world as a safe place",
        _ => "finding the world full of interest",
    };

    /// <summary>The Schwartz term behind a plain name, for people who want the science.</summary>
    public static string ScienceName(Dimension d) => d switch
    {
        Dimension.Openness => "Openness to change",
        Dimension.Conservation => "Conservation",
        Dimension.Transcendence => "Self-transcendence",
        Dimension.Enhancement => "Self-enhancement",
        Dimension.Safe => "Safe world (primal belief)",
        _ => "Enticing world (primal belief)",
    };

    /// <summary>The 1–7 importance scale in words, so a screen reader hears more than a bare number.</summary>
    public static string ImportanceWord(int answer) => answer switch
    {
        1 => "not important to me",
        2 => "slightly important",
        3 => "somewhat important",
        4 => "moderately important",
        5 => "quite important",
        6 => "very important",
        _ => "extremely important",
    };

    /// <summary>The 1–7 agreement scale in words, for the same reason.</summary>
    public static string AgreementWord(int answer) => answer switch
    {
        1 => "strongly disagree",
        2 => "disagree",
        3 => "slightly disagree",
        4 => "neither agree nor disagree",
        5 => "slightly agree",
        6 => "agree",
        _ => "strongly agree",
    };

    /// <summary>A level in words: relative priorities read as "above / around / below your average".</summary>
    public static string LevelWord(int level) => level switch
    {
        2 => "far above your average",
        1 => "above your average",
        0 => "around your average",
        -1 => "below your average",
        _ => "far below your average",
    };

    /// <summary>A world-belief level in words.</summary>
    public static string WorldWord(Dimension d, int level) => (d, level) switch
    {
        (Dimension.Safe, > 0) => "you see the world as a fairly safe place",
        (Dimension.Safe, < 0) => "you see the world as a risky place",
        (Dimension.Safe, _) => "you see the world as neither especially safe nor risky",
        (_, > 0) => "you find the world full of interest",
        (_, < 0) => "you find the world mostly dull",
        _ => "you find the world interesting in places",
    };

    /// <summary>One line per dimension for the person's own summary.</summary>
    public static IEnumerable<(string Name, string Science, string Reading, int Level)> Describe(ValuesProfile p)
    {
        for (var i = 0; i < 4; i++)
        {
            var d = (Dimension)i;
            yield return (PriorityName(d), ScienceName(d), LevelWord(p.Priorities[i]), p.Priorities[i]);
        }
        yield return (PriorityName(Dimension.Safe), ScienceName(Dimension.Safe), WorldWord(Dimension.Safe, p.Safe), p.Safe);
        yield return (PriorityName(Dimension.Enticing), ScienceName(Dimension.Enticing), WorldWord(Dimension.Enticing, p.Enticing), p.Enticing);
    }
}
