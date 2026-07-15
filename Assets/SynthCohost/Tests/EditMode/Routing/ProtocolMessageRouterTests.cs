using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Routing;

namespace SynthCohost.Tests.EditMode.Routing
{
    public sealed class ProtocolMessageRouterTests
    {
        private sealed class ImmediateDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => true;
            public void Post(Action action) => action();
            public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) => action();
        }

        private sealed class CountingHandler : IProtocolMessageHandler
        {
            public string EventType => ProtocolEventTypes.AiResponse;
            public int Count { get; private set; }

            public Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
            {
                Count++;
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingHandler : IProtocolMessageHandler
        {
            public string EventType => ProtocolEventTypes.AiResponse;
            public Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("presentation failed");
        }

        [Test]
        public async Task KnownCurrentSessionMessageIsHandled()
        {
            var handler = new CountingHandler();
            var router = Create(handler);

            var result = await router.RouteAsync(AiResponseJson("session-a"), "session-a", CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProtocolRouteStatus.Handled));
            Assert.That(handler.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task UnknownEventIsIgnoredForForwardCompatibility()
        {
            var router = Create();
            const string json = "{\"v\":2,\"type\":\"future.event\",\"session_id\":\"session-a\",\"ts\":\"2026-07-14T00:00:00Z\",\"payload\":{}}";

            var result = await router.RouteAsync(json, "session-a", CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProtocolRouteStatus.IgnoredUnknown));
        }

        [Test]
        public async Task StaleSessionNeverReachesHandler()
        {
            var handler = new CountingHandler();
            var router = Create(handler);

            var result = await router.RouteAsync(AiResponseJson("old-session"), "current-session", CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProtocolRouteStatus.SessionMismatch));
            Assert.That(handler.Count, Is.Zero);
        }

        [Test]
        public async Task HandlerFailureIsIsolatedFromReceiveLoop()
        {
            var router = Create(new ThrowingHandler());

            var result = await router.RouteAsync(AiResponseJson("session-a"), "session-a", CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProtocolRouteStatus.HandlerFailed));
        }

        private static ProtocolMessageRouter Create(params IProtocolMessageHandler[] handlers)
        {
            return new ProtocolMessageRouter(
                new ProtocolCodec(),
                new ImmediateDispatcher(),
                handlers,
                new NullCohostDiagnostics());
        }

        private static string AiResponseJson(string sessionId)
        {
            return "{\"v\":2,\"type\":\"ai.response\",\"session_id\":\"" + sessionId +
                   "\",\"ts\":\"2026-07-14T00:00:00Z\",\"payload\":{\"text\":\"Hello\",\"emotion\":\"happy\",\"intent\":\"chat\"}}";
        }
    }
}
