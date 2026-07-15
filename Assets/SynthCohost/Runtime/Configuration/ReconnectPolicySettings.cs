using System;
using UnityEngine;

namespace SynthCohost.Runtime.Configuration
{
    [Serializable]
    public sealed class ReconnectPolicySettings
    {
        [SerializeField, Min(0.1f)] private float baseDelaySeconds = 1f;
        [SerializeField, Min(0.1f)] private float maximumDelaySeconds = 30f;
        [SerializeField, Range(0f, 1f)] private float jitterRatio = 0.2f;
        [SerializeField, Min(0)] private int maximumAttempts;

        public TimeSpan BaseDelay => TimeSpan.FromSeconds(Math.Max(0.1f, baseDelaySeconds));
        public TimeSpan MaximumDelay => TimeSpan.FromSeconds(Math.Max(baseDelaySeconds, maximumDelaySeconds));
        public float JitterRatio => Mathf.Clamp01(jitterRatio);

        /// <summary>Zero means that transient reconnect attempts are not count-limited.</summary>
        public int MaximumAttempts => Math.Max(0, maximumAttempts);

        public ReconnectPolicySnapshot CreateSnapshot()
        {
            return new ReconnectPolicySnapshot(BaseDelay, MaximumDelay, JitterRatio, MaximumAttempts);
        }

        internal void Validate()
        {
            baseDelaySeconds = Mathf.Max(0.1f, baseDelaySeconds);
            maximumDelaySeconds = Mathf.Max(baseDelaySeconds, maximumDelaySeconds);
            jitterRatio = Mathf.Clamp01(jitterRatio);
            maximumAttempts = Mathf.Max(0, maximumAttempts);
        }
    }

    public readonly struct ReconnectPolicySnapshot
    {
        public ReconnectPolicySnapshot(
            TimeSpan baseDelay,
            TimeSpan maximumDelay,
            float jitterRatio,
            int maximumAttempts)
        {
            BaseDelay = baseDelay;
            MaximumDelay = maximumDelay;
            JitterRatio = jitterRatio;
            MaximumAttempts = maximumAttempts;
        }

        public TimeSpan BaseDelay { get; }
        public TimeSpan MaximumDelay { get; }
        public float JitterRatio { get; }
        public int MaximumAttempts { get; }
    }
}
