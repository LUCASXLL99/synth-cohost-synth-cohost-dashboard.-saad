using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public sealed class NullAiResponseSink : IAiResponseSink
    {
        public Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
