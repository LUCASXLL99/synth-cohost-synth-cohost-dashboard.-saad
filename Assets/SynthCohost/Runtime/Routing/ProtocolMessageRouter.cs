using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;

namespace SynthCohost.Runtime.Routing
{
    public sealed class ProtocolMessageRouter
    {
        private readonly IProtocolCodec codec;
        private readonly IMainThreadDispatcher dispatcher;
        private readonly ICohostDiagnostics diagnostics;
        private readonly Dictionary<string, IProtocolMessageHandler> handlers;

        public ProtocolMessageRouter(
            IProtocolCodec codec,
            IMainThreadDispatcher dispatcher,
            IEnumerable<IProtocolMessageHandler> handlers,
            ICohostDiagnostics diagnostics = null)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.diagnostics = diagnostics ?? new NullCohostDiagnostics();
            this.handlers = new Dictionary<string, IProtocolMessageHandler>(StringComparer.Ordinal);

            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers)
            {
                if (handler == null || string.IsNullOrEmpty(handler.EventType))
                {
                    throw new ArgumentException("Every protocol handler must provide an event type.", nameof(handlers));
                }

                if (!this.handlers.TryAdd(handler.EventType, handler))
                {
                    throw new ArgumentException($"A handler for '{handler.EventType}' is already registered.", nameof(handlers));
                }
            }
        }

        public async Task<ProtocolRouteResult> RouteAsync(
            string rawJson,
            string activeSessionId,
            CancellationToken cancellationToken)
        {
            if (!codec.TryParseEnvelope(rawJson, out var envelope, out var error))
            {
                diagnostics.Write(DiagnosticLogLevel.Warning, "Protocol", $"Dropped malformed frame ({error.Code}).");
                return new ProtocolRouteResult(ProtocolRouteStatus.Malformed, string.Empty, error);
            }

            if (!string.Equals(envelope.SessionId, activeSessionId, StringComparison.Ordinal))
            {
                diagnostics.Write(DiagnosticLogLevel.Warning, "Protocol", "Dropped a frame for a stale or mismatched session.");
                return new ProtocolRouteResult(
                    ProtocolRouteStatus.SessionMismatch,
                    envelope.EventType,
                    envelope: envelope);
            }

            if (!handlers.TryGetValue(envelope.EventType, out var handler))
            {
                if (ProtocolEventTypes.IsReservedUnimplementedSpeechEvent(envelope.EventType))
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Information,
                        "Protocol",
                        "Ignored reserved speech.* frame; speech.audio / speech.failed are handled separately.");
                }
                else
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Verbose,
                        "Protocol",
                        "Ignored an unhandled event type; the untrusted identifier was not logged.");
                }

                return new ProtocolRouteResult(
                    ProtocolRouteStatus.IgnoredUnknown,
                    envelope.EventType,
                    envelope: envelope);
            }

            try
            {
                await dispatcher.InvokeAsync(
                    () => handler.HandleAsync(envelope, cancellationToken),
                    cancellationToken);
                return new ProtocolRouteResult(
                    ProtocolRouteStatus.Handled,
                    envelope.EventType,
                    envelope: envelope);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Write(
                    DiagnosticLogLevel.Error,
                    "Routing",
                    $"Handler for '{envelope.EventType}' failed; receive processing will continue.",
                    exception);
                return new ProtocolRouteResult(
                    ProtocolRouteStatus.HandlerFailed,
                    envelope.EventType,
                    envelope: envelope);
            }
        }
    }
}
