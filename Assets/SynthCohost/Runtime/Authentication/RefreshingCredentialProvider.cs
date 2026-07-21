using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// Ensures the access token is still valid (or refreshed) before credentials are handed to
    /// the WebSocket handshake / reconnect path.
    /// </summary>
    public sealed class RefreshingCredentialProvider : ICohostCredentialProvider
    {
        private readonly RuntimeAuthSession session;
        private readonly Func<Uri> restBaseProvider;
        private readonly TimeSpan refreshSkew;
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);

        public RefreshingCredentialProvider(
            RuntimeAuthSession session,
            Func<Uri> restBaseProvider,
            TimeSpan? refreshSkew = null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.restBaseProvider = restBaseProvider ?? throw new ArgumentNullException(nameof(restBaseProvider));
            this.refreshSkew = refreshSkew ?? AuthHttpClient.DefaultRefreshSkew;
        }

        public event Action<AuthHttpResult> RefreshFailed;
        public event Action TokensRotated;

        public async Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            await EnsureFreshAccessTokenAsync(cancellationToken);
            return await session.GetCredentialsAsync(cancellationToken);
        }

        public async Task<bool> EnsureFreshAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!session.TryGetAccessToken(out var accessToken))
            {
                return false;
            }

            // If exp cannot be read, keep the current token and let the backend reject if needed.
            if (!JwtAccessTokenInspector.TryGetRefreshDueUtc(
                    accessToken,
                    refreshSkew,
                    out var refreshDueUtc))
            {
                return true;
            }

            if (refreshDueUtc > DateTimeOffset.UtcNow)
            {
                return true;
            }

            return await RefreshNowAsync(cancellationToken);
        }

        public async Task<bool> RefreshNowAsync(CancellationToken cancellationToken)
        {
            await refreshGate.WaitAsync(cancellationToken);
            try
            {
                if (!session.TryGetRefreshToken(out var refreshToken))
                {
                    var missing = AuthHttpResult.Fail(
                        AuthHttpOperation.Refresh,
                        "No refresh token is available.",
                        requiresLogin: true);
                    RefreshFailed?.Invoke(missing);
                    return false;
                }

                // Another waiter may have refreshed while we queued.
                if (session.TryGetAccessToken(out var currentAccess) &&
                    JwtAccessTokenInspector.TryGetRefreshDueUtc(
                        currentAccess,
                        refreshSkew,
                        out var due) &&
                    due > DateTimeOffset.UtcNow.AddSeconds(15))
                {
                    return true;
                }

                var restBase = restBaseProvider();
                if (restBase == null || !restBase.IsAbsoluteUri)
                {
                    var invalid = AuthHttpResult.Fail(
                        AuthHttpOperation.Refresh,
                        "Auth request failed: REST base URL is invalid.");
                    RefreshFailed?.Invoke(invalid);
                    return false;
                }

                var result = await AuthHttpClient.RefreshAsync(restBase, refreshToken, cancellationToken);
                if (!result.Succeeded)
                {
                    if (result.RequiresLogin || result.HttpStatus == 401)
                    {
                        session.Clear();
                    }

                    RefreshFailed?.Invoke(result);
                    return false;
                }

                session.ReplaceTokens(result.Tokens);
                TokensRotated?.Invoke();
                return true;
            }
            finally
            {
                refreshGate.Release();
            }
        }
    }
}
