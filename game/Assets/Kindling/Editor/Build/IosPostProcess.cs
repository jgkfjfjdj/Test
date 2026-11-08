#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace Kindling.Editor.Build
{
    /// <summary>
    /// Adjusts the generated Xcode project's Info.plist:
    /// - ITSAppUsesNonExemptEncryption = false: the app only uses HTTPS/TLS provided by iOS, which is
    ///   exempt, so App Store Connect doesn't hold each TestFlight build for the export-compliance question.
    /// - The app is a game and full-screen landscape only.
    /// Compiled only when the active build target is iOS (UnityEditor.iOS.Xcode exists only then).
    /// </summary>
    public sealed class IosPostProcess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            root.SetString("LSApplicationCategoryType", "public.app-category.games");
            root.SetBoolean("UIRequiresFullScreen", true);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
