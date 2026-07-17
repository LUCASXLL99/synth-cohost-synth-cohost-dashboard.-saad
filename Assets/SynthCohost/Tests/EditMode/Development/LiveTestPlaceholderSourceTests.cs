using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Development
{
    public sealed class LiveTestPlaceholderSourceTests
    {
        [Test]
        public void LocalDraft_ProvidesEditableRuntimeValues()
        {
            const string json =
                "{\"endpointUrl\":\"ws://127.0.0.1:8080/ws\"," +
                "\"accessToken\":\"fake-local-token\"," +
                "\"avatarId\":\"11111111-1111-1111-1111-111111111111\"}";

            var draft = LiveTestPlaceholderSource.Resolve(
                SynthCohostConnectionSettings.DefaultLiveEndpoint,
                json,
                null,
                null,
                null);

            Assert.That(draft.EndpointUrl, Is.EqualTo("ws://127.0.0.1:8080/ws"));
            Assert.That(draft.AccessToken, Is.EqualTo("fake-local-token"));
            Assert.That(draft.AvatarId, Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(draft.SourceSummary, Does.Contain("local UserSettings file"));
        }

        [Test]
        public void EnvironmentValues_OverrideLocalDraftWithoutMutatingFallback()
        {
            const string json =
                "{\"endpointUrl\":\"ws://127.0.0.1:8080/ws\"," +
                "\"accessToken\":\"local-token\"," +
                "\"avatarId\":\"11111111-1111-1111-1111-111111111111\"}";

            var draft = LiveTestPlaceholderSource.Resolve(
                SynthCohostConnectionSettings.DefaultLiveEndpoint,
                json,
                "wss://example.com/ws",
                "environment-token",
                "22222222-2222-2222-2222-222222222222");

            Assert.That(draft.EndpointUrl, Is.EqualTo("wss://example.com/ws"));
            Assert.That(draft.AccessToken, Is.EqualTo("environment-token"));
            Assert.That(draft.AvatarId, Is.EqualTo("22222222-2222-2222-2222-222222222222"));
            Assert.That(draft.SourceSummary, Does.Contain("process environment"));
        }

        [Test]
        public void MalformedLocalDraft_ProducesSafeNoticeWithoutEchoingContents()
        {
            const string secretMarker = "SECRET_LOCAL_CONTENT";

            var draft = LiveTestPlaceholderSource.Resolve(
                SynthCohostConnectionSettings.DefaultLiveEndpoint,
                "{not-json:" + secretMarker,
                null,
                null,
                null);

            Assert.That(draft.EndpointUrl, Is.EqualTo(SynthCohostConnectionSettings.DefaultLiveEndpoint));
            Assert.That(draft.SafeNotice, Is.Not.Empty);
            Assert.That(draft.SafeNotice, Does.Not.Contain(secretMarker));
        }

        [Test]
        public void LocalDraftPath_IsUnderIgnoredUserSettingsFolder()
        {
            var path = LiveTestPlaceholderSource.GetDefaultLocalPath();

            Assert.That(Path.GetFileName(path), Is.EqualTo(LiveTestPlaceholderSource.LocalFileName));
            Assert.That(
                new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty).Name,
                Is.EqualTo("UserSettings").IgnoreCase);
        }

        [Test]
        public void JwtInspector_ReadsExpiryWithoutDependingOnSignatureOrLoggingToken()
        {
            var expected = DateTimeOffset.UtcNow.AddMinutes(10);
            var payload = ToBase64Url($"{{\"exp\":{expected.ToUnixTimeSeconds()}}}");
            var fakeToken = $"header.{payload}.signature";

            Assert.That(
                JwtAccessTokenInspector.TryGetExpirationUtc(fakeToken, out var actual),
                Is.True);
            Assert.That(actual.ToUnixTimeSeconds(), Is.EqualTo(expected.ToUnixTimeSeconds()));
        }

        [TestCase("wss://synth-cohost-app.onrender.com/ws", true)]
        [TestCase("ws://127.0.0.1:8080/ws", true)]
        [TestCase("ws://localhost:8080/ws", true)]
        [TestCase("ws://example.com/ws", false)]
        [TestCase("https://example.com/ws", false)]
        [TestCase("wss://user:secret@example.com/ws", false)]
        [TestCase("wss://example.com/ws?token=secret", false)]
        [TestCase("wss://example.com/ws#fragment", false)]
        public void RuntimeEndpointParser_EnforcesWebSocketAndTlsRules(
            string value,
            bool expected)
        {
            Assert.That(
                SynthCohostConnectionSettings.TryParseEndpoint(value, out _, out _),
                Is.EqualTo(expected));
        }

        [Test]
        public void RuntimeOptionsOverride_DoesNotMutateSettingsAssetValue()
        {
            var settings = ScriptableObject.CreateInstance<SynthCohostConnectionSettings>();
            try
            {
                var original = settings.EndpointUrl;
                var options = settings.CreateRuntimeOptions(
                    new Uri("ws://127.0.0.1:8080/ws"));

                Assert.That(options.Endpoint.AbsoluteUri, Is.EqualTo("ws://127.0.0.1:8080/ws"));
                Assert.That(settings.EndpointUrl, Is.EqualTo(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [TestCase(SessionState.Disconnected, true)]
        [TestCase(SessionState.AuthRequired, true)]
        [TestCase(SessionState.Faulted, true)]
        [TestCase(SessionState.Connecting, false)]
        [TestCase(SessionState.Authenticating, false)]
        [TestCase(SessionState.Ready, false)]
        [TestCase(SessionState.Reconnecting, false)]
        [TestCase(SessionState.Stopping, false)]
        public void RuntimeEndpointChange_IsLimitedToTerminalStates(
            SessionState state,
            bool expected)
        {
            Assert.That(
                SynthCohostClientBehaviour.CanChangeRuntimeEndpoint(state),
                Is.EqualTo(expected));
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
