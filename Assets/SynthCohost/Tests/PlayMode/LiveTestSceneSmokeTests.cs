using System.Collections;
using NUnit.Framework;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.PlayMode
{
    public sealed class LiveTestSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator LiveTestScene_LoadsConfiguredAndWaitsForRuntimeCredentials()
        {
            var load = SceneManager.LoadSceneAsync("SynthCohostLiveTest", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

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
        }
    }
}
