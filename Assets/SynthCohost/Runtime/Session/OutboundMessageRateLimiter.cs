using System;
using System.Collections.Generic;

namespace SynthCohost.Runtime.Session
{
    /// <summary>
    /// Reserves outbound application-message capacity for one WebSocket connection.
    /// Rejected reservations are never queued or replayed.
    /// </summary>
    public sealed class OutboundMessageRateLimiter
    {
        public const int CurrentV2MaximumMessages = 60;
        public static readonly TimeSpan CurrentV2Window = TimeSpan.FromSeconds(10);

        private readonly object sync = new object();
        private readonly Queue<long> reservations = new Queue<long>();
        private readonly IMonotonicClock clock;
        private readonly int maximumMessages;
        private readonly long windowTicks;
        private long lastObservedTicks;

        public OutboundMessageRateLimiter()
            : this(CurrentV2MaximumMessages, CurrentV2Window, new StopwatchMonotonicClock())
        {
        }

        public OutboundMessageRateLimiter(IMonotonicClock clock)
            : this(CurrentV2MaximumMessages, CurrentV2Window, clock)
        {
        }

        public OutboundMessageRateLimiter(
            int maximumMessages,
            TimeSpan window,
            IMonotonicClock clock)
        {
            if (maximumMessages <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMessages));
            }

            if (window <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(window));
            }

            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.maximumMessages = maximumMessages;
            windowTicks = window.Ticks;
            lastObservedTicks = clock.Elapsed.Ticks;
        }

        public int MaximumMessages => maximumMessages;
        public TimeSpan Window => TimeSpan.FromTicks(windowTicks);

        /// <summary>
        /// Atomically reserves one message in the active rolling window.
        /// </summary>
        /// <param name="retryAfter">
        /// Zero when granted; otherwise the minimum delay before the oldest
        /// reservation expires under the currently observed clock value.
        /// </param>
        public bool TryReserve(out TimeSpan retryAfter)
        {
            lock (sync)
            {
                var nowTicks = ObserveClock();
                RemoveExpiredReservations(nowTicks);

                if (reservations.Count >= maximumMessages)
                {
                    var ageTicks = nowTicks - reservations.Peek();
                    var remainingTicks = windowTicks - ageTicks;
                    retryAfter = TimeSpan.FromTicks(Math.Max(remainingTicks, 0L));
                    return false;
                }

                reservations.Enqueue(nowTicks);
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }

        /// <summary>
        /// Clears all reservations. Call once when a new socket connection is created.
        /// </summary>
        public void Reset()
        {
            lock (sync)
            {
                reservations.Clear();
                lastObservedTicks = clock.Elapsed.Ticks;
            }
        }

        private long ObserveClock()
        {
            var observedTicks = clock.Elapsed.Ticks;
            if (observedTicks < lastObservedTicks)
            {
                // Preserve the safety limit even if a faulty injected clock regresses.
                return lastObservedTicks;
            }

            lastObservedTicks = observedTicks;
            return observedTicks;
        }

        private void RemoveExpiredReservations(long nowTicks)
        {
            while (reservations.Count > 0 && nowTicks - reservations.Peek() >= windowTicks)
            {
                reservations.Dequeue();
            }
        }
    }
}
