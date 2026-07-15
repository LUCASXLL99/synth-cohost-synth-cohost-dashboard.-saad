using System;
using System.Diagnostics;

namespace SynthCohost.Runtime.Session
{
    /// <summary>
    /// Supplies elapsed time that is unaffected by wall-clock corrections.
    /// </summary>
    public interface IMonotonicClock
    {
        TimeSpan Elapsed { get; }
    }

    public sealed class StopwatchMonotonicClock : IMonotonicClock
    {
        private readonly long startedAt = Stopwatch.GetTimestamp();

        public TimeSpan Elapsed
        {
            get
            {
                var elapsedTimestamp = Stopwatch.GetTimestamp() - startedAt;
                return TimeSpan.FromSeconds((double)elapsedTimestamp / Stopwatch.Frequency);
            }
        }
    }
}
