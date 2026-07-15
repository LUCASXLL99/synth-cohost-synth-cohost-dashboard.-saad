using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Session
{
    public interface IAsyncDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class TaskAsyncDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
