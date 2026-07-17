using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.Errors;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Development/Live Test System Error Adapter")]
    public sealed class LiveTestSystemErrorAdapter : SystemErrorSinkBehaviour
    {
        public event Action<SystemErrorPayload> ErrorReceived;

        public SystemErrorPayload LastError { get; private set; }

        public override Task PresentAsync(
            SystemErrorPayload error,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastError = error ?? throw new ArgumentNullException(nameof(error));
            ErrorReceived?.Invoke(error);
            return Task.CompletedTask;
        }
    }
}
