using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    public abstract class AvatarBehaviorControllerBehaviour : MonoBehaviour, IAvatarBehaviorController
    {
        public abstract System.Threading.Tasks.Task<bool> ApplyAsync(
            Protocol.AvatarBehavior behavior,
            System.Threading.CancellationToken cancellationToken);
    }
}
