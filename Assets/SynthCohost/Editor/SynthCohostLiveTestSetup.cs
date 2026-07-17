using System;
using System.Collections.Generic;
using System.Linq;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Development;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SynthCohost.Editor
{
    public static class SynthCohostLiveTestSetup
    {
        public const string SettingsPath =
            "Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset";
        public const string PrefabPath =
            "Assets/SynthCohost/Prefabs/SynthCohostLiveTest.prefab";
        public const string ScenePath =
            "Assets/Scenes/SynthCohostLiveTest.unity";

        [MenuItem("Tools/Synth Cohost/Create or Repair Live Test Scene")]
        public static void CreateOrRepairLiveTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException(
                    "Wait for compilation to finish and exit Play Mode before creating the live-test scene.");
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureFolder("Assets/SynthCohost", "Configuration");
            EnsureFolder("Assets/SynthCohost", "Prefabs");

            var settings = LoadOrCreateSettings();
            var prefab = CreateOrReplacePrefab(settings);
            CreateOrReplaceScene(prefab);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            Debug.Log(
                $"[SynthCohost] Live-test setup is ready: {ScenePath}. " +
                "Enter credentials only while Play Mode is running.");
        }

        private static SynthCohostConnectionSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SynthCohostConnectionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SynthCohostConnectionSettings>();
                settings.name = "SynthCohostLiveConnectionSettings";
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("endpointUrl").stringValue =
                SynthCohostConnectionSettings.DefaultLiveEndpoint;
            serialized.FindProperty("autoConnect").boolValue = false;
            serialized.FindProperty("runInBackground").boolValue = true;
            serialized.FindProperty("connectTimeoutSeconds").floatValue = 75f;
            serialized.FindProperty("authSendTimeoutSeconds").floatValue = 5f;
            serialized.FindProperty("finalTurnResponseTimeoutSeconds").floatValue = 120f;
            serialized.FindProperty("heartbeatIntervalSeconds").floatValue = 20f;
            serialized.FindProperty("diagnosticLogLevel").enumValueIndex =
                (int)DiagnosticLogLevel.Information;

            var reconnect = serialized.FindProperty("reconnect");
            reconnect.FindPropertyRelative("baseDelaySeconds").floatValue = 1f;
            reconnect.FindPropertyRelative("maximumDelaySeconds").floatValue = 30f;
            reconnect.FindPropertyRelative("jitterRatio").floatValue = 0.2f;
            reconnect.FindPropertyRelative("maximumAttempts").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static GameObject CreateOrReplacePrefab(SynthCohostConnectionSettings settings)
        {
            var root = new GameObject("Synth Cohost Live Test");
            try
            {
                var client = root.AddComponent<SynthCohostClientBehaviour>();
                var avatar = root.AddComponent<LiveTestAvatarBehaviorAdapter>();
                var ai = root.AddComponent<LiveTestAiResponseAdapter>();
                var errors = root.AddComponent<LiveTestSystemErrorAdapter>();
                var panel = root.AddComponent<SynthCohostLiveTestPanel>();

                AssignClientReferences(client, settings, avatar, ai, errors);
                AssignPanelReferences(panel, client, settings, avatar, ai, errors);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateOrReplaceScene(GameObject prefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Synth Cohost Live Test";
            SceneManager.MoveGameObjectToScene(instance, scene);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Unity could not save {ScenePath}.");
            }
        }

        private static void AssignClientReferences(
            SynthCohostClientBehaviour client,
            SynthCohostConnectionSettings settings,
            LiveTestAvatarBehaviorAdapter avatar,
            LiveTestAiResponseAdapter ai,
            LiveTestSystemErrorAdapter errors)
        {
            var serialized = new SerializedObject(client);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.FindProperty("avatarController").objectReferenceValue = avatar;
            serialized.FindProperty("aiResponseSink").objectReferenceValue = ai;
            serialized.FindProperty("systemErrorSink").objectReferenceValue = errors;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignPanelReferences(
            SynthCohostLiveTestPanel panel,
            SynthCohostClientBehaviour client,
            SynthCohostConnectionSettings settings,
            LiveTestAvatarBehaviorAdapter avatar,
            LiveTestAiResponseAdapter ai,
            LiveTestSystemErrorAdapter errors)
        {
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("client").objectReferenceValue = client;
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.FindProperty("avatarAdapter").objectReferenceValue = avatar;
            serialized.FindProperty("aiResponseAdapter").objectReferenceValue = ai;
            serialized.FindProperty("systemErrorAdapter").objectReferenceValue = errors;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.All(scene => !string.Equals(scene.path, ScenePath, StringComparison.Ordinal)))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
