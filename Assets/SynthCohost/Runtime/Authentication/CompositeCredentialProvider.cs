using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    public sealed class CompositeCredentialProvider : ICohostCredentialProvider
    {
        private readonly IAccessTokenProvider accessTokenProvider;
        private readonly IAvatarIdProvider avatarIdProvider;

        public CompositeCredentialProvider(
            IAccessTokenProvider accessTokenProvider,
            IAvatarIdProvider avatarIdProvider)
        {
            this.accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
            this.avatarIdProvider = avatarIdProvider ?? throw new ArgumentNullException(nameof(avatarIdProvider));
        }

        public async Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            var token = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
            var avatarId = await avatarIdProvider.GetAvatarIdAsync(cancellationToken);
            return new CohostCredentials(token, avatarId);
        }
    }
}
