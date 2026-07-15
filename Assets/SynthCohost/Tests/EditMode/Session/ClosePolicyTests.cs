using System;
using NUnit.Framework;
using SynthCohost.Runtime.Session;
using SynthCohost.Transport;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class ClosePolicyTests
    {
        [TestCase(4000, CloseDirective.Fault)]
        [TestCase(4001, CloseDirective.RequireAuthentication)]
        [TestCase(4002, CloseDirective.Reconnect)]
        [TestCase(4003, CloseDirective.Reconnect)]
        [TestCase(4004, CloseDirective.Reconnect)]
        public void BackendCloseCodeHasBoundedDirective(int code, CloseDirective expected)
        {
            var close = new TransportCloseInfo(code, string.Empty, true, true);

            var decision = ClosePolicy.Decide(close, false);

            Assert.That(decision.Directive, Is.EqualTo(expected));
        }

        [Test]
        public void RateLimitWaitsAtLeastServerWindow()
        {
            var close = new TransportCloseInfo(4004, string.Empty, true, true);

            Assert.That(ClosePolicy.Decide(close, false).MinimumDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void UserStopNeverReconnects()
        {
            var close = new TransportCloseInfo(null, string.Empty, false, false);

            Assert.That(ClosePolicy.Decide(close, true).Directive, Is.EqualTo(CloseDirective.StayDisconnected));
        }
    }
}
