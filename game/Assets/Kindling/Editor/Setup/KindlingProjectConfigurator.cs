using System.IO;
using System.Linq;
using Kindling.Gameplay.Boot;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kindling.Editor.Setup
{
    /// <summary>
    /// Applies the project-wide settings from the blueprint (IL2CPP, landscape iPhone, iOS minimum,
    /// text serialization) and creates the Boot scene. Safe to run more than once.
    /// </summary>
    public static class KindlingProjectConfigurator
    {
        public const string BootScenePath = "Assets/Kindling/Scenes/Boot.unity";

        /// <summary>Placeholder bundle id — replace with your own reverse-DNS id before the first TestFlight upload.</summary>
        public const string DefaultBundleId = "com.example.kindling";

        /// <summary>Development minimum; the launch minimum is decided in M10 (docs/blueprint/04 §7.1).</summary>
        public const string MinimumIosVersion = "17.0";

        [MenuItem("Kindling/Setup/2. Configure Project", priority = 2)]
        public static void ConfigureProject()
        {
            ApplyPlayerSettings();
            ApplyEditorSettings();
            EnsureBootScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[KINDLING] Project configured. Bundle id: " +
                      PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) +
                      " (change it in Player Settings before uploading to TestFlight).");
        }

        static void ApplyPlayerSettings()
        {
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == "game")
            {
                PlayerSettings.productName = "Kindling";
            }

            if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
            {
                PlayerSettings.companyName = "Kindling";
            }

            string bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            if (string.IsNullOrEmpty(bundleId) || bundleId.StartsWith("com.DefaultCompany", System.StringComparison.Ordinal))
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, DefaultBundleId);
            }

            // IL2CPP for the iOS client (required) and the dedicated server (performance).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);

            // iPhone, landscape only.
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.targetOSVersionString = MinimumIosVersion;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Keep simulating when the editor or server window loses focus.
            PlayerSettings.runInBackground = true;
        }

        static void ApplyEditorSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
        }

        static void EnsureBootScene()
        {
            if (!File.Exists(BootScenePath))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("[KINDLING] Boot scene not created: unsaved scene changes were kept.");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(BootScenePath));
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var boot = new GameObject("Bootstrapper");
                boot.AddComponent<Bootstrapper>();
                var camera = new GameObject("Main Camera");
                camera.tag = "MainCamera";
                var cam = camera.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.05f, 0.045f);
                EditorSceneManager.SaveScene(scene, BootScenePath);
                Debug.Log("[KINDLING] Created " + BootScenePath);
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == BootScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
