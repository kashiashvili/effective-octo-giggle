using System.Text;

namespace Profiler.Web.Profile;

/// <summary>One selectable interest: a stable slug (what gets hashed) and a human label.</summary>
public record InterestTag(string Slug, string Display);

/// <summary>A themed group of interests. <see cref="Prefix"/> namespaces the feature strings so two
/// people who tick the same interest emit the identical feature and therefore collide in MinHash,
/// while the prefix also lets <see cref="InterestLens"/> theme the result.</summary>
public record InterestCategory(string Theme, string Prefix, IReadOnlyList<InterestTag> Tags);

/// <summary>
/// The catalog behind self-described interests. Most people cannot use the token/API connectors —
/// they are for developers — so a curated pick-list is the only way a normal, privacy-conscious user
/// (or anyone unwilling to connect an account) can produce a real interest fingerprint and be matched.
///
/// A chosen tag becomes the feature string "<c>{prefix}:{slug}</c>", run through the same
/// <see cref="FingerprintGenerator"/> pipeline as any connector; the selection itself is discarded
/// after the signature is built, exactly like raw connector data. The list is intentionally broad but
/// finite: matching only works when two people can land on the *same* label, so free text is deferred.
/// </summary>
public static class InterestCatalog
{
    public static readonly IReadOnlyList<InterestCategory> Categories = new List<InterestCategory>
    {
        new("Programming & tech", "self-tech", new List<InterestTag>
        {
            new("python", "Python"), new("rust", "Rust"), new("typescript", "TypeScript"),
            new("go", "Go"), new("cpp", "C++"), new("functional-programming", "Functional programming"),
            new("machine-learning", "Machine learning"), new("llms", "Large language models"),
            new("web-dev", "Web development"), new("game-dev", "Game development"),
            new("embedded", "Embedded / hardware"), new("devops", "DevOps & infra"),
            new("databases", "Databases"), new("security", "Security & hacking"),
            new("open-source", "Open source"), new("data-science", "Data science"),
            new("distributed-systems", "Distributed systems"), new("compilers", "Compilers & languages"),
        }),
        new("Music", "self-music", new List<InterestTag>
        {
            new("indie-rock", "Indie rock"), new("metal", "Metal"), new("jazz", "Jazz"),
            new("hip-hop", "Hip-hop"), new("electronic", "Electronic"), new("techno", "Techno"),
            new("classical", "Classical"), new("folk", "Folk"), new("punk", "Punk"),
            new("ambient", "Ambient"), new("shoegaze", "Shoegaze"), new("k-pop", "K-pop"),
            new("jazz-fusion", "Jazz fusion"), new("post-rock", "Post-rock"), new("soul-funk", "Soul & funk"),
            new("vinyl", "Vinyl collecting"), new("music-production", "Making music"),
            new("live-gigs", "Live gigs"),
        }),
        new("Reading & writing", "self-reading", new List<InterestTag>
        {
            new("sci-fi", "Science fiction"), new("fantasy", "Fantasy"), new("literary-fiction", "Literary fiction"),
            new("history", "History"), new("philosophy", "Philosophy"), new("poetry", "Poetry"),
            new("mystery-crime", "Mystery & crime"), new("non-fiction", "Non-fiction"),
            new("comics-graphic", "Comics & graphic novels"), new("horror", "Horror"),
            new("biography", "Biography & memoir"), new("popular-science", "Popular science"),
            new("creative-writing", "Creative writing"), new("classics", "Classics"),
            new("essays", "Essays"), new("book-clubs", "Book clubs"),
        }),
        new("Film & TV", "self-screen", new List<InterestTag>
        {
            new("arthouse", "Arthouse cinema"), new("horror-film", "Horror films"),
            new("documentaries", "Documentaries"), new("anime", "Anime"), new("sci-fi-film", "Sci-fi film & TV"),
            new("world-cinema", "World cinema"), new("classic-film", "Classic film"),
            new("stand-up", "Stand-up comedy"), new("true-crime", "True crime"),
            new("prestige-tv", "Prestige drama"), new("animation", "Animation"),
            new("film-photography-tv", "Cinematography"), new("indie-film", "Indie film"),
        }),
        new("Gaming", "self-gaming", new List<InterestTag>
        {
            new("indie-games", "Indie games"), new("rpgs", "RPGs"), new("strategy-games", "Strategy games"),
            new("fps", "Shooters"), new("roguelikes", "Roguelikes"), new("tabletop-rpg", "Tabletop RPGs"),
            new("board-games", "Board games"), new("retro-gaming", "Retro gaming"),
            new("sim-games", "Simulation games"), new("fighting-games", "Fighting games"),
            new("mmos", "MMOs"), new("speedrunning", "Speedrunning"), new("puzzle-games", "Puzzle games"),
            new("game-design", "Game design"),
        }),
        new("Outdoors & sport", "self-outdoors", new List<InterestTag>
        {
            new("climbing", "Climbing"), new("hiking", "Hiking"), new("running", "Running"),
            new("cycling", "Cycling"), new("bouldering", "Bouldering"), new("trail-running", "Trail running"),
            new("skiing-snowboarding", "Skiing & snowboarding"), new("surfing", "Surfing"),
            new("camping", "Camping & backpacking"), new("football-soccer", "Football"),
            new("basketball", "Basketball"), new("yoga", "Yoga"), new("swimming", "Swimming"),
            new("martial-arts", "Martial arts"), new("bouldering-gym", "Gym & lifting"),
            new("kayaking", "Kayaking & paddling"),
        }),
        new("Food & drink", "self-food", new List<InterestTag>
        {
            new("home-cooking", "Home cooking"), new("baking", "Baking"), new("sourdough", "Sourdough"),
            new("bbq", "BBQ & grilling"), new("fermentation", "Fermentation"), new("coffee", "Specialty coffee"),
            new("tea", "Tea"), new("craft-beer", "Craft beer"), new("wine", "Wine"),
            new("vegan-cooking", "Vegan cooking"), new("street-food", "Street food"),
            new("cocktails", "Cocktails"), new("cheese", "Cheese"), new("spicy-food", "Spicy food"),
        }),
        new("Art & making", "self-art", new List<InterestTag>
        {
            new("photography", "Photography"), new("drawing-painting", "Drawing & painting"),
            new("woodworking", "Woodworking"), new("knitting-textiles", "Knitting & textiles"),
            new("pottery", "Pottery & ceramics"), new("3d-printing", "3D printing"),
            new("electronics-maker", "Electronics & making"), new("illustration", "Illustration"),
            new("graphic-design", "Design"), new("gardening", "Gardening"),
            new("calligraphy", "Calligraphy"), new("leatherwork", "Leatherwork"),
            new("film-photography", "Film photography"), new("sculpture", "Sculpture"),
        }),
        new("Science & curiosity", "self-science", new List<InterestTag>
        {
            new("astronomy", "Astronomy"), new("physics", "Physics"), new("biology", "Biology & nature"),
            new("psychology", "Psychology"), new("economics", "Economics"), new("linguistics", "Linguistics"),
            new("mathematics", "Mathematics"), new("space-exploration", "Space exploration"),
            new("neuroscience", "Neuroscience"), new("climate", "Climate & environment"),
            new("chess", "Chess"), new("puzzles-logic", "Puzzles & logic"),
            new("history-of-science", "History of science"), new("birdwatching", "Birdwatching"),
        }),
    };

