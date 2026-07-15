using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Routing;

namespace SynthCohost.Runtime.Features.AiResponse
{
    public sealed class AiResponseMessageHandler : IProtocolMessageHandler
    {
        private readonly IProtocolDialect dialect;
        private readonly IAiResponseSink sink;

        public AiResponseMessageHandler(IProtocolDialect dialect, IAiResponseSink sink)
        {
            this.dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public string EventType => ProtocolEventTypes.AiResponse;

        public async Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!dialect.TryReadAiResponse(envelope, out var payload, out var error))
            {
                throw new ProtocolException(error);
            }

            await sink.PresentAsync(payload, cancellationToken);
        }
    }
}
