using NUnit.Framework;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class SessionStateMachineTests
    {
        [Test]
        public void SilentV2Handshake_ReachesReadyThroughExpectedStates()
        {
            var machine = new SessionStateMachine();

            machine.Transition(SessionState.Connecting);
            machine.Transition(SessionState.Authenticating);
            machine.Transition(SessionState.Ready);

            Assert.That(machine.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void Ready_CannotSkipDirectlyBackToConnecting()
        {
            var machine = new SessionStateMachine();
            machine.Transition(SessionState.Connecting);
            machine.Transition(SessionState.Authenticating);
            machine.Transition(SessionState.Ready);

            Assert.That(machine.TryTransition(SessionState.Connecting), Is.False);
            Assert.That(machine.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void Reconnect_RequiresFreshConnectingTransition()
        {
            var machine = new SessionStateMachine();
            machine.Transition(SessionState.Connecting);
            machine.Transition(SessionState.Reconnecting);
            machine.Transition(SessionState.Connecting);

            Assert.That(machine.State, Is.EqualTo(SessionState.Connecting));
        }
    }
}
