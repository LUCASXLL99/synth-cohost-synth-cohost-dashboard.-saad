using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using UnityEngine.Events;

namespace SynthCohost.Runtime.Features.Errors
{
    public sealed class UnityEventSystemErrorAdapter : SystemErrorSinkBehaviour
    {
        [Serializable]
        public sealed class SystemErrorEvent : UnityEvent<string, string>
        {
        }

        [UnityEngine.SerializeField] private SystemErrorEvent errorReceived = new SystemErrorEvent();

        public override Task PresentAsync(SystemErrorPayload error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            errorReceived.Invoke(error.Code, error.Message);
            return Task.CompletedTask;
        }
    }
}