    /// <summary>Every legal feature string, for validating a POST — anything not here is ignored so a
    /// crafted form can never inject an arbitrary feature into the fingerprint.</summary>
    public static readonly IReadOnlySet<string> ValidFeatures =
        Categories.SelectMany(c => c.Tags.Select(t => $"{c.Prefix}:{t.Slug}")).ToHashSet();

    public static bool IsValidFeature(string? feature) => feature != null && ValidFeatures.Contains(feature);

    /// <summary>The feature string a tag emits, e.g. "self-music:jazz".</summary>
    public static string Feature(InterestCategory category, InterestTag tag) => $"{category.Prefix}:{tag.Slug}";

    // ---- Rarity weighting -------------------------------------------------------------------------
    //
    // Sharing a rare interest predicts a real connection far better than sharing a popular one — a
    // synthetic experiment (InterestWeightingExperimentTests) put the improvement at ~6x separation.
    // We weight rarity with a well-known trick that needs NO new stored data and no per-user frequency
    // table (which would be retained state against the north star): "feature replication". A weight-w
    // interest is expanded into w distinct sub-features before hashing, so two people who share a rare
    // (heavy) interest agree on more MinHash slots than two who share a common (light) one. Because the
    // weight is a property of the interest, both people expand it identically, so the replicas line up.
    //
    // Weights are authored statically here, not learned from users — that is the whole point: the
    // catalog is finite, so its rarity is a design fact, not runtime data. Broad, popular interests are
    // light; niche ones are heavy; everything else is neutral. (Connector features carry no catalog
    // weight and stay at 1 for now — down-weighting open-vocabulary connector commons would need a
    // frequency oracle, which is deliberately deferred.)
    private const int CommonWeight = 1;
    private const int NeutralWeight = 2;
    private const int RareWeight = 3;

