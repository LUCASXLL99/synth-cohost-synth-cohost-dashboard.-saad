using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    public sealed class AnimatorAvatarBehaviorAdapter : AvatarBehaviorControllerBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private string idleTrigger = "Idle";
        [SerializeField] private string listeningTrigger = "Listening";
        [SerializeField] private string thinkingTrigger = "Thinking";
        [SerializeField] private string speakingTrigger = "Speaking";
        [SerializeField] private string happyTrigger = "Happy";
        [SerializeField] private string celebrateTrigger = "Celebrate";

        public override Task<bool> ApplyAsync(AvatarBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (animator == null)
            {
                return Task.FromResult(false);
            }

            var trigger = GetTrigger(behavior);
            if (string.IsNullOrWhiteSpace(trigger) || !HasTrigger(animator, trigger))
            {
                return Task.FromResult(false);
            }

            animator.SetTrigger(trigger);
            return Task.FromResult(true);
        }

        private string GetTrigger(AvatarBehavior behavior)
        {
            switch (behavior)
            {
                case AvatarBehavior.Idle: return idleTrigger;
                case AvatarBehavior.Listening: return listeningTrigger;
                case AvatarBehavior.Thinking: return thinkingTrigger;
                case AvatarBehavior.Speaking: return speakingTrigger;
                case AvatarBehavior.Happy: return happyTrigger;
                case AvatarBehavior.Celebrate: return celebrateTrigger;
                default: throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null);
            }
        }

        private static bool HasTrigger(Animator target, string trigger)
        {
            var hash = Animator.StringToHash(trigger);
            foreach (var parameter in target.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
