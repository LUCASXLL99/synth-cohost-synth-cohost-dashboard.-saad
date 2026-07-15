using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Authentication;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Routing;
using SynthCohost.Runtime.Session;
using SynthCohost.Transport;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class CohostSessionControllerTests
    {
        private static readonly Guid AvatarId = Guid.Parse("b81f7c04-a4fb-4b99-98b9-93c85a2157e9");

        [Test]
        public async Task Connect_SendsAuthFirst_AndBecomesReadyWithoutSessionReady()
        {
            var harness = SessionHarness.Create();
            try
            {
                await harness.Controller.ConnectAsync();

                var socket = harness.Factory.GetTransport(0);
                Assert.That(harness.Controller.State, Is.EqualTo(SessionState.Ready));
                Assert.That(socket.SentMessages.Count, Is.EqualTo(1));
                AssertAuthFrame(harness.Codec, socket.SentMessages[0], out var envelope);
                Assert.That(envelope.EventType, Is.EqualTo(ProtocolEventTypes.Auth));
            }
            finally
            {
                await harness.StopAsync();
            }
        }

        [Test]
        public async Task Connect_PassesRenderSafeSeventyFiveSecondTimeout()
        {
            var harness = SessionHarness.Create();
            try
            {
                await harness.Controller.ConnectAsync();

                var socket = harness.Factory.GetTransport(0);
                Assert.That(socket.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(75)));
                Assert.That(socket.Endpoint, Is.EqualTo(SessionHarness.Endpoint));
            }
            finally
            {
                await harness.StopAsync();
            }
        }

        [Test]
        public async Task EveryConnection_AuthenticatesWithCredentialsAndFreshSessionId()
        {
            var harness = SessionHarness.Create();
            try
            {
                await harness.Controller.ConnectAsync();
                var firstSocket = harness.Factory.GetTransport(0);
                AssertAuthFrame(harness.Codec, firstSocket.SentMessages[0], out var firstEnvelope);

                firstSocket.EmitClosed(4003, "heartbeat timeout");
                await WaitUntilAsync(
                    () => harness.Factory.Count >= 2 && harness.Controller.State == SessionState.Ready,
                    "The controller did not finish its automatic reconnect.");

                var secondSocket = harness.Factory.GetTransport(1);
                AssertAuthFrame(harness.Codec, secondSocket.SentMessages[0], out var secondEnvelope);

                await harness.Controller.DisconnectAsync();
                await harness.Controller.ConnectAsync();
                var thirdSocket = harness.Factory.GetTransport(2);
                AssertAuthFrame(harness.Codec, thirdSocket.SentMessages[0], out var thirdEnvelope);

                Assert.That(secondEnvelope.SessionId, Is.Not.EqualTo(firstEnvelope.SessionId));
                Assert.That(thirdEnvelope.SessionId, Is.Not.EqualTo(firstEnvelope.SessionId));
                Assert.That(thirdEnvelope.SessionId, Is.Not.EqualTo(secondEnvelope.SessionId));
            }
            finally
            {
                await harness.StopAsync();
            }
        }

        [Test]
        public async Task SendFinal_RejectsASecondTurnUntilTheFirstCompletes()
        {
            var harness = SessionHarness.Create();
            try
            {
                await harness.Controller.ConnectAsync();

                var first = await harness.Controller.SendFinalTranscriptAsync("first turn");
                var second = await harness.Controller.SendFinalTranscriptAsync("second turn");

                Assert.That(first.Status, Is.EqualTo(CohostSendStatus.Sent));
                Assert.That(second.Status, Is.EqualTo(CohostSendStatus.TurnAlreadyInFlight));
                Assert.That(harness.Factory.GetTransport(0).SentMessages.Count, Is.EqualTo(2));
            }
            finally
            {
                await harness.StopAsync();
            }
        }

        [Test]
        public async Task InboundFrames_AreHandledSequentiallyInEmissionOrder()
        {
            var handler = new BlockingOrderedHandler();
            var harness = SessionHarness.Create(handler);
            try
            {
                await harness.Controller.ConnectAsync();
                var socket = harness.Factory.GetTransport(0);
                AssertAuthFrame(harness.Codec, socket.SentMessages[0], out var authEnvelope);

                var frame = harness.Codec.SerializeEnvelope(
                    new V2Envelope<AvatarStatePayload>(
                        ProtocolConstants.DeployedVersion,
                        ProtocolEventTypes.AvatarState,
                        authEnvelope.SessionId,
                        "2026-07-14T00:00:00Z",
                        new AvatarStatePayload { Behavior = AvatarBehavior.Listening }));

                socket.EmitMessage(frame);
                socket.EmitMessage(frame);

                await AwaitWithTimeoutAsync(handler.FirstStarted, "The first inbound handler did not start.");
                await Task.Yield();
                Assert.That(handler.SecondStarted.IsCompleted, Is.False,
                    "The second callback overtook the blocked first callback.");

                handler.ReleaseFirst();
                await AwaitWithTimeoutAsync(handler.SecondStarted, "The second inbound handler did not run.");
                CollectionAssert.AreEqual(new[] { 1, 2 }, handler.InvocationOrder);
            }
            finally
            {
                handler.ReleaseFirst();
                await harness.StopAsync();
            }
        }

        [Test]
        public async Task OutboundRateLimit_CountsAuthAndNeverSendsRejectedFrame()
        {
            var harness = SessionHarness.Create();
            try
            {
                await harness.Controller.ConnectAsync();
                var socket = harness.Factory.GetTransport(0);

                for (var index = 0; index < OutboundMessageRateLimiter.CurrentV2MaximumMessages - 1; index++)
                {
                    var accepted = await harness.Controller.SendPartialTranscriptAsync($"partial {index}");
                    Assert.That(accepted.Status, Is.EqualTo(CohostSendStatus.Sent), $"Send {index} was unexpectedly rejected.");
                }

                Assert.That(socket.SentMessages.Count, Is.EqualTo(OutboundMessageRateLimiter.CurrentV2MaximumMessages),
                    "Auth must consume one slot in the connection-wide limit.");

                var rejected = await harness.Controller.SendPartialTranscriptAsync("over the limit");
                Assert.That(rejected.Status, Is.EqualTo(CohostSendStatus.RateLimited));
                Assert.That(rejected.RetryAfter, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(socket.SentMessages.Count, Is.EqualTo(OutboundMessageRateLimiter.CurrentV2MaximumMessages));

                await Task.Yield();
                Assert.That(socket.SentMessages.Count, Is.EqualTo(OutboundMessageRateLimiter.CurrentV2MaximumMessages),
                    "A rejected frame must not be queued or replayed.");
            }
            finally
            {
                await harness.StopAsync();
            }
        }

        private static void AssertAuthFrame(
            ProtocolCodec codec,
            string rawJson,
            out IncomingEnvelope envelope)
        {
            Assert.That(codec.TryParseEnvelope(rawJson, out envelope, out var envelopeError), Is.True, envelopeError.Message);
            Assert.That(envelope.Version, Is.EqualTo(ProtocolConstants.DeployedVersion));
            Assert.That(envelope.EventType, Is.EqualTo(ProtocolEventTypes.Auth));
            Assert.That(envelope.SessionId, Is.Not.Empty);
            Assert.That(codec.TryParsePayload<AuthPayload>(envelope, out var payload, out var payloadError), Is.True, payloadError.Message);
            Assert.That(payload.Token, Is.EqualTo(SessionHarness.AccessToken));
            Assert.That(payload.AvatarId, Is.EqualTo(AvatarId.ToString("D")));
        }

        private static async Task AwaitWithTimeoutAsync(Task task, string failureMessage)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(task, timeout);
            Assert.That(completed, Is.SameAs(task), failureMessage);
            await task;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
        {
            var expiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!condition() && DateTime.UtcNow < expiresAt)
            {
                await Task.Delay(5);
            }

            Assert.That(condition(), Is.True, failureMessage);
        }

        private sealed class SessionHarness : IDisposable
        {
            public static readonly Uri Endpoint = new Uri("wss://synth-cohost-app.onrender.com/ws");
            public const string AccessToken = "test-access-token";

            private SessionHarness(
                ProtocolCodec codec,
                FakeTransportFactory factory,
                CohostSessionController controller)
            {
                Codec = codec;
                Factory = factory;
                Controller = controller;
            }

            public ProtocolCodec Codec { get; }
            public FakeTransportFactory Factory { get; }
            public CohostSessionController Controller { get; }

            public static SessionHarness Create(params IProtocolMessageHandler[] handlers)
            {
                var codec = new ProtocolCodec();
                var dialect = new DeployedV2ProtocolDialect(codec);
                var dispatcher = new ImmediateDispatcher();
                var factory = new FakeTransportFactory();
                var options = new ConnectionRuntimeOptions(
                    Endpoint,
                    TimeSpan.FromSeconds(75),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromHours(1),
                    new ReconnectPolicySnapshot(TimeSpan.Zero, TimeSpan.Zero, 0f, 3));
                var router = new ProtocolMessageRouter(codec, dispatcher, handlers);
                var controller = new CohostSessionController(
                    options,
                    new FixedCredentialProvider(),
                    dialect,
                    factory,
                    router,
                    dispatcher: dispatcher);
                return new SessionHarness(codec, factory, controller);
            }

            public async Task StopAsync()
            {
                if (Controller.State != SessionState.Disconnected)
                {
                    await Controller.DisconnectAsync();
                }

                Dispose();
            }

            public void Dispose()
            {
                Controller.Dispose();
            }
        }

        private sealed class FixedCredentialProvider : ICohostCredentialProvider
        {
            public Task<CohostCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new CohostCredentials(SessionHarness.AccessToken, AvatarId));
            }
        }

        private sealed class ImmediateDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => true;

            public void Post(Action action)
            {
                action();
            }

            public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }
        }

        private sealed class FakeTransportFactory : IWebSocketTransportFactory
        {
            private readonly object gate = new object();
            private readonly List<FakeTransport> transports = new List<FakeTransport>();

            public int Count
            {
                get
                {
                    lock (gate)
                    {
                        return transports.Count;
                    }
                }
            }

            public IWebSocketTransport Create()
            {
                var transport = new FakeTransport();
                lock (gate)
                {
                    transports.Add(transport);
                }

                return transport;
            }

            public FakeTransport GetTransport(int index)
            {
                lock (gate)
                {
                    return transports[index];
                }
            }
        }

        private sealed class FakeTransport : IWebSocketTransport
        {
            private readonly object gate = new object();
            private readonly List<string> sentMessages = new List<string>();

            public TransportState State { get; private set; } = TransportState.Created;
            public Uri Endpoint { get; private set; }
            public TimeSpan ConnectTimeout { get; private set; }

            public IReadOnlyList<string> SentMessages
            {
                get
                {
                    lock (gate)
                    {
                        return sentMessages.ToArray();
                    }
                }
            }

            public event Action Opened;
            public event Action<string> MessageReceived;
            public event Action<TransportErrorInfo> Error
            {
                add { }
                remove { }
            }
            public event Action<TransportCloseInfo> Closed;

            public Task ConnectAsync(Uri endpoint, TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Endpoint = endpoint;
                ConnectTimeout = timeout;
                State = TransportState.Connecting;
                State = TransportState.Open;
                Opened?.Invoke();
                return Task.CompletedTask;
            }

            public Task SendTextAsync(string message, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (State != TransportState.Open)
                {
                    throw new InvalidOperationException("The fake socket is not open.");
                }

                lock (gate)
                {
                    sentMessages.Add(message);
                }

                return Task.CompletedTask;
            }

            public Task CloseAsync(
                int code = WebSocketCloseCode.NormalClosure,
                string reason = "",
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                State = TransportState.Closed;
                return Task.CompletedTask;
            }

            public void EmitMessage(string message)
            {
                MessageReceived?.Invoke(message);
            }

            public void EmitClosed(int code, string reason)
            {
                State = TransportState.Closed;
                Closed?.Invoke(new TransportCloseInfo(code, reason, true, true));
            }

            public void Dispose()
            {
                State = TransportState.Disposed;
            }
        }

        private sealed class BlockingOrderedHandler : IProtocolMessageHandler
        {
            private readonly TaskCompletionSource<bool> firstStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> secondStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> releaseFirst =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<int> invocationOrder = new List<int>();
            private int invocationCount;

            public string EventType => ProtocolEventTypes.AvatarState;
            public Task FirstStarted => firstStarted.Task;
            public Task SecondStarted => secondStarted.Task;
            public IReadOnlyList<int> InvocationOrder => invocationOrder;

            public async Task HandleAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
            {
                var invocation = Interlocked.Increment(ref invocationCount);
                invocationOrder.Add(invocation);
                if (invocation == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                    return;
                }

                secondStarted.TrySetResult(true);
            }

            public void ReleaseFirst()
            {
                releaseFirst.TrySetResult(true);
            }
        }
    }
}