    // Broad, popular interests — a shared one is weak evidence, so it counts least.
    private static readonly IReadOnlySet<string> CommonSlugs = new HashSet<string>
    {
        "python", "web-dev", "machine-learning", "sci-fi", "fantasy", "history", "philosophy",
        "jazz", "hip-hop", "electronic", "classical", "indie-rock", "hiking", "running", "cycling",
        "yoga", "home-cooking", "baking", "coffee", "photography", "gardening", "astronomy", "chess",
        "sci-fi-film", "documentaries", "anime", "non-fiction", "indie-games", "rpgs",
    };

    // Niche interests — a shared one is strong evidence of a real overlap, so it counts most.
    private static readonly IReadOnlySet<string> RareSlugs = new HashSet<string>
    {
        "compilers", "distributed-systems", "embedded", "functional-programming",
        "shoegaze", "post-rock", "jazz-fusion", "soul-funk", "vinyl", "music-production",
        "bouldering", "trail-running", "kayaking", "martial-arts",
        "fermentation", "sourdough", "cheese", "cocktails",
        "calligraphy", "leatherwork", "sculpture", "film-photography", "pottery", "3d-printing",
        "linguistics", "neuroscience", "history-of-science", "birdwatching",
        "roguelikes", "tabletop-rpg", "mmos", "speedrunning", "fighting-games",
        "poetry", "world-cinema", "arthouse",
    };

    /// <summary>Replication weight for a catalog slug: rarer interests weigh more.</summary>
    public static int WeightOfSlug(string slug) =>
        RareSlugs.Contains(slug) ? RareWeight :
        CommonSlugs.Contains(slug) ? CommonWeight :
        NeutralWeight;

    // Feature string -> weight, precomputed once for the whole catalog.
    private static readonly IReadOnlyDictionary<string, int> FeatureWeights =
        Categories.SelectMany(c => c.Tags.Select(t => (Feature: Feature(c, t), Weight: WeightOfSlug(t.Slug))))
                  .ToDictionary(x => x.Feature, x => x.Weight);

    // ---- Cross-source bridging --------------------------------------------------------------------
    //
    // Self-described features live in their own `self-*` namespace, so without this a self-describer and
    // a connector user who love the exact same thing (both Python) would NEVER match — the two pools are
    // disjoint, which fragments match density (the whole reason self-described exists is to grow the
    // matchable pool, not split it). For the concepts with an unambiguous connector equivalent we emit
    // the *canonical* connector string INSTEAD of the self-* one, so the interest lands in the shared
    // vocabulary and matches across sources. Emitting instead of (not in addition to) the self-* string
    // avoids double-counting one interest, which would inflate self-to-self similarity and undo the
    // rarity weighting. Only rock-solid 1:1 mappings live here — GitHub always emits a lowercased
    // `language:*` for every repo. Fuzzy concepts (music/film genres, where each platform uses its own
    // vocabulary) are deliberately left un-bridged for now.
    private static readonly IReadOnlyDictionary<string, string> CanonicalBridge = new Dictionary<string, string>
    {
        ["self-tech:python"] = "language:python",
        ["self-tech:rust"] = "language:rust",
        ["self-tech:typescript"] = "language:typescript",
        ["self-tech:go"] = "language:go",
        ["self-tech:cpp"] = "language:c++",
    };

    /// <summary>
    /// Map bridged self-described features to the canonical connector string they should match on, so
    /// self-described and connector fingerprints can overlap. Non-bridged features pass through. 1:1, so
    /// the count is unchanged. A bridged feature hashes as its `language:*` form, which is not in the
    /// rarity table, so it takes weight 1 — regardless of the tag's catalog rarity. That is required,
    /// not incidental: the connector emits the same `language:*` at weight 1, and the two signatures only
    /// align if both sides replicate it the same number of times.
    /// </summary>
    public static List<string> Canonicalize(IEnumerable<string> features) =>
        features.Select(f => CanonicalBridge.TryGetValue(f, out var canonical) ? canonical : f).ToList();

