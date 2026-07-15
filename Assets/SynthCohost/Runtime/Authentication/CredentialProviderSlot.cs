using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>Lets login/refresh code replace the runtime provider without rebuilding the socket client.</summary>
    public sealed class CredentialProviderSlot : ICohostCredentialProvider
    {
        private readonly object gate = new object();
        private ICohostCredentialProvider target;

        public void Set(ICohostCredentialProvider provider)
        {
            lock (gate)
            {
                target = provider ?? throw new ArgumentNullException(nameof(provider));
            }
        }

        public Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            ICohostCredentialProvider current;
            lock (gate)
            {
                current = target;
            }

            if (current == null)
            {
                throw new InvalidOperationException("A runtime credential provider has not been supplied.");
            }

            return current.GetCredentialsAsync(cancellationToken);
        }
    }
}
