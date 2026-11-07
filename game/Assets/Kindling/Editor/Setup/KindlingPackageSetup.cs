using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Kindling.Editor.Setup
{
    /// <summary>
    /// Installs the Unity packages the project needs. Packages are requested by name only, so the
    /// Package Manager picks the version that matches the installed Unity 6 LTS editor; the resolved
    /// versions are then written to Packages/manifest.json (commit that file).
    /// On first open the user is asked once per editor session; afterwards use the Kindling menu.
    /// </summary>
    [InitializeOnLoad]
    public static class KindlingPackageSetup
    {
        public static readonly string[] RequiredPackages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.addressables",
            "com.unity.burst",
            "com.unity.collections",
            "com.unity.mathematics",
            "com.unity.ugui",
            "com.unity.localization",
            "com.unity.test-framework",
            "com.unity.nuget.newtonsoft-json",
            "com.unity.memoryprofiler",
        };

        const string PromptedSessionKey = "Kindling.Setup.PackagesPrompted";
        static AddAndRemoveRequest s_Request;

        static KindlingPackageSetup()
        {
            if (Application.isBatchMode)
            {
                return; // CI uses the committed manifest; never prompt or modify packages there.
            }

            EditorApplication.delayCall += PromptIfPackagesMissing;
        }

        public static string[] GetMissingPackages()
        {
            var installed = new HashSet<string>(
                UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p => p.name));
            return RequiredPackages.Where(name => !installed.Contains(name)).ToArray();
        }

        [MenuItem("Kindling/Setup/1. Install Required Packages", priority = 1)]
        public static void InstallMissingPackages()
        {
            var missing = GetMissingPackages();
            if (missing.Length == 0)
            {
                Debug.Log("[KINDLING] All required packages are installed.");
                return;
            }

            Install(missing);
        }

        static void PromptIfPackagesMissing()
        {
            if (SessionState.GetBool(PromptedSessionKey, false))
            {
                return;
            }

            var missing = GetMissingPackages();
            if (missing.Length == 0)
            {
                return;
            }

            SessionState.SetBool(PromptedSessionKey, true);
            bool install = EditorUtility.DisplayDialog(
                "KINDLING project setup",
                "These packages are required and not installed yet:\n\n  " + string.Join("\n  ", missing) +
                "\n\nInstall them now? (If Unity asks to enable the new Input System backends, choose Yes; the editor restarts.)",
                "Install",
                "Later");
            if (install)
            {
                Install(missing);
            }
        }

        static void Install(string[] packages)
        {
            if (s_Request != null && !s_Request.IsCompleted)
            {
                Debug.LogWarning("[KINDLING] A package installation is already running.");
                return;
            }

            Debug.Log("[KINDLING] Installing packages: " + string.Join(", ", packages));
            s_Request = Client.AddAndRemove(packages, null);
            EditorApplication.update += WaitForInstall;
        }

        static void WaitForInstall()
        {
            if (s_Request == null || !s_Request.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= WaitForInstall;
            if (s_Request.Status == StatusCode.Success)
            {
                Debug.Log("[KINDLING] Packages installed. Next: Kindling > Setup > 2. Configure Project, " +
                          "then 3. Create Render Pipeline Tiers.");
            }
            else
            {
                Debug.LogError("[KINDLING] Package installation failed: " +
                               (s_Request.Error != null ? s_Request.Error.message : "unknown error"));
            }

            s_Request = null;
        }
    }
}
