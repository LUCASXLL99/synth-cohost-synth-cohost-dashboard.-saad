using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Session
{
    public sealed class HeartbeatScheduler : IDisposable
    {
        private readonly TimeSpan interval;
        private readonly Func<CancellationToken, Task> sendHeartbeat;
        private readonly IAsyncDelay delay;
        private readonly object gate = new object();

        private CancellationTokenSource cancellation;
        private Task loopTask = Task.CompletedTask;

        public event Action<Exception> Faulted;

        public HeartbeatScheduler(
            TimeSpan interval,
            Func<CancellationToken, Task> sendHeartbeat,
            IAsyncDelay delay = null)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            this.interval = interval;
            this.sendHeartbeat = sendHeartbeat ?? throw new ArgumentNullException(nameof(sendHeartbeat));
            this.delay = delay ?? new TaskAsyncDelay();
        }

        public bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return cancellation != null;
                }
            }
        }

        public void Start(CancellationToken sessionCancellation)
        {
            lock (gate)
            {
                if (cancellation != null)
                {
                    throw new InvalidOperationException("Heartbeat scheduler is already running.");
                }

                cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
                loopTask = ObserveLoopAsync(cancellation);
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource source;
            Task task;
            lock (gate)
            {
                source = cancellation;
                task = loopTask;
                cancellation = null;
                loopTask = Task.CompletedTask;
            }

            if (source == null)
            {
                return;
            }

            source.Cancel();
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                source.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                await delay.DelayAsync(interval, cancellationToken);
                await sendHeartbeat(cancellationToken);
            }
        }

        private async Task ObserveLoopAsync(CancellationTokenSource source)
        {
            var cancellationToken = source.Token;
            try
            {
                await RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Faulted?.Invoke(exception);
            }
            finally
            {
                var disposeSource = false;
                lock (gate)
                {
                    if (ReferenceEquals(cancellation, source))
                    {
                        cancellation = null;
                        loopTask = Task.CompletedTask;
                        disposeSource = true;
                    }
                }

                if (disposeSource)
                {
                    source.Dispose();
                }
            }
        }

        public void Dispose()
        {
            CancellationTokenSource source;
            lock (gate)
            {
                source = cancellation;
                cancellation = null;
                loopTask = Task.CompletedTask;
            }

            source?.Cancel();
            source?.Dispose();
        }
    }
}
