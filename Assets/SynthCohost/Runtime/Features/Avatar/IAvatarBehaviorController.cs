using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Avatar
{
    public interface IAvatarBehaviorController
    {
        Task<bool> ApplyAsync(AvatarBehavior behavior, CancellationToken cancellationToken);
    }
}
