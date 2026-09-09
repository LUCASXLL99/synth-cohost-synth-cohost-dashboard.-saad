using NUnit.Framework;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Diagnostics
{
    public sealed class ConnectionStatusViewModelTests
    {
        [Test]
        public void Connecting_IncrementsConnectCount()
        {
            var status = new ConnectionStatusViewModel();
            status.SetState(SessionState.Connecting);
            status.SetState(SessionState.Ready);
            status.SetState(SessionState.Connecting);

            Assert.That(status.ConnectCount, Is.EqualTo(2));
        }

        [Test]
        public void HeartbeatOutbound_IncrementsHeartbeatSendCount()
        {
            var status = new ConnectionStatusViewModel();
            status.SetLastOutboundEvent("heartbeat");
            status.SetLastOutboundEvent("stt.final");

            Assert.That(status.HeartbeatSendCount, Is.EqualTo(1));
            Assert.That(status.LastOutboundEventType, Is.EqualTo("stt.final"));
        }

        [Test]
        public void InboundAndSanitizedError_IncrementCounters()
        {
            var status = new ConnectionStatusViewModel();
            status.SetLastEvent("avatar.state");
            status.SetError("Backend reported AI_GENERATION_FAILED.");
            status.SetError(string.Empty);

            Assert.That(status.InboundCount, Is.EqualTo(1));
            Assert.That(status.SanitizedErrorCount, Is.EqualTo(1));
        }
    }
}
