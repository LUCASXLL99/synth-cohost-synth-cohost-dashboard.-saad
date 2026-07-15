using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class OutboundMessageRateLimiterTests
    {
        [Test]
        public void CurrentV2_AllowsSixtyMessagesAndRejectsTheNext()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(clock);

            for (var index = 0; index < 60; index++)
            {
                Assert.That(limiter.TryReserve(out var retryAfter), Is.True);
                Assert.That(retryAfter, Is.EqualTo(TimeSpan.Zero));
            }

            Assert.That(limiter.TryReserve(out var deniedRetryAfter), Is.False);
            Assert.That(deniedRetryAfter, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void ReservationAtExactWindowBoundary_IsAllowed()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(1, TimeSpan.FromSeconds(10), clock);

            Assert.That(limiter.TryReserve(out _), Is.True);
            clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(limiter.TryReserve(out var retryAfter), Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromMilliseconds(1)));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(limiter.TryReserve(out retryAfter), Is.True);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void SlidingWindow_ExpiresOnlyReservationsOldEnough()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(3, TimeSpan.FromSeconds(10), clock);

            Assert.That(limiter.TryReserve(out _), Is.True);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(limiter.TryReserve(out _), Is.True);
            Assert.That(limiter.TryReserve(out _), Is.True);

            clock.Advance(TimeSpan.FromSeconds(9));
            Assert.That(limiter.TryReserve(out _), Is.True, "The reservation at t=0 should expire.");
            Assert.That(limiter.TryReserve(out var retryAfter), Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void RejectedAttempt_DoesNotConsumeFutureCapacity()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(1, TimeSpan.FromSeconds(10), clock);

            Assert.That(limiter.TryReserve(out _), Is.True);
            for (var index = 0; index < 20; index++)
            {
                Assert.That(limiter.TryReserve(out _), Is.False);
            }

            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.That(limiter.TryReserve(out _), Is.True);
        }

        [Test]
        public void Reset_StartsAFullBudgetForANewConnection()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(2, TimeSpan.FromSeconds(10), clock);

            Assert.That(limiter.TryReserve(out _), Is.True);
            Assert.That(limiter.TryReserve(out _), Is.True);
            Assert.That(limiter.TryReserve(out _), Is.False);

            limiter.Reset();

            Assert.That(limiter.TryReserve(out _), Is.True);
            Assert.That(limiter.TryReserve(out _), Is.True);
        }

        [Test]
        public void ConcurrentReservations_NeverExceedLimit()
        {
            var clock = new ManualMonotonicClock();
            var limiter = new OutboundMessageRateLimiter(clock);
            var granted = 0;

            Parallel.For(0, 500, index =>
            {
                if (limiter.TryReserve(out _))
                {
                    Interlocked.Increment(ref granted);
                }
            });

            Assert.That(granted, Is.EqualTo(60));
        }

        [Test]
        public void RegressingClock_DoesNotBypassTheLimit()
        {
            var clock = new ManualMonotonicClock();
            clock.Advance(TimeSpan.FromSeconds(20));
            var limiter = new OutboundMessageRateLimiter(1, TimeSpan.FromSeconds(10), clock);

            Assert.That(limiter.TryReserve(out _), Is.True);
            clock.Set(TimeSpan.Zero);

            Assert.That(limiter.TryReserve(out var retryAfter), Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void RateLimitedResult_ExposesStatusAndRetryDelay()
        {
            var result = CohostSendResult.RateLimited(TimeSpan.FromSeconds(2));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo(CohostSendStatus.RateLimited));
            Assert.That(result.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        private sealed class ManualMonotonicClock : IMonotonicClock
        {
            private long elapsedTicks;

            public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref elapsedTicks));

            public void Advance(TimeSpan duration)
            {
                Interlocked.Add(ref elapsedTicks, duration.Ticks);
            }

            public void Set(TimeSpan elapsed)
            {
                Interlocked.Exchange(ref elapsedTicks, elapsed.Ticks);
            }
        }
    }
}
