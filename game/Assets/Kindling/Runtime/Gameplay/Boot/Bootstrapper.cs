using Kindling.Core.Diagnostics;
using Kindling.Core.Timing;
using UnityEngine;

namespace Kindling.Gameplay.Boot
{
    /// <summary>
    /// First object to run in the Boot scene. In M0 it only sets up logging and frame pacing;
    /// later milestones turn it into the composition root described in
    /// docs/blueprint/04-client-and-ios-controls.md §2 (config → logging → platform → backend → content → net → UI).
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class Bootstrapper : MonoBehaviour
    {
        const string LogCategory = "Boot";

        /// <summary>True when running as a headless dedicated server build.</summary>
        public static bool IsDedicatedServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        static Bootstrapper s_Instance;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            Log.SetSink(new UnityLogSink());
            Log.MinimumLevel = Debug.isDebugBuild ? LogLevel.Debug : LogLevel.Info;

            if (IsDedicatedServer)
            {
                // The headless server has no rendering; its main loop is paced at the simulation tick rate.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = TickClock.DefaultTickRate;
                Log.Info(LogCategory,
                    $"KINDLING dedicated server {Application.version} (Unity {Application.unityVersion}) starting at {TickClock.DefaultTickRate} Hz");
            }
            else
            {
                // 30 FPS is the Low/Medium tier target; graphics tiers adjust this from M6 on.
                Application.targetFrameRate = 30;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                Log.Info(LogCategory,
                    $"KINDLING client {Application.version} (Unity {Application.unityVersion}) on {SystemInfo.deviceModel}, {SystemInfo.systemMemorySize} MB RAM");
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}
