using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The product tells people their interests cannot be read back out of what is stored. That claim is
/// about someone holding the database, so it has to survive the obvious attack: the interest
/// vocabulary is small and guessable by construction (`language:python`, `genre:sci-fi`), so an
/// attacker can hash every candidate label and look for it in the stored signature.
///
/// A MinHash slot holds the smallest hash over the feature set, so a candidate whose hash equals a
/// stored slot value is almost certainly a feature the person actually had. Without a secret in the
/// hash, that test can be run offline by anyone with a copy of the database.
/// </summary>
public class FingerprintIrreversibilityTests
{
    /// <summary>Every label an attacker might guess — small, because real vocabularies are small.</summary>
    private static string[] Dictionary() =>
        Enumerable.Range(0, 500).Select(i => $"language:candidate-{i}")
            .Concat(Enumerable.Range(0, 500).Select(i => $"genre:candidate-{i}"))
            .ToArray();

    /// <summary>
    /// Recovers the labels an attacker can prove were in the profile: those whose hash appears
    /// verbatim in a stored slot.
    /// </summary>
    private static HashSet<string> Attack(FingerprintGenerator attackerGenerator, ulong[] storedSignature, IEnumerable<string> dictionary)
    {
        var slots = storedSignature.ToHashSet();
        var recovered = new HashSet<string>();

        foreach (var candidate in dictionary)
        {
            // The attacker hashes one candidate at a time; a single-feature signature is exactly
            // that feature's hash in every slot.
            var probe = attackerGenerator.GenerateRaw(new[] { candidate });
            if (probe.Any(slots.Contains)) recovered.Add(candidate);
        }

        return recovered;
    }

    [Fact]
    public void AStoredSignature_DoesNotGiveUpItsFeatures_ToADictionaryAttack()
    {
        var dictionary = Dictionary();
        var secretInterests = dictionary.Take(20).ToArray();

        // What the server stores, built exactly as SourcesController builds it.
        var stored = new FingerprintGenerator(pepper: "the-deployments-pepper").GenerateRaw(secretInterests);

        // The attacker has the database and the source code, but not the deployment's secret.
        var attacker = new FingerprintGenerator(pepper: "not-the-deployments-pepper");
        var recovered = Attack(attacker, stored, dictionary);

        Assert.Empty(recovered);
    }

    /// <summary>
    /// The attack itself has to be shown to work, or the assertion above could pass for the wrong
    /// reason — a typo in the dictionary would look identical to real protection.
    /// </summary>
    [Fact]
    public void TheAttackReallyWorks_WhenTheAttackerKnowsTheSecret()
    {
        var dictionary = Dictionary();
        var secretInterests = dictionary.Take(20).ToArray();

        var pepper = "the-deployments-pepper";
        var stored = new FingerprintGenerator(pepper: pepper).GenerateRaw(secretInterests);

        var recovered = Attack(new FingerprintGenerator(pepper: pepper), stored, dictionary);

        // With the secret in hand every interest falls out, which is precisely why it must be secret.
        Assert.Equal(secretInterests.OrderBy(s => s), recovered.OrderBy(s => s));
    }
}
