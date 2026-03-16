using System.Security.Cryptography;
using System.Text;

namespace Profiler.Web.Profile;

public class FingerprintGenerator
{
    private const ulong PRIME = (1UL << 61) - 1;
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

    public ProfileFingerprint Generate(IEnumerable<string> features)
    {
        var featureList = features.ToList();
        if (featureList.Count == 0)
            return new ProfileFingerprint(new int[_numHashes]);

        var featureInts = featureList.Select(StrToInt).ToArray();
        var signature = new int[_numHashes];

        for (int i = 0; i < _numHashes; i++)
        {
            var (a, b) = _params[i];
            ulong minHash = ulong.MaxValue;
            foreach (var fi in featureInts)
            {
                var h = HashValue(fi, a, b);
                if (h < minHash) minHash = h;
            }
            signature[i] = (int)(minHash & 0x7FFFFFFF);
        }

        return new ProfileFingerprint(signature);
    }
}
