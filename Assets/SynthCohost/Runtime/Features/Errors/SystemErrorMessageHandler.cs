using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Routing;

namespace SynthCohost.Runtime.Features.Errors
{
    public sealed class SystemErrorMessageHandler : IProtocolMessageHandler
    {
        private readonly IProtocolDialect dialect;
        private readonly ISystemErrorSink sink;

        public SystemErrorMessageHandler(IProtocolDialect dialect, ISystemErrorSink sink)
        {
            this.dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public string EventType => ProtocolEventTypes.SystemError;

        public async Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!dialect.TryReadSystemError(envelope, out var payload, out var error))
            {
                throw new ProtocolException(error);
            }

            await sink.PresentAsync(payload, cancellationToken);
        }
    }
}
