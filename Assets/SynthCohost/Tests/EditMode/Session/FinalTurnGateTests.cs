using NUnit.Framework;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class FinalTurnGateTests
    {
        [Test]
        public void OnlyOneTurnCanBeActive()
        {
            var gate = new FinalTurnGate();

            Assert.That(gate.TryBegin(out var first), Is.True);
            Assert.That(first, Is.Not.Zero);
            Assert.That(gate.TryBegin(out var blocked), Is.False);
            Assert.That(blocked, Is.Zero);
            Assert.That(gate.IsActive, Is.True);
        }

        [Test]
        public void StaleCompletionCannotReleaseNewTurn()
        {
            var gate = new FinalTurnGate();
            gate.TryBegin(out var first);
            Assert.That(gate.Complete(first), Is.True);
            gate.TryBegin(out var second);

            Assert.That(gate.Complete(first), Is.False);
            Assert.That(gate.IsActive, Is.True);
            Assert.That(gate.Complete(second), Is.True);
        }

        [Test]
        public void ResetDropsInterruptedTurnWithoutReplayState()
        {
            var gate = new FinalTurnGate();
            gate.TryBegin(out _);

            gate.Reset();

            Assert.That(gate.IsActive, Is.False);
            Assert.That(gate.TryBegin(out _), Is.True);
        }
    }
}
