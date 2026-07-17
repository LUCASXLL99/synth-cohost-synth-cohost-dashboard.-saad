using System;
using System.Linq;
using NUnit.Framework;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Development;
using UnityEditor;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Development
{
    public sealed class LiveTestSetupAssetTests
    {
        private const string SettingsPath =
            "Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset";
        private const string PrefabPath =
            "Assets/SynthCohost/Prefabs/SynthCohostLiveTest.prefab";
        private const string ScenePath =
            "Assets/Scenes/SynthCohostLiveTest.unity";

        [Test]
        public void LiveSettings_UseSafeCurrentV2Defaults()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SynthCohostConnectionSettings>(SettingsPath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.EndpointUrl, Is.EqualTo(SynthCohostConnectionSettings.DefaultLiveEndpoint));
            Assert.That(settings.AutoConnect, Is.False);
            Assert.That(settings.RunInBackground, Is.True);
            Assert.That(settings.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(75)));
            Assert.That(settings.AuthSendTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(settings.FinalTurnResponseTimeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
            Assert.That(settings.HeartbeatInterval, Is.EqualTo(TimeSpan.FromSeconds(20)));
            Assert.That(settings.DiagnosticLogLevel, Is.EqualTo(DiagnosticLogLevel.Verbose));
        }

        [Test]
        public void LiveTestPrefab_HasEveryRequiredReference()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SynthCohostConnectionSettings>(SettingsPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            var client = prefab.GetComponent<SynthCohostClientBehaviour>();
            var avatar = prefab.GetComponent<LiveTestAvatarBehaviorAdapter>();
            var ai = prefab.GetComponent<LiveTestAiResponseAdapter>();
            var errors = prefab.GetComponent<LiveTestSystemErrorAdapter>();
            var panel = prefab.GetComponent<SynthCohostLiveTestPanel>();
            Assert.That(client, Is.Not.Null);
            Assert.That(avatar, Is.Not.Null);
            Assert.That(ai, Is.Not.Null);
            Assert.That(errors, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);

            var clientObject = new SerializedObject(client);
            Assert.That(clientObject.FindProperty("settings").objectReferenceValue, Is.SameAs(settings));
            Assert.That(clientObject.FindProperty("avatarController").objectReferenceValue, Is.SameAs(avatar));
            Assert.That(clientObject.FindProperty("aiResponseSink").objectReferenceValue, Is.SameAs(ai));
            Assert.That(clientObject.FindProperty("systemErrorSink").objectReferenceValue, Is.SameAs(errors));

            var panelObject = new SerializedObject(panel);
            Assert.That(panelObject.FindProperty("client").objectReferenceValue, Is.SameAs(client));
            Assert.That(panelObject.FindProperty("settings").objectReferenceValue, Is.SameAs(settings));
            Assert.That(panelObject.FindProperty("avatarAdapter").objectReferenceValue, Is.SameAs(avatar));
            Assert.That(panelObject.FindProperty("aiResponseAdapter").objectReferenceValue, Is.SameAs(ai));
            Assert.That(panelObject.FindProperty("systemErrorAdapter").objectReferenceValue, Is.SameAs(errors));
        }

        [Test]
        public void LiveTestPanel_DoesNotSerializeCredentials()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.GetComponent<SynthCohostLiveTestPanel>();
            var serialized = new SerializedObject(panel);

            Assert.That(serialized.FindProperty("accessToken"), Is.Null);
            Assert.That(serialized.FindProperty("avatarId"), Is.Null);
        }

        [Test]
        public void LiveTestScene_IsEnabledAndDependsOnConfiguredPrefab()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            Assert.That(
                EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath),
                Is.True);

            var dependencies = AssetDatabase.GetDependencies(ScenePath, true);
            Assert.That(dependencies, Does.Contain(PrefabPath));
            Assert.That(dependencies, Does.Contain(SettingsPath));
        }
    }
}
