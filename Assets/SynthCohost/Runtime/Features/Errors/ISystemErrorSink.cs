using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Errors
{
    public interface ISystemErrorSink
    {
        Task PresentAsync(SystemErrorPayload error, CancellationToken cancellationToken);
    }
}
