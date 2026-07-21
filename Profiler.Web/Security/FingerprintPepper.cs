namespace Profiler.Web.Security;

/// <summary>
/// Supplies the secret mixed into every fingerprint hash.
///
/// Without it the fingerprint scheme is entirely public, and since interest labels come from a small
/// guessable vocabulary, a copy of the database is enough to test guesses against the stored
/// signatures and recover what someone was actually interested in. That would contradict the one
/// promise the product is built on, so outside Development the app refuses to start without a
/// pepper rather than run in a state where the promise is untrue.
///
/// Treat it like the Data Protection key ring: set it once, keep it, and never commit it. Changing
/// it invalidates every signature ever produced — the raw interests needed to rebuild them are
/// deliberately gone, so everyone has to reconnect their sources.
/// </summary>
public static class FingerprintPepper
{
    public const string ConfigKey = "Fingerprint:Pepper";

    /// <summary>
    /// Development runs on a fixed, published value: a dev machine has no secrets worth protecting,
    /// and a random one per run would throw away the local database's fingerprints on every restart.
    /// </summary>
    public const string DevelopmentPepper = "development-only-pepper-not-for-deployment";

    public static string Resolve(IConfiguration configuration, bool isDevelopment)
    {
        var configured = configuration[ConfigKey];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!isDevelopment && configured == DevelopmentPepper)
            {
                throw new InvalidOperationException(
                    $"{ConfigKey} is set to the published development value. It is in the source code, " +
                    "so it protects nothing. Generate a random secret for this deployment.");
            }
            return configured;
        }

        if (isDevelopment) return DevelopmentPepper;

        throw new InvalidOperationException(
            $"{ConfigKey} is not set. Fingerprints would then be hashed with a scheme that is entirely " +
            "public, so anyone with a copy of the database could test a list of guessed interests " +
            "against the stored signatures and recover real ones — which is exactly what this product " +
            "promises is impossible. Set it to a random secret (for example the output of " +
            "`openssl rand -base64 32`), keep it for the life of the deployment, and never commit it.");
    }
}
