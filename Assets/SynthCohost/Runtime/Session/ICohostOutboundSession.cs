using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Session
{
    public interface ICohostOutboundSession
    {
        SessionState State { get; }
        Task<CohostSendResult> SendPartialTranscriptAsync(string text, CancellationToken cancellationToken = default);
        Task<CohostSendResult> SendFinalTranscriptAsync(string text, CancellationToken cancellationToken = default);
        Task<CohostSendResult> SendStateAcknowledgementAsync(
            AvatarBehavior behavior,
            CancellationToken cancellationToken = default);
    }
}
