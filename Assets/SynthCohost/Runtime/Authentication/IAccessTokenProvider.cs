using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    public interface IAccessTokenProvider
    {
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
    }
}
