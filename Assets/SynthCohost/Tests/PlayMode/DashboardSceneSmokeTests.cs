using System.Collections;
using NUnit.Framework;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.PlayMode
{
    public sealed class DashboardSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator DashboardScene_HasNoLiveTestPanel_AndWiresPresenter()
        {
            var load = SceneManager.LoadSceneAsync("SynthCohostDashboard", LoadSceneMode.Single);
            if (load == null)
            {
                Assert.Ignore("SynthCohostDashboard is not in Build Settings yet.");
                yield break;
            }

            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Assert.That(Object.FindFirstObjectByType<SynthCohost.Runtime.Development.SynthCohostLiveTestPanel>(), Is.Null);
            Assert.That(Object.FindFirstObjectByType<SynthCohostClientBehaviour>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<SynthCohostRuntimeBootstrap>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<SynthCohostSafeStatusHud>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<DashboardAvatarPresenter>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<DashboardStreamCameraMarker>(), Is.Not.Null);
        }
    }
}
