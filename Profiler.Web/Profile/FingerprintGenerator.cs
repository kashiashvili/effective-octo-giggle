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

    public FingerprintGenerator(int numHashes = 128)
    {
        _numHashes = numHashes;
        _params = new (ulong, ulong)[numHashes];
        for (int i = 0; i < numHashes; i++)
        {
            var seed = SHA256.HashData(Encoding.UTF8.GetBytes($"minhash-param-{i}"));
            ulong a = BitConverter.ToUInt64(seed, 0) % PRIME;
            ulong b = BitConverter.ToUInt64(seed, 8) % PRIME;
            _params[i] = (a == 0 ? 1 : a, b);
        }
    }

    private static ulong StrToInt(string feature)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(feature));
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
