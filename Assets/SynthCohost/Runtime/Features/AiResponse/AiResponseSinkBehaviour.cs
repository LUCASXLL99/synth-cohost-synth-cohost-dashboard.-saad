using UnityEngine;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public abstract class AiResponseSinkBehaviour : MonoBehaviour, IAiResponseSink
    {
        public abstract System.Threading.Tasks.Task PresentAsync(
            Protocol.AiResponsePayload response,
            System.Threading.CancellationToken cancellationToken);
    }
}
