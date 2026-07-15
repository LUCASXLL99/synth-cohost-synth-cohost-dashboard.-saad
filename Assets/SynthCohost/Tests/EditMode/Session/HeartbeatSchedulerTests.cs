using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class HeartbeatSchedulerTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

        [Test]
        public async Task Start_WaitsForFirstIntervalBeforeSending()
        {
            var delay = new ControllableDelay();
            var sends = 0;
            using (var scheduler = new HeartbeatScheduler(
                       Interval,
                       _ =>
                       {
                           Interlocked.Increment(ref sends);
                           return Task.CompletedTask;
                       },
                       delay))
            {
                scheduler.Start(CancellationToken.None);

                Assert.That(delay.RequestCount, Is.EqualTo(1));
                Assert.That(delay.LastRequestedDelay, Is.EqualTo(Interval));
                Assert.That(Volatile.Read(ref sends), Is.Zero);

                Assert.That(delay.CompleteNext(), Is.True);
                await AwaitWithTimeoutAsync(
                    delay.WaitForRequestCountAsync(2),
                    "The scheduler did not request its second interval.");

                Assert.That(Volatile.Read(ref sends), Is.EqualTo(1));
                await scheduler.StopAsync();
            }
        }

        [Test]
        public async Task CompletedIntervals_SendExactlyOneHeartbeatEach()
        {
            var delay = new ControllableDelay();
            var sends = 0;
            using (var scheduler = new HeartbeatScheduler(
                       Interval,
                       _ =>
                       {
                           Interlocked.Increment(ref sends);
                           return Task.CompletedTask;
                       },
                       delay))
            {
                scheduler.Start(CancellationToken.None);

                Assert.That(delay.CompleteNext(), Is.True);
                await AwaitWithTimeoutAsync(
                    delay.WaitForRequestCountAsync(2),
                    "The first heartbeat did not complete.");
                Assert.That(Volatile.Read(ref sends), Is.EqualTo(1));

                Assert.That(delay.CompleteNext(), Is.True);
                await AwaitWithTimeoutAsync(
                    delay.WaitForRequestCountAsync(3),
                    "The second heartbeat did not complete.");
                Assert.That(Volatile.Read(ref sends), Is.EqualTo(2));

                await scheduler.StopAsync();
            }
        }

        [Test]
        public async Task StopAsync_CancelsPendingDelayAndPreventsLaterSend()
        {
            var delay = new ControllableDelay();
            var sends = 0;
            using (var scheduler = new HeartbeatScheduler(
                       Interval,
                       _ =>
                       {
                           Interlocked.Increment(ref sends);
                           return Task.CompletedTask;
                       },
                       delay))
            {
                scheduler.Start(CancellationToken.None);

                await scheduler.StopAsync();

                Assert.That(delay.CanceledRequestCount, Is.EqualTo(1));
                await AwaitConditionAsync(
                    () => !scheduler.IsRunning,
                    "The scheduler did not publish its stopped state after session cancellation.");
                Assert.That(delay.CompleteNext(), Is.False);
                Assert.That(Volatile.Read(ref sends), Is.Zero);
                Assert.That(scheduler.IsRunning, Is.False);
            }
        }

        [Test]
        public async Task ExternalSessionCancellation_StopsPendingHeartbeatLoop()
        {
            var delay = new ControllableDelay();
            var sends = 0;
            using (var sessionCancellation = new CancellationTokenSource())
            using (var scheduler = new HeartbeatScheduler(
                       Interval,
                       _ =>
                       {
                           Interlocked.Increment(ref sends);
                           return Task.CompletedTask;
                       },
                       delay))
            {
                scheduler.Start(sessionCancellation.Token);
                sessionCancellation.Cancel();

                await AwaitWithTimeoutAsync(
                    delay.WaitForCanceledRequestCountAsync(1),
                    "Session cancellation did not cancel the pending interval.");

                await AwaitConditionAsync(
                    () => !scheduler.IsRunning,
                    "The scheduler did not publish its stopped state after session cancellation.");
                Assert.That(delay.CompleteNext(), Is.False);
                Assert.That(Volatile.Read(ref sends), Is.Zero);
                Assert.That(scheduler.IsRunning, Is.False);

                await scheduler.StopAsync();
            }
        }

        [Test]
        public async Task SendFailure_RaisesFaultedOnceAndIsObservedByStopAsync()
        {
            var delay = new ControllableDelay();
            var expected = new InvalidOperationException("heartbeat send failed");
            var observedFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var faultCount = 0;
            using (var scheduler = new HeartbeatScheduler(
                       Interval,
                       _ => Task.FromException(expected),
                       delay))
            {
                scheduler.Faulted += exception =>
                {
                    Interlocked.Increment(ref faultCount);
                    observedFault.TrySetResult(exception);
                };
                scheduler.Start(CancellationToken.None);

                Assert.That(delay.CompleteNext(), Is.True);
                var exception = await AwaitWithTimeoutAsync(
                    observedFault.Task,
                    "The scheduler did not publish the send failure.");

                Assert.That(exception, Is.SameAs(expected));
                Assert.That(Volatile.Read(ref faultCount), Is.EqualTo(1));
                Assert.That(delay.RequestCount, Is.EqualTo(1));
                Assert.That(scheduler.IsRunning, Is.False);

                await scheduler.StopAsync();
                Assert.That(Volatile.Read(ref faultCount), Is.EqualTo(1));
            }
        }

        private static async Task AwaitWithTimeoutAsync(Task task, string failureMessage)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(task, timeout);
            Assert.That(completed, Is.SameAs(task), failureMessage);
            await task;
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(Task<T> task, string failureMessage)
        {
            await AwaitWithTimeoutAsync((Task)task, failureMessage);
            return await task;
        }

        private static async Task AwaitConditionAsync(Func<bool> condition, string failureMessage)
        {
            var timeoutAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!condition() && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(1);
            }

            Assert.That(condition(), Is.True, failureMessage);
        }

        private sealed class ControllableDelay : IAsyncDelay
        {
            private readonly object gate = new object();
            private readonly Queue<DelayRequest> pending = new Queue<DelayRequest>();
            private readonly List<CountWaiter> requestWaiters = new List<CountWaiter>();
            private readonly List<CountWaiter> cancellationWaiters = new List<CountWaiter>();
            private int requestCount;
            private int canceledRequestCount;

            public int RequestCount
            {
                get
                {
                    lock (gate)
                    {
                        return requestCount;
                    }
                }
            }

            public int CanceledRequestCount
            {
                get
                {
                    lock (gate)
                    {
                        return canceledRequestCount;
                    }
                }
            }

            public TimeSpan LastRequestedDelay { get; private set; }

            public Task DelayAsync(TimeSpan requestedDelay, CancellationToken cancellationToken)
            {
                var request = new DelayRequest();
                lock (gate)
                {
                    LastRequestedDelay = requestedDelay;
                    requestCount++;
                    pending.Enqueue(request);
                    CompleteSatisfiedWaiters(requestWaiters, requestCount);
                }

                request.Cancellation = cancellationToken.Register(() => Cancel(request));
                return AwaitRequestAsync(request);
            }

            public bool CompleteNext()
            {
                DelayRequest request;
                lock (gate)
                {
                    do
                    {
                        if (pending.Count == 0)
                        {
                            return false;
                        }

                        request = pending.Dequeue();
                    }
                    while (request.Completion.Task.IsCompleted);
                }

                return request.Completion.TrySetResult(true);
            }

            public Task WaitForRequestCountAsync(int expectedCount)
            {
                lock (gate)
                {
                    return CreateCountWaiter(requestWaiters, expectedCount, requestCount);
                }
            }

            public Task WaitForCanceledRequestCountAsync(int expectedCount)
            {
                lock (gate)
                {
                    return CreateCountWaiter(cancellationWaiters, expectedCount, canceledRequestCount);
                }
            }

            private async Task AwaitRequestAsync(DelayRequest request)
            {
                try
                {
                    await request.Completion.Task;
                }
                finally
                {
                    request.Cancellation.Dispose();
                }
            }

            private void Cancel(DelayRequest request)
            {
                if (!request.Completion.TrySetCanceled())
                {
                    return;
                }

                lock (gate)
                {
                    canceledRequestCount++;
                    CompleteSatisfiedWaiters(cancellationWaiters, canceledRequestCount);
                }
            }

            private static Task CreateCountWaiter(
                ICollection<CountWaiter> waiters,
                int expectedCount,
                int currentCount)
            {
                if (currentCount >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                var waiter = new CountWaiter(expectedCount);
                waiters.Add(waiter);
                return waiter.Completion.Task;
            }

            private static void CompleteSatisfiedWaiters(List<CountWaiter> waiters, int currentCount)
            {
                for (var index = waiters.Count - 1; index >= 0; index--)
                {
                    if (currentCount < waiters[index].ExpectedCount)
                    {
                        continue;
                    }

                    waiters[index].Completion.TrySetResult(true);
                    waiters.RemoveAt(index);
                }
            }

            private sealed class DelayRequest
            {
                public readonly TaskCompletionSource<bool> Completion =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                public CancellationTokenRegistration Cancellation;
            }

            private sealed class CountWaiter
            {
                public CountWaiter(int expectedCount)
                {
                    ExpectedCount = expectedCount;
                }

                public int ExpectedCount { get; }
                public TaskCompletionSource<bool> Completion { get; } =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
