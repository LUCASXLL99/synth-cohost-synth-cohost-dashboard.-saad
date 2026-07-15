using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Avatar
{
    public sealed class NullAvatarBehaviorController : IAvatarBehaviorController
    {
        public Task<bool> ApplyAsync(AvatarBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }
}
