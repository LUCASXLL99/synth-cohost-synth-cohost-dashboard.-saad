using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Routing;

namespace SynthCohost.Runtime.Features.Speech
{
    public sealed class SpeechFailedMessageHandler : IProtocolMessageHandler
    {
        private readonly IProtocolDialect dialect;
        private readonly ISpeechAudioSink sink;

        public SpeechFailedMessageHandler(IProtocolDialect dialect, ISpeechAudioSink sink)
        {
            this.dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public string EventType => ProtocolEventTypes.SpeechFailed;

        public async Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!dialect.TryReadSpeechFailed(envelope, out var payload, out var error))
            {
                throw new ProtocolException(error);
            }

            await sink.HandleFailedAsync(payload ?? new SpeechFailedPayload(), cancellationToken);
        }
    }
}
