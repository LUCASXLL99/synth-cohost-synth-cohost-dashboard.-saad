using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// Process-memory auth session holding access + refresh tokens and the selected avatar.
    /// Both tokens are always replaced together after login/refresh (rotation).
    /// </summary>
    public sealed class RuntimeAuthSession : ICohostCredentialProvider, IAccessTokenProvider, IAvatarIdProvider
    {
        private readonly object gate = new object();

        [NonSerialized] private string accessToken;
        [NonSerialized] private string refreshToken;
        [NonSerialized] private Guid avatarId;

        public event Action TokensChanged;
        public event Action Cleared;

        public bool HasAccessCredentials
        {
            get
            {
                lock (gate)
                {
                    return !string.IsNullOrWhiteSpace(accessToken) && avatarId != Guid.Empty;
                }
            }
        }

        public bool HasRefreshToken
        {
            get
            {
                lock (gate)
                {
                    return !string.IsNullOrWhiteSpace(refreshToken);
                }
            }
        }

        public void SetSession(string access, string refresh, Guid avatar)
        {
            var pair = new AuthTokenPair(access, refresh);
            if (avatar == Guid.Empty)
            {
                throw new ArgumentException("A non-empty avatar ID is required.", nameof(avatar));
            }

            lock (gate)
            {
                accessToken = pair.AccessToken;
                refreshToken = pair.RefreshToken;
                avatarId = avatar;
            }

            TokensChanged?.Invoke();
        }

        public void ReplaceTokens(AuthTokenPair pair)
        {
            lock (gate)
            {
                if (avatarId == Guid.Empty)
                {
                    throw new InvalidOperationException("Cannot replace tokens before an avatar is selected.");
                }

                accessToken = pair.AccessToken;
                refreshToken = pair.RefreshToken;
            }

            TokensChanged?.Invoke();
        }

        public void Clear()
        {
            lock (gate)
            {
                accessToken = null;
                refreshToken = null;
                avatarId = Guid.Empty;
            }

            Cleared?.Invoke();
        }

        public bool TryGetRefreshToken(out string refresh)
        {
            lock (gate)
            {
                refresh = refreshToken;
                return !string.IsNullOrWhiteSpace(refreshToken);
            }
        }

        public bool TryGetAccessToken(out string access)
        {
            lock (gate)
            {
                access = accessToken;
                return !string.IsNullOrWhiteSpace(accessToken);
            }
        }

        public Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(accessToken) || avatarId == Guid.Empty)
                {
                    throw new InvalidOperationException("Auth session credentials have not been supplied.");
                }

                return Task.FromResult(new CohostCredentials(accessToken, avatarId));
            }
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return (await GetCredentialsAsync(cancellationToken)).AccessToken;
        }

        public async Task<Guid> GetAvatarIdAsync(CancellationToken cancellationToken)
        {
            return (await GetCredentialsAsync(cancellationToken)).AvatarId;
        }
    }
}
