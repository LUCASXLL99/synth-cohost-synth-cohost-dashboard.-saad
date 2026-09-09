using System;
using System.Collections.Generic;
using System.Linq;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Features.Avatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SynthCohost.Editor
{
    public static class SynthCohostDashboardAvatarSetup
    {
        public const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        public const string AvatarPrefabPath = "Assets/SynthCohost/Prefabs/DashboardAvatar.prefab";
        public const string SourceAvatarName = "AvatarVerify_anime_model";
        public const string DashboardScenePath = "Assets/Scenes/SynthCohostDashboard.unity";
        public const string DashboardPrefabPath = "Assets/SynthCohost/Prefabs/SynthCohostDashboard.prefab";

        private static readonly Vector3 LiveCameraPosition = new Vector3(0.62f, 1.42f, 1.28f);
        private static readonly Vector3 LiveCameraEuler = new Vector3(6f, 199f, 0f);
        private const float LiveCameraFov = 81f;

        [MenuItem("Tools/Synth Cohost/Install Dashboard Avatar In Live Test Scene")]
        public static void InstallDashboardAvatarInLiveTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException(
                    "Wait for compilation to finish and exit Play Mode before installing the dashboard avatar.");
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureFolder("Assets/SynthCohost", "Prefabs");
            CaptureAvatarPrefabFromSampleScene();
            InstallIntoLiveTestScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[SynthCohost] Dashboard avatar installed in the live-test scene and wired to the WebSocket client.");
        }

        [MenuItem("Tools/Synth Cohost/Create or Repair Dashboard Scene")]
        public static void CreateOrRepairDashboardScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException(
                    "Wait for compilation to finish and exit Play Mode before creating the dashboard scene.");
            }

            EditorSceneManager.SaveOpenScenes();

            var settings = AssetDatabase.LoadAssetAtPath<SynthCohostConnectionSettings>(
                SynthCohostLiveTestSetup.SettingsPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "Live connection settings are missing. Run Create or Repair Live Test Scene first.");
            }

            var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
            if (avatarPrefab == null)
            {
                InstallDashboardAvatarInLiveTestScene();
                avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
            }

            if (avatarPrefab == null)
            {
                throw new InvalidOperationException("DashboardAvatar prefab is missing.");
            }

            var runtimePrefab = CreateDashboardRuntimePrefab(settings);
            CreateDashboardScene(runtimePrefab, avatarPrefab);
            AddDashboardSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SynthCohost] Dashboard scene is ready: {DashboardScenePath}.");
        }

        private static void CaptureAvatarPrefabFromSampleScene()
        {
            var previous = SceneManager.GetActiveScene().path;
            var sample = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            GameObject source = null;
            foreach (var root in sample.GetRootGameObjects())
            {
                if (root.name == SourceAvatarName)
                {
                    source = root;
                    break;
                }
            }

            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not find '{SourceAvatarName}' in {SampleScenePath}.");
            }

            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = "DashboardAvatar";
            clone.SetActive(true);
            var tester = clone.GetComponent<AvatarClipTester>();
            if (tester != null)
            {
                UnityEngine.Object.DestroyImmediate(tester);
            }

            var animator = clone.GetComponent<Animator>();
            if (animator == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                throw new InvalidOperationException("Source avatar has no Animator.");
            }

            animator.applyRootMotion = false;
            var presenter = clone.GetComponent<DashboardAvatarPresenter>();
            if (presenter == null)
            {
                presenter = clone.AddComponent<DashboardAvatarPresenter>();
            }

            var presenterSo = new SerializedObject(presenter);
            presenterSo.FindProperty("animator").objectReferenceValue = animator;
            presenterSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(clone, AvatarPrefabPath);
            UnityEngine.Object.DestroyImmediate(clone);

            if (!string.IsNullOrEmpty(previous) &&
                !string.Equals(previous, SampleScenePath, StringComparison.Ordinal) &&
                System.IO.File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
        }

        private static void InstallIntoLiveTestScene()
        {
            var scene = EditorSceneManager.OpenScene(SynthCohostLiveTestSetup.ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "DashboardAvatar" ||
                    root.name == "DashboardAvatar_Ground" ||
                    root.name == "DashboardAvatar_FillLight")
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Missing {AvatarPrefabPath}.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "DashboardAvatar";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance, scene);

            var presenter = instance.GetComponent<DashboardAvatarPresenter>();
            var client = UnityEngine.Object.FindFirstObjectByType<SynthCohostClientBehaviour>();
            var panel = UnityEngine.Object.FindFirstObjectByType<SynthCohostLiveTestPanel>();
            if (client == null || presenter == null)
            {
                throw new InvalidOperationException("Live-test client or dashboard presenter is missing.");
            }

            var clientSo = new SerializedObject(client);
            clientSo.FindProperty("avatarController").objectReferenceValue = presenter;
            clientSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(client);

            if (panel != null)
            {
                var panelSo = new SerializedObject(panel);
                panelSo.FindProperty("dashboardAvatar").objectReferenceValue = presenter;
                panelSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
            }

            FrameCamera(scene);
            EnsureGround(scene);
            EnsureFillLight(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SynthCohostLiveTestSetup.ScenePath))
            {
                throw new InvalidOperationException("Could not save the live-test scene.");
            }
        }

        private static void FrameCamera(Scene scene)
        {
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                camera = root.GetComponent<Camera>();
                if (camera != null)
                {
                    break;
                }
            }

            if (camera == null)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(
                LiveCameraPosition,
                Quaternion.Euler(LiveCameraEuler));
            camera.fieldOfView = LiveCameraFov;
            camera.nearClipPlane = 0.05f;
        }

        private static void EnsureGround(Scene scene)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "DashboardAvatar_Ground";
            ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ground.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        private static void EnsureFillLight(Scene scene)
        {
            var lightGo = new GameObject("DashboardAvatar_FillLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.55f;
            lightGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static readonly Vector3 DashboardCameraPosition = new Vector3(0f, 1.38f, 3.55f);
        private static readonly Vector3 DashboardCameraEuler = new Vector3(6f, 180f, 0f);
        private const float DashboardCameraFov = 38f;

        private static GameObject CreateDashboardRuntimePrefab(SynthCohostConnectionSettings settings)
        {
            var root = new GameObject("Synth Cohost Dashboard");
            try
            {
                var client = root.AddComponent<SynthCohostClientBehaviour>();
                var bootstrap = root.AddComponent<SynthCohostRuntimeBootstrap>();
                root.AddComponent<SynthCohostSafeStatusHud>();

                var clientSo = new SerializedObject(client);
                clientSo.FindProperty("settings").objectReferenceValue = settings;
                clientSo.ApplyModifiedPropertiesWithoutUndo();

                var bootSo = new SerializedObject(bootstrap);
                bootSo.FindProperty("client").objectReferenceValue = client;
                bootSo.FindProperty("settings").objectReferenceValue = settings;
                bootSo.FindProperty("connectOnStart").boolValue = true;
                bootSo.FindProperty("persistAcrossScenes").boolValue = false;
                bootSo.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, DashboardPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateDashboardScene(GameObject runtimePrefab, GameObject avatarPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                camera = root.GetComponent<Camera>();
                if (camera != null)
                {
                    break;
                }
            }

            if (camera != null)
            {
                camera.gameObject.name = DashboardStreamCameraMarker.DefaultObjectName;
                if (camera.GetComponent<DashboardStreamCameraMarker>() == null)
                {
                    camera.gameObject.AddComponent<DashboardStreamCameraMarker>();
                }

                camera.transform.SetPositionAndRotation(
                    DashboardCameraPosition,
                    Quaternion.Euler(DashboardCameraEuler));
                camera.fieldOfView = DashboardCameraFov;
                camera.nearClipPlane = 0.05f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 1f);
            }

            var runtime = (GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab, scene);
            runtime.name = "Synth Cohost Dashboard";
            SceneManager.MoveGameObjectToScene(runtime, scene);

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(avatarPrefab, scene);
            avatar.name = "DashboardAvatar";
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(avatar, scene);

            var presenter = avatar.GetComponent<DashboardAvatarPresenter>();
            var client = runtime.GetComponent<SynthCohostClientBehaviour>();
            if (client != null && presenter != null)
            {
                var clientSo = new SerializedObject(client);
                clientSo.FindProperty("avatarController").objectReferenceValue = presenter;
                clientSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(client);
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "DashboardAvatar_Ground";
            ground.transform.localScale = new Vector3(1.4f, 1f, 1.4f);
            SceneManager.MoveGameObjectToScene(ground, scene);

            var lightGo = new GameObject("DashboardAvatar_FillLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.55f;
            lightGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);

            if (!EditorSceneManager.SaveScene(scene, DashboardScenePath))
            {
                throw new InvalidOperationException($"Could not save {DashboardScenePath}.");
            }
        }

        private static void AddDashboardSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.All(scene => !string.Equals(scene.path, DashboardScenePath, StringComparison.Ordinal)))
            {
                scenes.Add(new EditorBuildSettingsScene(DashboardScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
