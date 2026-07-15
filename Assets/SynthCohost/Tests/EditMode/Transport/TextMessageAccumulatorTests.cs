using System;
using System.Text;
using NUnit.Framework;

namespace SynthCohost.Transport.Tests
{
    public sealed class TextMessageAccumulatorTests
    {
        [Test]
        public void Append_ReassemblesUtf8SplitInsideMultiByteCharacter()
        {
            var bytes = Encoding.UTF8.GetBytes("hello 🌍");
            using (var accumulator = new TextMessageAccumulator(64))
            {
                string message;
                Assert.That(
                    accumulator.Append(bytes, 0, bytes.Length - 2, false, out message),
                    Is.False);
                Assert.That(message, Is.Null);

                Assert.That(
                    accumulator.Append(bytes, bytes.Length - 2, 2, true, out message),
                    Is.True);
                Assert.That(message, Is.EqualTo("hello 🌍"));
                Assert.That(accumulator.BufferedByteCount, Is.Zero);
            }
        }

        [Test]
        public void Append_AllowsMessageExactlyAtReceiveCeiling()
        {
            var bytes = new byte[ClientWebSocketTransport.MaximumReceiveMessageBytes];
            Array.Fill(bytes, (byte)'a');

            using (var accumulator = new TextMessageAccumulator(
                       ClientWebSocketTransport.MaximumReceiveMessageBytes))
            {
                string message;
                Assert.That(
                    accumulator.Append(bytes, 0, bytes.Length, true, out message),
                    Is.True);
                Assert.That(message.Length, Is.EqualTo(bytes.Length));
            }
        }

        [Test]
        public void Append_RejectsMessageAboveReceiveCeilingAcrossFragments()
        {
            var bytes = new byte[ClientWebSocketTransport.MaximumReceiveMessageBytes];
            using (var accumulator = new TextMessageAccumulator(
                       ClientWebSocketTransport.MaximumReceiveMessageBytes))
            {
                string message;
                Assert.That(
                    accumulator.Append(bytes, 0, bytes.Length, false, out message),
                    Is.False);

                var exception = Assert.Throws<TransportMessageTooLargeException>(() =>
                    accumulator.Append(new byte[1], 0, 1, true, out message));
                Assert.That(
                    exception.MaximumBytes,
                    Is.EqualTo(ClientWebSocketTransport.MaximumReceiveMessageBytes));
            }
        }

        [Test]
        public void Append_RejectsInvalidUtf8AndResetsForNextMessage()
        {
            using (var accumulator = new TextMessageAccumulator(16))
            {
                string message;
                Assert.Throws<DecoderFallbackException>(() =>
                    accumulator.Append(new byte[] { 0xC3, 0x28 }, 0, 2, true, out message));

                var valid = Encoding.UTF8.GetBytes("ok");
                Assert.That(
                    accumulator.Append(valid, 0, valid.Length, true, out message),
                    Is.True);
                Assert.That(message, Is.EqualTo("ok"));
            }
        }
    }
}
