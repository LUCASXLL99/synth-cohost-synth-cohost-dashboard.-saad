using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    public interface ICohostCredentialProvider
    {
        Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken);
    }
}
