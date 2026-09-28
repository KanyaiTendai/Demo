namespace Demo.Support;

/// <summary>
/// Thread-safe, run-scoped cache for the PayPal OAuth2 access token. Reqnroll creates a fresh
/// step-definition/client instance per scenario, so the token is cached here rather than on an
/// instance field, letting every scenario reuse a still-valid token instead of re-authenticating.
/// </summary>
public static class AccessTokenStore
{
    private static readonly object SyncRoot = new();
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private static string? _cachedAccessToken;
    private static DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public static bool TryGetValidToken(out string accessToken)
    {
        lock (SyncRoot)
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc - ExpiryBuffer)
            {
                accessToken = _cachedAccessToken;
                return true;
            }
        }

        accessToken = string.Empty;
        return false;
    }

    public static void Store(string accessToken, int expiresInSeconds)
    {
        lock (SyncRoot)
        {
            _cachedAccessToken = accessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            _cachedAccessToken = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
        }
    }
}
