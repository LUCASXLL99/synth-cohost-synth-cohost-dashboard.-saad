using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public interface IAiResponseSink
    {
        Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken);
    }
}
