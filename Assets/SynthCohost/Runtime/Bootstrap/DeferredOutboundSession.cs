using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Runtime.Bootstrap
{
    internal sealed class DeferredOutboundSession : ICohostOutboundSession
    {
        private ICohostOutboundSession target;

        public SessionState State => target?.State ?? SessionState.Disconnected;

        public void Bind(ICohostOutboundSession session)
        {
            target = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Task<CohostSendResult> SendPartialTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            return Target().SendPartialTranscriptAsync(text, cancellationToken);
        }

        public Task<CohostSendResult> SendFinalTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            return Target().SendFinalTranscriptAsync(text, cancellationToken);
        }

        public Task<CohostSendResult> SendStateAcknowledgementAsync(
            AvatarBehavior behavior,
            CancellationToken cancellationToken = default)
        {
            return Target().SendStateAcknowledgementAsync(behavior, cancellationToken);
        }

        private ICohostOutboundSession Target()
        {
            return target ?? throw new InvalidOperationException("The outbound session has not been composed yet.");
        }
    }
}
