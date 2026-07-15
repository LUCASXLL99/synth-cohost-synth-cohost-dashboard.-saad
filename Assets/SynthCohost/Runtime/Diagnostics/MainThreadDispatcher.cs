using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Runtime.Diagnostics
{
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }
        void Post(Action action);
        Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
    }

    public sealed class MainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly SynchronizationContext context;
        private readonly int threadId;

        public MainThreadDispatcher(SynchronizationContext context = null)
        {
            this.context = context ?? SynchronizationContext.Current ?? new SynchronizationContext();
            threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == threadId;

        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (IsMainThread)
            {
                action();
                return;
            }

            context.Post(_ => action(), null);
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (IsMainThread)
            {
                return action();
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(async _ =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    await action();
                    completion.TrySetResult(true);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null);
            return completion.Task;
        }
    }
}
