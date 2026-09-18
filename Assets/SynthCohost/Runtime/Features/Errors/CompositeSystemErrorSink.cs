using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Errors
{
    public sealed class CompositeSystemErrorSink : ISystemErrorSink
    {
        private readonly ISystemErrorSink[] sinks;

        public CompositeSystemErrorSink(params ISystemErrorSink[] sinks)
        {
            this.sinks = sinks ?? Array.Empty<ISystemErrorSink>();
        }

        public async Task PresentAsync(SystemErrorPayload error, CancellationToken cancellationToken)
        {
            Exception first = null;
            foreach (var sink in sinks)
            {
                if (sink == null)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await sink.PresentAsync(error, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    first ??= ex;
                }
            }

            if (first != null)
            {
                throw first;
            }
        }
    }
}