    // ---- Free-text custom interests ---------------------------------------------------------------
    //
    // A fixed ~140-tag list cannot hold the genuinely niche interest that makes the best match — and
    // rarity weighting shows those rare shared interests carry the most signal. So let people type their
    // own. The hard part is that two people who mean the same thing must land on the same feature, so we
    // normalise aggressively (lowercase, collapse every run of non-alphanumerics to one hyphen, trim):
    // "Byzantine History!", "byzantine  history", "Byzantine-History" all become `byzantine-history`.
    // This resolves case/spacing/punctuation variance (the bulk of it); synonyms ("films" vs "movies")
    // are not resolved and are an accepted v1 limitation. A typed interest that matches a catalog concept
    // is mapped onto that concept's feature (so typing "python" unifies with picking Python and with a
    // GitHub user), otherwise it becomes `interest:<slug>` — the shared "Communities & topics" namespace.
    // The text is only ever hashed and discarded, never stored or rendered, so it carries no XSS risk.
    public const int MaxCustomInterests = 25;
    private const int MinCustomSlugLength = 2;
    private const int MaxCustomSlugLength = 40;

    // Reverse map: a normalized slug -> the (canonicalized) feature the catalog would emit for it. Slugs
    // are unique across the catalog, so this is unambiguous.
    private static readonly IReadOnlyDictionary<string, string> SlugToFeature =
        Categories.SelectMany(c => c.Tags.Select(t => (t.Slug, Feature: Canonicalize(new[] { Feature(c, t) })[0])))
                  .ToDictionary(x => x.Slug, x => x.Feature);

    // Common interests whose meaning lives entirely in the symbols the normalizer strips: "C++" would
    // collapse to "c" and vanish. Map the whole-string cases to a clean slug that survives — and that
    // unifies with the catalog where one exists (cpp -> the C++ tag -> language:c++).
    private static readonly IReadOnlyDictionary<string, string> CustomAliases = new Dictionary<string, string>
    {
        ["c++"] = "cpp",
        ["c#"] = "csharp",
        ["f#"] = "fsharp",
        [".net"] = "dotnet",
    };

    /// <summary>Normalize a free-text interest to a stable slug, or null if it is too short to be real.</summary>
    public static string? NormalizeCustom(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Rescue symbol-defined tokens before the normalizer would strip them to nothing.
        var lowered = raw.Trim().ToLowerInvariant();
        if (CustomAliases.TryGetValue(lowered, out var alias)) return alias;
        var sb = new StringBuilder(lowered.Length);
        var lastHyphen = false;
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); lastHyphen = false; }
            else if (sb.Length > 0 && !lastHyphen) { sb.Append('-'); lastHyphen = true; }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxCustomSlugLength) slug = slug[..MaxCustomSlugLength].Trim('-');
        return slug.Length < MinCustomSlugLength ? null : slug;
    }

    /// <summary>
    /// Turn raw free-text lines into features: normalized, de-duplicated, capped, and mapped onto the
    /// catalog concept when one matches (so typed interests unify with picked ones) else `interest:*`.
    /// </summary>
    public static List<string> CustomFeatures(IEnumerable<string>? rawLines)
    {
        if (rawLines == null) return new List<string>();
        return rawLines
            .Select(NormalizeCustom)
            .Where(slug => slug != null)
            .Distinct()
            .Take(MaxCustomInterests)
            .Select(slug => SlugToFeature.TryGetValue(slug!, out var f) ? f : $"interest:{slug}")
            .ToList();
    }

    /// <summary>
    /// Expand chosen features by rarity weight for weighted MinHash: a weight-w feature becomes w
    /// distinct sub-features. Unknown features (e.g. connector-derived) get weight 1 — unchanged.
    /// Standard MinHash over the expanded set is a weighted MinHash of the originals.
    /// </summary>
    public static List<string> Expand(IEnumerable<string> features)
    {
        var expanded = new List<string>();
        foreach (var f in features)
        {
            var weight = FeatureWeights.TryGetValue(f, out var w) ? w : 1;
            expanded.Add(f);
            for (var i = 1; i < weight; i++)
                expanded.Add($"{f}#{i}");
        }
        return expanded;
    }
}
