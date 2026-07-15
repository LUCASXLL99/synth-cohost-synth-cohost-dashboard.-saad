using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using UnityEngine.Events;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public sealed class UnityEventAiResponseAdapter : AiResponseSinkBehaviour
    {
        [Serializable]
        public sealed class AiResponseEvent : UnityEvent<string, string, string>
        {
        }

        [UnityEngine.SerializeField] private AiResponseEvent responseReceived = new AiResponseEvent();

        public override Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            responseReceived.Invoke(
                response.Text,
                response.Emotion.ToWireValue(),
                response.Intent.ToWireValue());
            return Task.CompletedTask;
        }
    }
}
