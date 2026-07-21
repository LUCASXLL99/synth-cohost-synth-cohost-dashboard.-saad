using System;
using System.Text;
using NUnit.Framework;
using SynthCohost.Runtime.Authentication;

namespace SynthCohost.Tests.EditMode.Authentication
{
    public sealed class AuthTokenFlowTests
    {
        [Test]
        public void JwtInspector_ReadsExpiryAndRefreshDue()
        {
            var expected = DateTimeOffset.UtcNow.AddMinutes(15);
            var payload = ToBase64Url(
                $"{{\"exp\":{expected.ToUnixTimeSeconds()},\"iat\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}");
            var fakeToken = $"header.{payload}.signature";

            Assert.That(
                JwtAccessTokenInspector.TryGetExpirationUtc(fakeToken, out var actual),
                Is.True);
            Assert.That(actual.ToUnixTimeSeconds(), Is.EqualTo(expected.ToUnixTimeSeconds()));
            Assert.That(
                JwtAccessTokenInspector.TryGetRefreshDueUtc(
                    fakeToken,
                    TimeSpan.FromMinutes(2),
                    out var due),
                Is.True);
            Assert.That(due, Is.EqualTo(expected - TimeSpan.FromMinutes(2)));
        }

        [TestCase("wss://synth-cohost-app.onrender.com/ws", "https://synth-cohost-app.onrender.com/")]
        [TestCase("ws://127.0.0.1:8080/ws", "http://127.0.0.1:8080/")]
        public void AuthHttpClient_BuildsRestBaseFromWebSocket(string websocket, string expected)
        {
            Assert.That(
                AuthHttpClient.TryBuildRestBaseUri(websocket, out var restBase, out var error),
                Is.True);
            Assert.That(error, Is.Empty);
            Assert.That(restBase.AbsoluteUri, Is.EqualTo(expected));
        }

        [Test]
        public void RuntimeAuthSession_RotatesBothTokensTogether()
        {
            var session = new RuntimeAuthSession();
            session.SetSession("access-one", "refresh-one", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            session.ReplaceTokens(new AuthTokenPair("access-two", "refresh-two"));

            Assert.That(session.TryGetAccessToken(out var access), Is.True);
            Assert.That(access, Is.EqualTo("access-two"));
            Assert.That(session.TryGetRefreshToken(out var refresh), Is.True);
            Assert.That(refresh, Is.EqualTo("refresh-two"));
        }

        [Test]
        public void RuntimeAuthSession_ClearRemovesRefreshAndAccess()
        {
            var session = new RuntimeAuthSession();
            session.SetSession("access-one", "refresh-one", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            session.Clear();

            Assert.That(session.HasAccessCredentials, Is.False);
            Assert.That(session.HasRefreshToken, Is.False);
        }

        [Test]
        public void AuthTokenPair_ToStringRedactsSecrets()
        {
            var pair = new AuthTokenPair("secret-access", "secret-refresh");
            Assert.That(pair.ToString(), Does.Not.Contain("secret-access"));
            Assert.That(pair.ToString(), Does.Not.Contain("secret-refresh"));
            Assert.That(pair.ToString(), Does.Contain("REDACTED"));
        }

        private static string ToBase64Url(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
