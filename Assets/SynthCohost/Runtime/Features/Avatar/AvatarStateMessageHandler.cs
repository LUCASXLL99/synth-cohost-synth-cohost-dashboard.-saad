using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Routing;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Runtime.Features.Avatar
{
    public sealed class AvatarStateMessageHandler : IProtocolMessageHandler
    {
        private readonly IProtocolDialect dialect;
        private readonly IAvatarBehaviorController controller;
        private readonly ICohostOutboundSession outbound;

        public AvatarStateMessageHandler(
            IProtocolDialect dialect,
            IAvatarBehaviorController controller,
            ICohostOutboundSession outbound)
        {
            this.dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        }

        public string EventType => ProtocolEventTypes.AvatarState;

        public async Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!dialect.TryReadAvatarState(envelope, out var payload, out var error))
            {
                throw new ProtocolException(error);
            }

            if (await controller.ApplyAsync(payload.Behavior, cancellationToken))
            {
                await outbound.SendStateAcknowledgementAsync(payload.Behavior, cancellationToken);
            }
        }
    }
}
