using Profiler.Web.Profile;
using Xunit;
using static Profiler.Web.Profile.ValuesQuestionnaire;

namespace Profiler.Web.Tests;

/// <summary>
/// The v2 derivation (docs/DESIGN_VALUES.md): ratings are read relative to the person's own mean,
/// the ten values fold into four priorities with hedonism split, world beliefs reverse-score, and the
/// comparison and explanation helpers say only what the card shows.
/// </summary>
public class ValuesQuestionnaireTests
{
    /// <summary>All value items at <paramref name="baseline"/>, then overrides; world items at 4 unless given.</summary>
    private static Dictionary<string, int> Answers(int baseline = 4, int world = 4, params (string Key, int Value)[] overrides)
    {
        var a = new Dictionary<string, int>();
        foreach (var item in ValueItems) a[item.Key] = baseline;
        foreach (var item in WorldItems) a[item.Key] = world;
        foreach (var (k, v) in overrides) a[k] = v;
        return a;
    }

    [Fact]
    public void FlatRatings_GiveAFlatProfile_WhateverTheLevel()
    {
        // Rating everything 7 and rating everything 2 are the same priorities: nothing ahead of anything.
        var high = Derive(Answers(baseline: 7))!;
        var low = Derive(Answers(baseline: 2))!;
        Assert.Equal(new ValuesProfile(0, 0, 0, 0, 0, 0), high);
        Assert.Equal(high, low);
    }

    [Fact]
    public void PuttingIndependenceAndExcitementFirst_ReadsAsOpenness_AndAgainstConservation()
    {
        var p = Derive(Answers(baseline: 4, overrides: new[] { ("independence", 7), ("excitement", 7), ("security", 2), ("tradition", 2), ("fitting-in", 2) }))!;
        Assert.True(p.Openness >= 1, $"openness {p.Openness}");
        Assert.True(p.Conservation <= -1, $"conservation {p.Conservation}");
        Assert.Equal(Dimension.Openness, (Dimension)Array.IndexOf(p.Priorities, p.Priorities.Max()));
    }

    [Fact]
    public void Enjoyment_CountsHalfToOpenness_AndHalfToSelfEnhancement()
    {
        var only = Derive(Answers(baseline: 4, overrides: new[] { ("enjoyment", 7) }))!;
        Assert.True(only.Openness >= 0 && only.Enhancement >= 0);
        Assert.Equal(only.Openness, only.Enhancement);
        Assert.True(only.Conservation <= 0 && only.Transcendence <= 0);
    }

    [Fact]
    public void WorldBeliefs_ReverseScore_AndCentreOnTheMidpoint()
    {
        var safe = Derive(Answers(overrides: new[] { ("safe-place", 7), ("trust", 1) }))!;      // agrees it's safe, disagrees people can't be trusted
        var risky = Derive(Answers(overrides: new[] { ("safe-place", 1), ("trust", 7) }))!;
        var neutral = Derive(Answers())!;
        Assert.Equal(2, safe.Safe);
        Assert.Equal(-2, risky.Safe);
        Assert.Equal(0, neutral.Safe);
        var enticing = Derive(Answers(overrides: new[] { ("interesting", 7), ("dull", 1) }))!;
        Assert.Equal(2, enticing.Enticing);
    }

    [Fact]
    public void MissingOrOutOfRangeAnswers_YieldNothing()
    {
        var partial = Answers();
        partial.Remove("loyalty");
        Assert.Null(Derive(partial));
        Assert.Null(Derive(Answers(overrides: new[] { ("fairness", 0) })));
        Assert.Null(Derive(Answers(overrides: new[] { ("dull", 8) })));
        Assert.Null(Derive(null));
    }

