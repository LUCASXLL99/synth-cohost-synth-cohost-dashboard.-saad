using System;
using SynthCohost.Runtime.Configuration;

namespace SynthCohost.Runtime.Session
{
    public interface IRandomSource
    {
        double NextUnit();
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random random = new Random();
        private readonly object gate = new object();

        public double NextUnit()
        {
            lock (gate)
            {
                return random.NextDouble();
            }
        }
    }

    public sealed class ReconnectController
    {
        private readonly ReconnectPolicySnapshot policy;
        private readonly IRandomSource random;

        public ReconnectController(ReconnectPolicySnapshot policy, IRandomSource random = null)
        {
            this.policy = policy;
            this.random = random ?? new SystemRandomSource();
        }

        public bool CanAttempt(int attempt)
        {
            return attempt > 0 && (policy.MaximumAttempts == 0 || attempt <= policy.MaximumAttempts);
        }

        public TimeSpan GetDelay(int attempt, TimeSpan minimumDelay = default)
        {
            if (attempt <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt));
            }

            var exponent = Math.Min(30, attempt - 1);
            var seconds = policy.BaseDelay.TotalSeconds * Math.Pow(2d, exponent);
            seconds = Math.Min(seconds, policy.MaximumDelay.TotalSeconds);

            var jitterOffset = (random.NextUnit() * 2d - 1d) * policy.JitterRatio;
            seconds = Math.Max(0d, seconds * (1d + jitterOffset));
            seconds = Math.Max(seconds, minimumDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
