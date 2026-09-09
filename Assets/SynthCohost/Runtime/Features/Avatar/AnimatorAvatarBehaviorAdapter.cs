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

            if (!TryGetTrigger(behavior, out var trigger) ||
                string.IsNullOrWhiteSpace(trigger) ||
                !HasTrigger(animator, trigger))
            {
                return Task.FromResult(false);
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.ResetTrigger(parameter.nameHash);
                }
            }

            animator.SetTrigger(trigger);
            return Task.FromResult(true);
        }

        private bool TryGetTrigger(AvatarBehavior behavior, out string trigger)
        {
            switch (behavior)
            {
                case AvatarBehavior.Idle:
                    trigger = idleTrigger;
                    return true;
                case AvatarBehavior.Listening:
                    trigger = listeningTrigger;
                    return true;
                case AvatarBehavior.Thinking:
                    trigger = thinkingTrigger;
                    return true;
                case AvatarBehavior.Speaking:
                    trigger = speakingTrigger;
                    return true;
                case AvatarBehavior.Happy:
                    trigger = happyTrigger;
                    return true;
                case AvatarBehavior.Celebrate:
                    trigger = celebrateTrigger;
                    return true;
                default:
                    trigger = null;
                    return false;
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
