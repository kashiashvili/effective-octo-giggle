using Profiler.Web.Connectors;
using Profiler.Web.Matching;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

public class AggregatorAndMatcherTests
{
    private sealed class StubConnector : IConnector
    {
        private readonly ProfileData? _data;
        private readonly Exception? _error;

        public StubConnector(ProfileData data) => _data = data;
        public StubConnector(Exception error) => _error = error;

        public string Name => _data?.Source ?? "Stub";

        public Task<ProfileData> FetchAsync() =>
            _error != null ? Task.FromException<ProfileData>(_error) : Task.FromResult(_data!);
    }

    /// <summary>A connector that takes a fixed time before answering, for the fan-out timing tests.</summary>
    private sealed class SlowConnector : IConnector
    {
        private readonly TimeSpan _delay;
        private readonly ProfileData? _data;

        public SlowConnector(string name, TimeSpan delay, ProfileData? data = null)
        {
            Name = name;
            _delay = delay;
            _data = data;
        }

        public string Name { get; }

        public async Task<ProfileData> FetchAsync()
        {
            await Task.Delay(_delay);
            return _data ?? new ProfileData(Name, new[] { "slow:1" });
        }
    }

    [Fact]
    public async Task Aggregate_RunsConnectorsConcurrently_NotOneAfterAnother()
    {
        var delay = TimeSpan.FromMilliseconds(300);
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new SlowConnector("A", delay),
            new SlowConnector("B", delay),
            new SlowConnector("C", delay),
            new SlowConnector("D", delay)
        });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await aggregator.AggregateAsync();
        started.Stop();

        Assert.Equal(new[] { "A", "B", "C", "D" }, result.Sources);
        // Sequentially this is 1200ms. The bound is loose enough to survive a slow machine while
        // still failing outright if the connectors are awaited one at a time.
        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(900),
            $"fan-out took {started.ElapsedMilliseconds}ms, which suggests the connectors ran sequentially");
    }

    [Fact]
    public async Task Aggregate_ConnectorExceedingTheBudget_IsReportedButDoesNotLoseTheOthers()
    {
        var aggregator = new ProfileAggregator(
            new IConnector[]
            {
                new StubConnector(new ProfileData("Fast", new[] { "x:1" })),
                new SlowConnector("Hung", TimeSpan.FromSeconds(30))
            },
            TimeSpan.FromMilliseconds(200));

        var result = await aggregator.AggregateAsync();

        Assert.Equal(new[] { "Fast" }, result.Sources);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("Hung", failure.Source);
        Assert.Contains("too long", failure.Message);
    }

    [Fact]
    public async Task Aggregate_OrdersResultsByConnector_NotByWhoAnsweredFirst()
    {
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new SlowConnector("Slowest", TimeSpan.FromMilliseconds(250)),
            new SlowConnector("Middle", TimeSpan.FromMilliseconds(120)),
            new SlowConnector("Fastest", TimeSpan.Zero)
        });

        var result = await aggregator.AggregateAsync();

        Assert.Equal(new[] { "Slowest", "Middle", "Fastest" }, result.Sources);
    }

    [Fact]
    public async Task Aggregate_CollectsFeaturesFromAllSuccessfulConnectors()
    {
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new StubConnector(new ProfileData("A", new[] { "x:1", "x:2" })),
            new StubConnector(new ProfileData("B", new[] { "y:1" }))
        });

        var result = await aggregator.AggregateAsync();

        Assert.Equal(new[] { "A", "B" }, result.Sources);
        Assert.Equal(3, result.Features.Count);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Aggregate_RecordsFailuresWithoutLosingSuccessfulSources()
    {
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new StubConnector(new ProfileData("Good", new[] { "x:1" })),
            new StubConnector(new ConnectorException("token expired"))
        });

        var result = await aggregator.AggregateAsync();

        Assert.Equal(new[] { "Good" }, result.Sources);
        Assert.Single(result.Failures);
        Assert.Equal("Stub", result.Failures[0].Source);
        Assert.Contains("token expired", result.Failures[0].Message);
    }

    [Fact]
    public async Task Aggregate_AllConnectorsFail_ReturnsOnlyFailures()
    {
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new StubConnector(new ConnectorException("bad token")),
            new StubConnector(new InvalidOperationException("boom"))
        });

        var result = await aggregator.AggregateAsync();

        Assert.Empty(result.Sources);
        Assert.Empty(result.Features);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public async Task Aggregate_ConnectorWithNoFeatures_IsTreatedAsFailure()
    {
        var aggregator = new ProfileAggregator(new IConnector[]
        {
            new StubConnector(new ProfileData("Silent", Array.Empty<string>())),
            new StubConnector(new ProfileData("Good", new[] { "x:1" }))
        });

        var result = await aggregator.AggregateAsync();

        Assert.Equal(new[] { "Good" }, result.Sources);
        Assert.Single(result.Failures);
        Assert.Contains("no interest data", result.Failures[0].Message);
    }

    [Fact]
    public void CombineRaw_EqualsSignatureOfUnion()
    {
        var gen = new FingerprintGenerator(64);
        var setA = Enumerable.Range(0, 30).Select(i => $"a:{i}").ToArray();
        var setB = Enumerable.Range(0, 25).Select(i => $"b:{i}").Concat(setA.Take(10)).ToArray();

        var combined = FingerprintGenerator.CombineRaw(new[] { gen.GenerateRaw(setA), gen.GenerateRaw(setB) });
        var union = gen.GenerateRaw(setA.Concat(setB).Distinct());

        Assert.Equal(union, combined);
    }

    [Fact]
    public void CombineRaw_EmptySignatureIsIdentity()
    {
        var gen = new FingerprintGenerator(32);
        var raw = gen.GenerateRaw(new[] { "x:1", "x:2" });
        var combined = FingerprintGenerator.CombineRaw(new[] { raw, gen.GenerateRaw(Array.Empty<string>()) });
        Assert.Equal(raw, combined);
    }

    [Fact]
    public void FromRaw_MatchesGenerate()
    {
        var gen = new FingerprintGenerator(64);
        var features = new[] { "lang:csharp", "topic:dotnet", "genre:fiction" };
        Assert.Equal(gen.Generate(features).Signature, FingerprintGenerator.FromRaw(gen.GenerateRaw(features)).Signature);
    }

    [Fact]
    public void EmptyFingerprint_IsEmpty()
    {
        var gen = new FingerprintGenerator(32);
        Assert.True(gen.Generate(Array.Empty<string>()).IsEmpty);
        Assert.False(gen.Generate(new[] { "a" }).IsEmpty);
    }

    [Fact]
    public void Matcher_ExcludesEmptyFingerprints()
    {
        var gen = new FingerprintGenerator(32);
        var matcher = new UserMatcher();
        matcher.Add("1", gen.Generate(Array.Empty<string>()), "empty-alice");
        matcher.Add("2", gen.Generate(Array.Empty<string>()), "empty-bob");
        matcher.Add("3", gen.Generate(new[] { "x:1" }), "carol");

        // Two empty fingerprints must not match each other at 100%.
        Assert.Empty(matcher.FindMatches("1"));

        // A real fingerprint must not see empty ones as candidates.
        Assert.Empty(matcher.FindMatches("3"));
    }

    [Fact]
    public void Matcher_RanksClosestUserFirst()
    {
        var gen = new FingerprintGenerator(64);
        var shared = Enumerable.Range(0, 40).Select(i => $"common:{i}").ToArray();
        var matcher = new UserMatcher();
        matcher.Add("me", gen.Generate(shared), "me");
        matcher.Add("close", gen.Generate(shared.Take(35).ToArray()), "close");
        matcher.Add("far", gen.Generate(new[] { "other:1", "other:2" }), "far");

        var matches = matcher.FindMatches("me");

        Assert.Equal(2, matches.Count);
        Assert.Equal("close", matches[0].Username);
        Assert.True(matches[0].Similarity > matches[1].Similarity);
    }

    [Fact]
    public void Matcher_DropsMatchesBelowThreshold()
    {
        var gen = new FingerprintGenerator(128);
        var shared = Enumerable.Range(0, 40).Select(i => $"common:{i}").ToArray();
        var matcher = new UserMatcher();
        matcher.Add("me", gen.Generate(shared), "me");
        matcher.Add("close", gen.Generate(shared.Take(35).ToArray()), "close");
        matcher.Add("disjoint", gen.Generate(Enumerable.Range(0, 40).Select(i => $"other:{i}").ToArray()), "disjoint");

        // A high floor keeps only the genuinely-similar user.
        var strong = matcher.FindMatches("me", minSimilarity: 0.5);
        Assert.Single(strong);
        Assert.Equal("close", strong[0].Username);

        // The disjoint user is always at or below the noise floor.
        Assert.DoesNotContain(matcher.FindMatches("me", minSimilarity: 0.05), m => m.Username == "disjoint");
    }

    [Fact]
    public void Matcher_CandidateCountIgnoresSelfAndEmpty()
    {
        var gen = new FingerprintGenerator(32);
        var matcher = new UserMatcher();
        matcher.Add("me", gen.Generate(new[] { "x:1" }), "me");
        matcher.Add("other", gen.Generate(new[] { "y:1" }), "other");
        matcher.Add("empty", gen.Generate(Array.Empty<string>()), "empty");

        Assert.Equal(1, matcher.CandidateCount("me"));
    }

    [Theory]
    [InlineData(0.85, "high", "Strong match")]
    [InlineData(0.45, "medium", "Good match")]
    [InlineData(0.10, "low", "Some overlap")]
    public void MatchViewModel_TierLabelsReflectSimilarity(double sim, string tier, string label)
    {
        var vm = new Profiler.Web.ViewModels.MatchViewModel { Similarity = sim };
        Assert.Equal(tier, vm.Tier);
        Assert.Equal(label, vm.TierLabel);
    }

    [Theory]
    [InlineData(0, "updated today", false)]
    [InlineData(10, "updated this month", false)]
    [InlineData(60, "updated about 2 months ago", false)]
    [InlineData(200, "updated about 7 months ago", true)]
    [InlineData(500, "updated over a year ago", true)]
    public void MatchViewModel_ReportsFingerprintAge_AndFlagsStaleOnes(int daysOld, string label, bool stale)
    {
        var vm = new Profiler.Web.ViewModels.MatchViewModel
        {
            UpdatedAt = DateTime.UtcNow.AddDays(-daysOld)
        };

        Assert.Equal(label, vm.FreshnessLabel);
        Assert.Equal(stale, vm.IsStale);
    }

    private static IList<System.ComponentModel.DataAnnotations.ValidationResult> Validate(object model)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), results, true);
        return results;
    }

    [Fact]
    public void SourceStatus_IsStale_OnlyPastTheThreshold()
    {
        var days = Profiler.Web.ViewModels.SourceStatusViewModel.StaleAfterDays;

        var fresh = new Profiler.Web.ViewModels.SourceStatusViewModel { UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        var justInside = new Profiler.Web.ViewModels.SourceStatusViewModel { UpdatedAt = DateTime.UtcNow.AddDays(-days + 1) };
        var stale = new Profiler.Web.ViewModels.SourceStatusViewModel { UpdatedAt = DateTime.UtcNow.AddDays(-days - 1) };

        Assert.False(fresh.IsStale);
        Assert.False(justInside.IsStale);
        Assert.True(stale.IsStale);
    }

    [Fact]
    public void ProfileViewModel_AllowsEmptyAndWithinLimits()
    {
        Assert.Empty(Validate(new Profiler.Web.ViewModels.ProfileViewModel()));
        Assert.Empty(Validate(new Profiler.Web.ViewModels.ProfileViewModel
        {
            Bio = new string('a', 280),
            Contact = new string('b', 120)
        }));
    }

    [Fact]
    public void ProfileViewModel_RejectsOverlongFields()
    {
        Assert.NotEmpty(Validate(new Profiler.Web.ViewModels.ProfileViewModel { Bio = new string('a', 281) }));
        Assert.NotEmpty(Validate(new Profiler.Web.ViewModels.ProfileViewModel { Contact = new string('b', 121) }));
    }
}
