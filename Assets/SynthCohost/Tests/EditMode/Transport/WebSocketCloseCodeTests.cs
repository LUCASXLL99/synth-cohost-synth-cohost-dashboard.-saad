using System;
using NUnit.Framework;

namespace SynthCohost.Transport.Tests
{
    public sealed class WebSocketCloseCodeTests
    {
        [TestCase(1000)]
        [TestCase(1003)]
        [TestCase(4000)]
        [TestCase(4001)]
        [TestCase(4002)]
        [TestCase(4003)]
        [TestCase(4004)]
        [TestCase(4999)]
        public void IsValidToSend_AcceptsStandardAndApplicationCodes(int code)
        {
            Assert.That(WebSocketCloseCode.IsValidToSend(code), Is.True);
        }

        [TestCase(999)]
        [TestCase(1004)]
        [TestCase(1005)]
        [TestCase(1006)]
        [TestCase(1015)]
        [TestCase(5000)]
        public void IsValidToSend_RejectsInvalidOrReservedCodes(int code)
        {
            Assert.That(WebSocketCloseCode.IsValidToSend(code), Is.False);
        }

        [Test]
        public void ValidateToSend_UsesUtf8ByteLengthForReasonLimit()
        {
            var exactlyAtLimit = new string('a', WebSocketCloseCode.MaximumReasonUtf8Bytes);
            Assert.DoesNotThrow(() =>
                WebSocketCloseCode.ValidateToSend(4000, exactlyAtLimit));

            var tooLongInUtf8 = new string('é', 62);
            Assert.Throws<ArgumentException>(() =>
                WebSocketCloseCode.ValidateToSend(4000, tooLongInUtf8));
        }
    }
}
