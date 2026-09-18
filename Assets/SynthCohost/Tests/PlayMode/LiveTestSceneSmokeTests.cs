using System.Collections;
using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Features.Avatar;
using SynthCohost.Runtime.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.PlayMode
{
    public sealed class LiveTestSceneSmokeTests
    {
        [SetUp]
        public void DisableLiveTokenRefreshDuringSmokeTests()
        {
            SynthCohostLiveTestPanel.AutoRefreshSavedTokenOnPlay = false;
        }

        [TearDown]
        public void RestoreLiveTokenRefresh()
        {
            SynthCohostLiveTestPanel.AutoRefreshSavedTokenOnPlay = true;
        }

        [UnityTest]
        public IEnumerator LiveTestScene_LoadsConfiguredAndWaitsForRuntimeCredentials()
        {
            yield return LoadLiveTestScene();

            var client = Object.FindFirstObjectByType<SynthCohostClientBehaviour>();
            Assert.That(client, Is.Not.Null);
            Assert.That(client.isActiveAndEnabled, Is.True);
            Assert.That(client.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(client.Status, Is.Not.Null);
            Assert.That(client.Status.HasSession, Is.False);

            Assert.That(Object.FindFirstObjectByType<SynthCohostLiveTestPanel>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<LiveTestAvatarBehaviorAdapter>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<LiveTestAiResponseAdapter>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<LiveTestSystemErrorAdapter>(), Is.Not.Null);

            var dashboardAvatar = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            Assert.That(dashboardAvatar, Is.Not.Null);
            Assert.That(dashboardAvatar.GetComponent<Animator>(), Is.Not.Null);
            Assert.That(dashboardAvatar.GetComponent<Animator>().runtimeAnimatorController, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DashboardPresenter_PreviewAndRapidReplace_DoNotNeedTheSocket()
        {
            yield return LoadLiveTestScene();

            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            Assert.That(presenter, Is.Not.Null);

            var thinking = presenter.PreviewLocalAsync(AvatarBehavior.Thinking, default);
            while (!thinking.IsCompleted)
            {
                yield return null;
            }

            Assert.That(thinking.Result, Is.True);
            Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Thinking));

            var idleAgain = presenter.PreviewLocalAsync(AvatarBehavior.Idle, default);
            while (!idleAgain.IsCompleted)
            {
                yield return null;
            }

            Assert.That(idleAgain.Result, Is.True);

            var speaking = presenter.PreviewLocalAsync(AvatarBehavior.Speaking, default);
            while (!speaking.IsCompleted)
            {
                yield return null;
            }

            Assert.That(speaking.Result, Is.True);
            Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Speaking));
            Assert.That(Object.FindFirstObjectByType<SynthCohostClientBehaviour>().State,
                Is.EqualTo(SessionState.Disconnected));
        }

        [UnityTest]
        public IEnumerator DisconnectedClient_CanSwitchEndpointWithoutConnecting()
        {
            yield return LoadLiveTestScene();

            var client = Object.FindFirstObjectByType<SynthCohostClientBehaviour>();
            Assert.That(client.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(
                client.TrySetRuntimeEndpoint("ws://127.0.0.1:8080/ws", out var error),
                Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(client.EffectiveEndpoint.IsLoopback, Is.True);

            Assert.That(
                client.TrySetRuntimeEndpoint(
                    SynthCohost.Runtime.Configuration.SynthCohostConnectionSettings.DefaultLiveEndpoint,
                    out _),
                Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyingTheClient_DoesNotThrowAfterSceneLoad()
        {
            yield return LoadLiveTestScene();

            var client = Object.FindFirstObjectByType<SynthCohostClientBehaviour>();
            Object.Destroy(client.gameObject);
            yield return null;
            Assert.That(Object.FindFirstObjectByType<SynthCohostClientBehaviour>(), Is.Null);
        }

        private static IEnumerator LoadLiveTestScene()
        {
            var load = SceneManager.LoadSceneAsync("SynthCohostLiveTest", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
        }
    }
}