    [Fact]
    public void EveryDimension_StaysWithinTheStoredRange()
    {
        var rng = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var a = new Dictionary<string, int>();
            foreach (var k in AllKeys) a[k] = rng.Next(MinAnswer, MaxAnswer + 1);
            var p = Derive(a)!;
            Assert.All(p.Priorities.Concat(p.World), v => Assert.InRange(v, MinLevel, MaxLevel));
        }
    }

    [Fact]
    public void Json_RoundTrips_AndRejectsGarbage()
    {
        var p = new ValuesProfile(2, -1, 1, 0, -2, 1);
        Assert.Equal(p, ValuesProfile.FromJson(p.ToJson()));
        Assert.Null(ValuesProfile.FromJson(null));
        Assert.Null(ValuesProfile.FromJson(""));
        Assert.Null(ValuesProfile.FromJson("not json"));
        Assert.Null(ValuesProfile.FromJson("{\"o\":9,\"c\":0,\"t\":0,\"e\":0,\"s\":0,\"n\":0}")); // out of range
    }

    [Theory]
    [InlineData(0, 0, 0, 0, "Similar priorities")]
    [InlineData(1, 0, 0, 1, "Similar priorities")]        // mean gap 0.5
    [InlineData(1, 1, 1, 1, "Some overlap in priorities")] // mean gap 1
    [InlineData(2, 2, 1, 0, "Some overlap in priorities")] // 1.25
    [InlineData(2, 2, 2, 0, "Different priorities")]       // 1.5
    public void AlignmentLabel_KeysToMeanPriorityGap(int dO, int dC, int dT, int dE, string expected)
    {
        var a = new ValuesProfile(0, 0, 0, 0, 0, 0);
        var b = new ValuesProfile(dO, dC, dT, dE, 0, 0);
        Assert.Equal(expected, AlignmentLabel(a, b));
        Assert.Equal(expected, AlignmentLabel(b, a));
    }

    [Fact]
    public void Labels_AreNull_WhenEitherSideHasNoProfile()
    {
        var p = new ValuesProfile(1, 0, 0, 0, 0, 0);
        Assert.Null(AlignmentLabel(null, p));
        Assert.Null(AlignmentReason(p, null));
        Assert.Null(WorldLabel(null, null));
        Assert.Equal(int.MaxValue, AlignmentRank(p, null));
    }

    [Fact]
    public void AlignmentRank_OrdersByShownTiers_PrioritiesFirst_ThenWorld()
    {
        var me = new ValuesProfile(2, -2, 1, -1, 2, 2);
        var twin = me;
        var sameValuesDifferentWorld = new ValuesProfile(2, -2, 1, -1, -2, -2);
        var differentValuesSameWorld = new ValuesProfile(-2, 2, -1, 1, 2, 2);
        Assert.True(AlignmentRank(me, twin) < AlignmentRank(me, sameValuesDifferentWorld));
        Assert.True(AlignmentRank(me, sameValuesDifferentWorld) < AlignmentRank(me, differentValuesSameWorld));
        // Within a shown tier nothing is distinguished: both of these are "Similar priorities".
        Assert.Equal(AlignmentRank(me, twin), AlignmentRank(me, new ValuesProfile(2, -2, 1, -1, 2, 2) with { Enhancement = 0 }) - 0);
    }

    [Fact]
    public void AlignmentReason_NamesTheSharedTopPriority_OrTheWidestGap()
    {
        var caring = new ValuesProfile(0, -1, 2, -1, 0, 0);
        var caringToo = new ValuesProfile(1, -2, 2, 0, 0, 0);
        Assert.Equal("you both put caring for people and the planet first", AlignmentReason(caring, caringToo));

        var ambitious = new ValuesProfile(0, -1, -2, 2, 0, 0);
        Assert.Equal("you differ most on caring for people and the planet", AlignmentReason(caring, ambitious));

        Assert.Equal("your priorities line up across the board", AlignmentReason(new ValuesProfile(0, 0, 0, 0, 0, 0), new ValuesProfile(0, 0, 0, 0, 2, 2)));
    }

    [Theory]
    [InlineData(2, 2, "similar view of the world")]
    [InlineData(1, 1, "a partly similar view of the world")]
    [InlineData(2, -2, "a different view of the world")]
    public void WorldLabel_KeysToMeanWorldGap(int safeB, int enticingB, string expected)
    {
        var a = new ValuesProfile(0, 0, 0, 0, 2, 2);
        var b = new ValuesProfile(0, 0, 0, 0, safeB, enticingB);
        Assert.Equal(expected, WorldLabel(a, b));
    }

    [Fact]
    public void Describe_SaysEachDimensionInWords_RelativeToTheOwnAverage()
    {
        var rows = Describe(new ValuesProfile(2, -2, 0, 1, -1, 2)).ToList();
        Assert.Equal(6, rows.Count);
        Assert.Contains(rows, r => r.Name == "new experiences and independence" && r.Reading == "far above your average" && r.Science == "Openness to change");
        Assert.Contains(rows, r => r.Name == "stability, tradition and fitting in" && r.Reading == "far below your average");
        Assert.Contains(rows, r => r.Science.StartsWith("Safe world") && r.Reading == "you see the world as a risky place");
        Assert.Contains(rows, r => r.Science.StartsWith("Enticing world") && r.Reading == "you find the world full of interest");
    }

    [Fact]
    public void Items_AreOriginalWording_AndCoverTheTenValuesOnce()
    {
        Assert.Equal(10, ValueItems.Count);
        Assert.Equal(4, WorldItems.Count);
        Assert.Equal(AllKeys.Count(), AllKeys.Distinct().Count());
        // Each priority is fed by at least two items; the weights per item sum to one.
        Assert.All(ValueItems, i => Assert.Equal(1.0, i.Openness + i.Conservation + i.Transcendence + i.Enhancement, 6));
        Assert.True(ValueItems.Sum(i => i.Openness) >= 2 && ValueItems.Sum(i => i.Conservation) >= 2
                 && ValueItems.Sum(i => i.Transcendence) >= 2 && ValueItems.Sum(i => i.Enhancement) >= 2);
        // Nothing political, moral, clinical or religious in the wording (standing decision).
        var banned = new[] { "god", "religio", "vote", "party", "government", "immigra", "abortion", "gun", "sin", "moral", "disorder", "anxiety", "depress" };
        var text = string.Join(" ", ValueItems.Select(i => i.Name + " " + i.Description).Concat(WorldItems.Select(i => i.Statement))).ToLowerInvariant();
        Assert.All(banned, word => Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{word}"), $"item wording contains '{word}'"));
    }
}
