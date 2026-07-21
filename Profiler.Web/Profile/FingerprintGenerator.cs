using System.Security.Cryptography;
using System.Text;

namespace Profiler.Web.Profile;

public class FingerprintGenerator
{
    private const ulong PRIME = (1UL << 61) - 1;

    /// <summary>Slot value meaning "no features hashed" — the identity element for min-merging.</summary>
    public const ulong EmptySlot = ulong.MaxValue;

    private readonly int _numHashes;
    private readonly (ulong a, ulong b)[] _params;
    private readonly byte[] _pepper;

    /// <param name="pepper">
    /// A secret belonging to the deployment, mixed into every hash. Without it the scheme is fully
    /// public: interest labels come from a small, guessable vocabulary (`language:python`,
    /// `genre:sci-fi`), and a MinHash slot holds the smallest hash over the set — so anyone holding
    /// the database could hash each guess and see which ones appear, recovering real interests from
    /// data that is supposed to be one-way. The pepper is what makes that attack require more than
    /// the database. It is never stored beside the signatures, and changing it invalidates every
    /// signature ever produced.
    /// </param>
    public FingerprintGenerator(int numHashes = 128, string? pepper = null)
    {
        _numHashes = numHashes;
        _pepper = Encoding.UTF8.GetBytes(pepper ?? "");
        _params = new (ulong, ulong)[numHashes];
        for (int i = 0; i < numHashes; i++)
        {
            // The hash family is derived under the pepper too, so a signature cannot be attacked by
            // reproducing the parameters from the published source alone.
            var seed = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"minhash-param-{i}"));
            ulong a = BitConverter.ToUInt64(seed, 0) % PRIME;
            ulong b = BitConverter.ToUInt64(seed, 8) % PRIME;
            _params[i] = (a == 0 ? 1 : a, b);
        }
    }

    /// <summary>
    /// A value that changes whenever the pepper does, and reveals nothing about it. Stored so the
    /// app can notice that every existing signature was built under a different secret and is now
    /// meaningless, rather than silently matching nobody against anybody.
    /// </summary>
    public string SchemeVerifier =>
        Convert.ToHexString(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"scheme-v1-{_numHashes}")));

    private ulong StrToInt(string feature)
    {
        var bytes = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(feature));
        return BitConverter.ToUInt64(bytes, 0);
    }

    private static ulong HashValue(ulong featureInt, ulong a, ulong b)
    {
        // Using 128-bit intermediary via BigInteger to avoid overflow with Mersenne prime
        var product = (System.Numerics.BigInteger)a * featureInt;
        product = (product + b) % PRIME;
        return (ulong)product;
    }

    /// <summary>
    /// Full-width MinHash signature. Unlike the truncated <see cref="ProfileFingerprint"/>,
    /// raw signatures preserve ordering, so the signature of a union of feature sets is the
    /// element-wise minimum of the individual signatures. Empty input yields all EmptySlot.
    /// </summary>
    public ulong[] GenerateRaw(IEnumerable<string> features)
    {
        var signature = new ulong[_numHashes];
        Array.Fill(signature, EmptySlot);

        var featureInts = features.Select(StrToInt).ToArray();
        if (featureInts.Length == 0)
            return signature;

        for (int i = 0; i < _numHashes; i++)
        {
            var (a, b) = _params[i];
            foreach (var fi in featureInts)
            {
                var h = HashValue(fi, a, b);
                if (h < signature[i]) signature[i] = h;
            }
        }

        return signature;
    }

    /// <summary>Element-wise minimum — the raw signature of the union of the underlying feature sets.</summary>
    public static ulong[] CombineRaw(IEnumerable<ulong[]> rawSignatures)
    {
        ulong[]? combined = null;
        foreach (var raw in rawSignatures)
        {
            if (combined == null)
            {
                combined = (ulong[])raw.Clone();
                continue;
            }
            for (int i = 0; i < combined.Length && i < raw.Length; i++)
                if (raw[i] < combined[i]) combined[i] = raw[i];
        }
        return combined ?? Array.Empty<ulong>();
    }

    /// <summary>
    /// Estimated Jaccard similarity between two raw signatures — used to compare a single source
    /// against the same source on someone else, which is what turns "41% similar" into "mostly on
    /// music". Slots left empty on both sides carry no evidence and are excluded rather than counted
    /// as agreement.
    /// </summary>
    public static double RawSimilarity(ulong[] a, ulong[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        if (len == 0) return 0.0;

        var compared = 0;
        var matches = 0;
        for (var i = 0; i < len; i++)
        {
            if (a[i] == EmptySlot && b[i] == EmptySlot) continue;
            compared++;
            if (a[i] == b[i]) matches++;
        }

        return compared == 0 ? 0.0 : (double)matches / compared;
    }

    /// <summary>Truncate a raw signature into the stored/compared form. EmptySlot maps to 0.</summary>
    public static ProfileFingerprint FromRaw(ulong[] raw)
    {
        var signature = new int[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            signature[i] = raw[i] == EmptySlot ? 0 : (int)(raw[i] & 0x7FFFFFFF);
        return new ProfileFingerprint(signature);
    }

    public ProfileFingerprint Generate(IEnumerable<string> features) => FromRaw(GenerateRaw(features));
}
