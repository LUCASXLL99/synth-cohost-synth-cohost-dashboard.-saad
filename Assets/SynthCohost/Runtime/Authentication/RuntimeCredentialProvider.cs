using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// Process-memory-only credentials. This is deliberately not a UnityEngine.Object and has no
    /// serialized fields, so tokens cannot be written into a scene, prefab, or settings asset.
    /// </summary>
    public sealed class RuntimeCredentialProvider : ICohostCredentialProvider, IAccessTokenProvider, IAvatarIdProvider
    {
        private readonly object gate = new object();

        [NonSerialized] private string accessToken;
        [NonSerialized] private Guid avatarId;

        public bool HasCredentials
        {
            get
            {
                lock (gate)
                {
                    return !string.IsNullOrWhiteSpace(accessToken) && avatarId != Guid.Empty;
                }
            }
        }

        public void SetCredentials(string token, Guid id)
        {
            var credentials = new CohostCredentials(token, id);
            lock (gate)
            {
                accessToken = credentials.AccessToken;
                avatarId = credentials.AvatarId;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                accessToken = null;
                avatarId = Guid.Empty;
            }
        }

        public Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(accessToken) || avatarId == Guid.Empty)
                {
                    throw new InvalidOperationException("Runtime credentials have not been supplied.");
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
