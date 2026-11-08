using System;
using System.IO;
using System.Linq;
using Kindling.Editor.Setup;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kindling.Editor.Build
{
    /// <summary>
    /// Build entry points for the editor menu and CI (GameCI / -executeMethod).
    /// CI iOS:    -executeMethod Kindling.Editor.Build.KindlingBuild.CiBuildIos
    ///            -customBuildPath &lt;folder&gt; [-kindlingVersion 0.1.0] [-kindlingBuild 123]
    ///            [-bundleId com.you.kindling] [-appleTeamId ABCDE12345]
    /// CI server: -executeMethod Kindling.Editor.Build.KindlingBuild.CiBuildLinuxServer -customBuildPath &lt;path&gt;
    /// CI builds start from a clean checkout, so they apply the project setup first (idempotent).
    /// </summary>
    public static class KindlingBuild
    {
        public const string ServerExecutableName = "KindlingServer.x86_64";

        [MenuItem("Kindling/Build/iOS Xcode Project", priority = 100)]
        public static void BuildIosMenu() => Report(BuildIos("Builds/iOS", development: true));

        [MenuItem("Kindling/Build/Linux Dedicated Server", priority = 101)]
        public static void BuildServerMenu() =>
            Report(BuildLinuxServer(Path.Combine("Builds/Server/Linux", ServerExecutableName), development: true));

        public static BuildReport BuildIos(string outputFolder, bool development)
        {
            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = outputFolder,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });
        }

        public static BuildReport BuildLinuxServer(string executablePath, bool development)
        {
            string directory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = executablePath,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });
        }

        // ----- CI entry points -----

        public static void CiBuildLinuxServer()
        {
            PrepareProjectForCi();
            string path = GetArgument("-customBuildPath") ?? Path.Combine("Builds/Server/Linux", ServerExecutableName);
            if (Directory.Exists(path) || path.EndsWith("/", StringComparison.Ordinal))
            {
                path = Path.Combine(path, ServerExecutableName);
            }

            ExitWith(BuildLinuxServer(path, development: GetArgument("-development") != null));
        }

        public static void CiBuildIos()
        {
            PrepareProjectForCi();
            ApplyIosReleaseSettings(
                version: GetArgument("-kindlingVersion"),
                buildNumber: GetArgument("-kindlingBuild"),
                bundleId: GetArgument("-bundleId"),
                teamId: GetArgument("-appleTeamId"));
            string path = GetArgument("-customBuildPath") ?? "Builds/iOS";
            Debug.Log($"[KINDLING] Exporting Xcode project to {path}");
            ExitWith(BuildIos(path, development: GetArgument("-development") != null));
        }

        /// <summary>Player settings + Boot scene + URP tier assets. Safe to run on every build.</summary>
        static void PrepareProjectForCi()
        {
            string[] missing = KindlingPackageSetup.GetMissingPackages();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException("Packages missing from Packages/manifest.json: " + string.Join(", ", missing));
            }

            KindlingProjectConfigurator.ConfigureProject();
            // Lives in the URP-only editor assembly, so it's invoked through its menu entry.
            if (!EditorApplication.ExecuteMenuItem("Kindling/Setup/3. Create Render Pipeline Tiers"))
            {
                throw new InvalidOperationException("Render pipeline tier setup is unavailable (is URP installed?).");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Version, build number, bundle id and signing team for App Store builds.</summary>
        static void ApplyIosReleaseSettings(string version, string buildNumber, string bundleId, string teamId)
        {
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version; // CFBundleShortVersionString
            }

            if (!string.IsNullOrEmpty(buildNumber))
            {
                PlayerSettings.iOS.buildNumber = buildNumber; // CFBundleVersion — must increase with every upload
            }

            if (!string.IsNullOrEmpty(bundleId))
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, bundleId);
            }

            if (!string.IsNullOrEmpty(teamId))
            {
                PlayerSettings.iOS.appleDeveloperTeamID = teamId;
                // Signing is finalized by fastlane (match) on the macOS runner.
                PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            }

            Debug.Log($"[KINDLING] iOS {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)} " +
                      $"{PlayerSettings.bundleVersion} ({PlayerSettings.iOS.buildNumber}) team {PlayerSettings.iOS.appleDeveloperTeamID}");
        }

        static string[] EnabledScenes()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No scenes in Build Settings. Run Kindling > Setup > 2. Configure Project first.");
            }

            return scenes;
        }

        static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                {
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : string.Empty;
                }
            }

            return null;
        }

        static void Report(BuildReport report)
        {
            var summary = report.summary;
            string message = $"[KINDLING] Build {summary.result}: {summary.outputPath} " +
                             $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime})";
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }
        }

        static void ExitWith(BuildReport report)
        {
            Report(report);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }
    }
}
