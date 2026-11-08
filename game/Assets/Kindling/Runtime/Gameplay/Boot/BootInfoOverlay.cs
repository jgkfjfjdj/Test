#if !UNITY_SERVER
using UnityEngine;

namespace Kindling.Gameplay.Boot
{
    /// <summary>
    /// Temporary on-screen info for pre-alpha device builds (until the real UI arrives in M1/M2):
    /// what build this is, that it has no gameplay yet, and basic device/frame-rate figures.
    /// Drawn with IMGUI so it needs no assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootInfoOverlay : MonoBehaviour
    {
        GUIStyle m_Title;
        GUIStyle m_Body;
        float m_SmoothedDelta = 1f / 30f;
        int m_Scale;

        void Update()
        {
            m_SmoothedDelta = Mathf.Lerp(m_SmoothedDelta, Time.unscaledDeltaTime, 0.05f);
        }

        void EnsureStyles()
        {
            // Scale text with the screen so it stays readable on Retina displays.
            int scale = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) / 360f));
            if (m_Title != null && scale == m_Scale)
            {
                return;
            }

            m_Scale = scale;
            m_Title = new GUIStyle(GUI.skin.label) { fontSize = 22 * scale, fontStyle = FontStyle.Bold, wordWrap = true };
            m_Title.normal.textColor = new Color(0.98f, 0.55f, 0.2f);
            m_Body = new GUIStyle(GUI.skin.label) { fontSize = 11 * scale, wordWrap = true };
            m_Body.normal.textColor = new Color(0.93f, 0.91f, 0.89f);
        }

        void OnGUI()
        {
            EnsureStyles();
            Rect safe = Screen.safeArea;
            // IMGUI's origin is top-left; safeArea's is bottom-left.
            var area = new Rect(safe.x + 16 * m_Scale, Screen.height - safe.yMax + 16 * m_Scale, safe.width - 32 * m_Scale, safe.height - 32 * m_Scale);
            GUILayout.BeginArea(area);
            GUILayout.Label("KINDLING", m_Title);
            GUILayout.Label($"Pre-alpha {Application.version} · Unity {Application.unityVersion}", m_Body);
            GUILayout.Space(8 * m_Scale);
            GUILayout.Label("Technical test build — the game has no gameplay yet. This build checks that the app installs, starts and runs on your iPhone.", m_Body);
            GUILayout.Space(8 * m_Scale);
            GUILayout.Label($"{SystemInfo.deviceModel} · {SystemInfo.operatingSystem}", m_Body);
            GUILayout.Label($"{SystemInfo.systemMemorySize} MB RAM · {SystemInfo.graphicsDeviceName} · {1f / Mathf.Max(0.0001f, m_SmoothedDelta):0} FPS", m_Body);
            GUILayout.EndArea();
        }
    }
}
#endif
