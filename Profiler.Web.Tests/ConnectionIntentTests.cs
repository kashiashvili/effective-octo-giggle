using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

public class ConnectionIntentHelperTests
{
    [Theory]
    [InlineData("friends")]
    [InlineData("collaborators")]
    [InlineData("discussion")]
    [InlineData("open")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_KnownKeysAndUnspecified(string? key)
    {
        Assert.True(ConnectionIntent.IsValid(key));
    }

    [Theory]
    [InlineData("marriage")]
    [InlineData("FRIENDS")]     // keys are case-sensitive by design
    [InlineData("<script>")]
    public void Rejects_AnythingOffList(string key)
    {
        Assert.False(ConnectionIntent.IsValid(key));
    }

    [Fact]
    public void Normalize_MapsUnknownAndBlankToNull_AndKeepsKnown()
    {
        Assert.Null(ConnectionIntent.Normalize("nope"));
        Assert.Null(ConnectionIntent.Normalize(""));
        Assert.Null(ConnectionIntent.Normalize(null));
        Assert.Equal("open", ConnectionIntent.Normalize("open"));
    }

    [Fact]
    public void LabelFor_ReturnsAHumanLabel_OrNullForUnspecified()
    {
        Assert.Equal("Collaborators on projects", ConnectionIntent.LabelFor("collaborators"));
        Assert.Null(ConnectionIntent.LabelFor(null));
        Assert.Null(ConnectionIntent.LabelFor("bogus"));
    }
}
