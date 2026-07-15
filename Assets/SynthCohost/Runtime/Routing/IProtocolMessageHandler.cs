using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Routing
{
    public interface IProtocolMessageHandler
    {
        string EventType { get; }
        Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken);
    }
}
