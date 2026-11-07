using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kindling.Editor.Build
{
    /// <summary>
    /// Build entry points for the editor menu and CI (GameCI / -executeMethod).
    /// CI: -executeMethod Kindling.Editor.Build.KindlingBuild.CiBuildLinuxServer -customBuildPath &lt;path&gt;
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
            string path = GetArgument("-customBuildPath") ?? Path.Combine("Builds/Server/Linux", ServerExecutableName);
            if (Directory.Exists(path) || path.EndsWith("/", StringComparison.Ordinal))
            {
                path = Path.Combine(path, ServerExecutableName);
            }

            ExitWith(BuildLinuxServer(path, development: GetArgument("-development") != null));
        }

        public static void CiBuildIos()
        {
            string path = GetArgument("-customBuildPath") ?? "Builds/iOS";
            ExitWith(BuildIos(path, development: GetArgument("-development") != null));
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
