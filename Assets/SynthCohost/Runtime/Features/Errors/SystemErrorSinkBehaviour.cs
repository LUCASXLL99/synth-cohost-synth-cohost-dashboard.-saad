using UnityEngine;

namespace SynthCohost.Runtime.Features.Errors
{
    public abstract class SystemErrorSinkBehaviour : MonoBehaviour, ISystemErrorSink
    {
        public abstract System.Threading.Tasks.Task PresentAsync(
            Protocol.SystemErrorPayload error,
            System.Threading.CancellationToken cancellationToken);
    }
}
