using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Errors
{
    public sealed class NullSystemErrorSink : ISystemErrorSink
    {
        public Task PresentAsync(SystemErrorPayload error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
