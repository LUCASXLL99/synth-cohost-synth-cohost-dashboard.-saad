using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public sealed class CompositeAiResponseSink : IAiResponseSink
    {
        private readonly IAiResponseSink[] sinks;

        public CompositeAiResponseSink(params IAiResponseSink[] sinks)
        {
            this.sinks = sinks ?? Array.Empty<IAiResponseSink>();
        }

        public async Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken)
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
                    await sink.PresentAsync(response, cancellationToken);
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
