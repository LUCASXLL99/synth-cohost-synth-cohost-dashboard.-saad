using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    /// <summary>
    /// Development-only avatar adapter used by the SampleScene live-test panel. It records every
    /// requested behavior as an applied state so the client can exercise state.ack without a final
    /// avatar Animator being present.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Development/Live Test Avatar Adapter")]
    public sealed class LiveTestAvatarBehaviorAdapter : AvatarBehaviorControllerBehaviour
    {
        public event Action<AvatarBehavior> BehaviorApplied;

        public bool HasBehavior { get; private set; }
        public AvatarBehavior LastBehavior { get; private set; }

        public override Task<bool> ApplyAsync(
            AvatarBehavior behavior,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasBehavior = true;
            LastBehavior = behavior;
            BehaviorApplied?.Invoke(behavior);
            return Task.FromResult(true);
        }
    }
}
