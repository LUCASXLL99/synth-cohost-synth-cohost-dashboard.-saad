using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Maps deployed-v2 <see cref="AvatarBehavior"/> values to Animator state
    /// names on the SampleScene AvatarVerify controller.
    /// </summary>
    public static class DashboardBehaviorStateMap
    {
        public const string Idle = "01_Idle_A_(Breathing)";
        public const string Listening = "Listening";
        public const string Thinking = "53_Thinking";
        public const string Speaking = "34_Curious_Lean"; // present-to-camera; mouth uses blendshapes + TTS
        public const string Happy = "46_Smile";
        public const string Celebrate = "48_cheer";

        public static bool TryGetStateName(AvatarBehavior behavior, out string stateName)
        {
            switch (behavior)
            {
                case AvatarBehavior.Idle:
                    stateName = Idle;
                    return true;
                case AvatarBehavior.Listening:
                    stateName = Listening;
                    return true;
                case AvatarBehavior.Thinking:
                    stateName = Thinking;
                    return true;
                case AvatarBehavior.Speaking:
                    stateName = Speaking;
                    return true;
                case AvatarBehavior.Happy:
                    stateName = Happy;
                    return true;
                case AvatarBehavior.Celebrate:
                    stateName = Celebrate;
                    return true;
                default:
                    stateName = null;
                    return false;
            }
        }

        public static bool IsLooping(AvatarBehavior behavior)
        {
            return behavior == AvatarBehavior.Idle
                || behavior == AvatarBehavior.Listening
                || behavior == AvatarBehavior.Thinking
                || behavior == AvatarBehavior.Speaking;
        }

        public static bool IsBurst(AvatarBehavior behavior)
        {
            return behavior == AvatarBehavior.Happy || behavior == AvatarBehavior.Celebrate;
        }
    }
}
