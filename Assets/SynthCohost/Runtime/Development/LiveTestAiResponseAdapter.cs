using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.AiResponse;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Development/Live Test AI Response Adapter")]
    public sealed class LiveTestAiResponseAdapter : AiResponseSinkBehaviour
    {
        public event Action<AiResponsePayload> ResponseReceived;

        public AiResponsePayload LastResponse { get; private set; }

        public override Task PresentAsync(
            AiResponsePayload response,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastResponse = response ?? throw new ArgumentNullException(nameof(response));
            ResponseReceived?.Invoke(response);
            return Task.CompletedTask;
        }
    }
}
