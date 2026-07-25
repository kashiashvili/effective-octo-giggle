using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

public class ShowableInterestsTests
{
    [Fact]
    public void Parse_SplitsOnCommasAndNewlines_TrimsAndDropsEmpties()
    {
        var result = ShowableInterests.Parse("sea kayaking, byzantine history\nmodular synthesis\n\n , ");
        Assert.Equal(new[] { "sea kayaking", "byzantine history", "modular synthesis" }, result);
    }

    [Fact]
    public void Parse_DedupesByCaseFoldedKey_KeepingFirstSpelling()
    {
        var result = ShowableInterests.Parse("Sea Kayaking\nsea kayaking\nSEA  KAYAKING");
        Assert.Equal(new[] { "Sea Kayaking" }, result);
    }

    [Fact]
    public void Parse_CapsCountAndLabelLength()
    {
        var many = string.Join("\n", Enumerable.Range(0, 50).Select(i => $"interest {i}"));
        Assert.Equal(ShowableInterests.MaxCount, ShowableInterests.Parse(many).Count);

        var longLabel = ShowableInterests.Parse(new string('x', 100)).Single();
        Assert.True(longLabel.Length <= ShowableInterests.MaxLabelLength);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTrips_AndNullForEmpty()
    {
        var labels = new List<string> { "sea kayaking", "byzantine history" };
        var json = ShowableInterests.Serialize(labels);
        Assert.NotNull(json);
        Assert.Equal(labels, ShowableInterests.Deserialize(json));

        Assert.Null(ShowableInterests.Serialize(new List<string>()));
        Assert.Empty(ShowableInterests.Deserialize(null));
    }

    [Fact]
    public void Common_ReturnsSharedLabels_ByCaseFoldedKey_KeepingMatchSpelling()
    {
        var viewer = new[] { "Sea Kayaking", "Chess" };
        var match = new[] { "sea kayaking", "byzantine history" };
        Assert.Equal(new[] { "sea kayaking" }, ShowableInterests.Common(viewer, match));
    }

    [Fact]
    public void Common_IsEmpty_WhenNoOverlap()
    {
        Assert.Empty(ShowableInterests.Common(new[] { "chess" }, new[] { "surfing" }));
    }
}
