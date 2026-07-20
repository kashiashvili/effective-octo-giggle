using System.Text.Json;

namespace Profiler.Web.Profile;

public class ProfileFingerprint
{
    public int[] Signature { get; }
    public int NumHashes => Signature.Length;

    public ProfileFingerprint(int[] signature)
    {
        Signature = signature;
    }

    public double Similarity(ProfileFingerprint other)
    {
        if (Signature.Length == 0 || other.Signature.Length == 0) return 0.0;
        int len = Math.Min(Signature.Length, other.Signature.Length);
        int matches = 0;
        for (int i = 0; i < len; i++)
            if (Signature[i] == other.Signature[i]) matches++;
        return (double)matches / len;
    }

    public string ToJson() => JsonSerializer.Serialize(Signature);

    public static ProfileFingerprint FromJson(string json)
    {
        var arr = JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
        return new ProfileFingerprint(arr);
    }
}
