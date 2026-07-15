using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SynthCohost.Transport.Tests
{
    public sealed class ClientWebSocketTransportLifecycleTests
    {
        [Test]
        public async Task CloseBeforeConnect_IsIdempotentAndNotifiesExactlyOnce()
        {
            var transport = new ClientWebSocketTransport();
            var notificationCount = 0;
            TransportCloseInfo closeInfo = null;
            transport.Closed += info =>
            {
                notificationCount++;
                closeInfo = info;
            };

            await transport.CloseAsync(4003, "test close", CancellationToken.None);
            await transport.CloseAsync(4003, "test close", CancellationToken.None);
            transport.Dispose();
            transport.Dispose();

            Assert.That(notificationCount, Is.EqualTo(1));
            Assert.That(closeInfo, Is.Not.Null);
            Assert.That(closeInfo.Code, Is.EqualTo(4003));
            Assert.That(closeInfo.Reason, Is.EqualTo("test close"));
            Assert.That(closeInfo.WasClean, Is.True);
            Assert.That(closeInfo.RemoteInitiated, Is.False);
            Assert.That(transport.State, Is.EqualTo(TransportState.Disposed));
        }

        [Test]
        public void DisposeBeforeConnect_IsIdempotentAndNotifiesExactlyOnce()
        {
            var transport = new ClientWebSocketTransport();
            var notificationCount = 0;
            transport.Closed += _ => notificationCount++;

            transport.Dispose();
            transport.Dispose();

            Assert.That(notificationCount, Is.EqualTo(1));
            Assert.That(transport.State, Is.EqualTo(TransportState.Disposed));
        }

        [Test]
        public void ClosedSubscriberException_DoesNotBlockOtherSubscribers()
        {
            var transport = new ClientWebSocketTransport();
            var secondSubscriberCalled = false;
            transport.Closed += _ => throw new InvalidOperationException("consumer failure");
            transport.Closed += _ => secondSubscriberCalled = true;

            Assert.DoesNotThrow(transport.Dispose);
            Assert.That(secondSubscriberCalled, Is.True);
        }

        [Test]
        public void SendBeforeConnect_ThrowsWithoutChangingState()
        {
            var transport = new ClientWebSocketTransport();
            try
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await transport.SendTextAsync("hello", CancellationToken.None));
                Assert.That(transport.State, Is.EqualTo(TransportState.Created));
            }
            finally
            {
                transport.Dispose();
            }
        }

        [Test]
        public void Connect_RejectsNonWebSocketEndpointBeforeChangingState()
        {
            var transport = new ClientWebSocketTransport();
            try
            {
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await transport.ConnectAsync(
                        new Uri("https://example.com/ws"),
                        TimeSpan.FromSeconds(75),
                        CancellationToken.None));
                Assert.That(transport.State, Is.EqualTo(TransportState.Created));
            }
            finally
            {
                transport.Dispose();
            }
        }

        [Test]
        public async Task Connect_AcceptsSeventyFiveSecondTimeoutAtValidationBoundary()
        {
            var transport = new ClientWebSocketTransport();
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    await transport.ConnectAsync(
                        new Uri("ws://127.0.0.1:8080/ws"),
                        TimeSpan.FromSeconds(75),
                        cancellation.Token);
                    Assert.Fail("A pre-cancelled connection must not start.");
                }
                catch (OperationCanceledException)
                {
                    // TaskCanceledException is an allowed subtype on newer .NET runtimes.
                }
            }

            Assert.That(transport.State, Is.EqualTo(TransportState.Closed));
            transport.Dispose();
        }

        [Test]
        public void Factory_CreatesDistinctOneShotTransports()
        {
            var factory = new ClientWebSocketTransportFactory();
            var first = factory.Create();
            var second = factory.Create();
            try
            {
                Assert.That(first, Is.TypeOf<ClientWebSocketTransport>());
                Assert.That(second, Is.TypeOf<ClientWebSocketTransport>());
                Assert.That(second, Is.Not.SameAs(first));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }
    }
}
