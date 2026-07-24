using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The values derivation is the privacy-sensitive core of the second signal: raw answers in, one
/// coarse bucket out, nothing else kept. These pin the scoring (including reverse items), the
/// rejection of incomplete or out-of-range input, and the coarse alignment wording.
/// </summary>
public class ValuesQuestionnaireTests
{
    private static Dictionary<string, int> Answers(int novelty, int routine, int ideas, int tradition) => new()
    {
        ["novelty"] = novelty,
        ["routine"] = routine,
        ["ideas"] = ideas,
        ["tradition"] = tradition,
    };

    [Fact]
    public void FullyOpenResponse_MapsToTheOpennessExtreme()
    {
        // Agree with openness items (5), disagree with conservation items (1).
        Assert.Equal(2, ValuesQuestionnaire.DeriveBucket(Answers(novelty: 5, routine: 1, ideas: 5, tradition: 1)));
    }

    [Fact]
    public void FullyTraditionalResponse_MapsToTheConservationExtreme()
    {
        Assert.Equal(-2, ValuesQuestionnaire.DeriveBucket(Answers(novelty: 1, routine: 5, ideas: 1, tradition: 5)));
    }

    [Fact]
    public void AllNeutral_MapsToTheCentre()
    {
        Assert.Equal(0, ValuesQuestionnaire.DeriveBucket(Answers(3, 3, 3, 3)));
    }

    [Fact]
    public void ReverseItemsCountAgainstOpenness()
    {
        // Agreeing with every statement, including the conservation ones, must not read as fully open.
        var bucket = ValuesQuestionnaire.DeriveBucket(Answers(5, 5, 5, 5));
        Assert.Equal(0, bucket);
    }

    [Fact]
    public void IncompleteAnswers_ReturnNull()
    {
        var partial = new Dictionary<string, int> { ["novelty"] = 5, ["ideas"] = 5 };
        Assert.Null(ValuesQuestionnaire.DeriveBucket(partial));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void OutOfRangeAnswers_ReturnNull(int bad)
    {
        Assert.Null(ValuesQuestionnaire.DeriveBucket(Answers(bad, 3, 3, 3)));
    }

    [Fact]
    public void DerivedBucket_StaysWithinRange_ForEveryPossibleResponse()
    {
        for (var n = 1; n <= 5; n++)
        for (var r = 1; r <= 5; r++)
        for (var i = 1; i <= 5; i++)
        for (var t = 1; t <= 5; t++)
        {
            var bucket = ValuesQuestionnaire.DeriveBucket(Answers(n, r, i, t));
            Assert.NotNull(bucket);
            Assert.InRange(bucket!.Value, ValuesQuestionnaire.MinBucket, ValuesQuestionnaire.MaxBucket);
        }
    }

    [Theory]
    [InlineData(2, 2, "Similar outlook")]
    [InlineData(2, 1, "Similar outlook")]
    [InlineData(2, 0, "Some overlap in outlook")]
    [InlineData(2, -1, "Different outlook")]
    [InlineData(-2, 2, "Different outlook")]
    public void AlignmentLabel_IsCoarse_AndByDistance(int a, int b, string expected)
    {
        Assert.Equal(expected, ValuesQuestionnaire.AlignmentLabel(a, b));
    }

    [Fact]
    public void AlignmentLabel_IsNull_WhenEitherSideHasNoSignal()
    {
        Assert.Null(ValuesQuestionnaire.AlignmentLabel(null, 1));
        Assert.Null(ValuesQuestionnaire.AlignmentLabel(1, null));
        Assert.Null(ValuesQuestionnaire.AlignmentLabel(null, null));
    }

    [Theory]
    [InlineData(2, 2, 0)]
    [InlineData(2, -2, 4)]
    [InlineData(-1, 1, 2)]
    public void AlignmentRank_IsTheDistance_SmallerIsCloser(int a, int b, int expected)
    {
        Assert.Equal(expected, ValuesQuestionnaire.AlignmentRank(a, b));
    }

    [Fact]
    public void AlignmentRank_SortsAbsentSignalsLast()
    {
        // The sort keys off this, not the display label, so a missing signal must rank worst.
        Assert.Equal(int.MaxValue, ValuesQuestionnaire.AlignmentRank(null, 2));
        Assert.Equal(int.MaxValue, ValuesQuestionnaire.AlignmentRank(2, null));
        Assert.True(ValuesQuestionnaire.AlignmentRank(2, -2) < ValuesQuestionnaire.AlignmentRank(2, null));
    }
}
