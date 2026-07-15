using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Authentication
{
    public interface IAvatarIdProvider
    {
        Task<Guid> GetAvatarIdAsync(CancellationToken cancellationToken);
    }
}
