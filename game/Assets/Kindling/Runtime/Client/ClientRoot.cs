using System;
using System.Globalization;
using Kindling.Client.Input;
using Kindling.Client.View;
using Kindling.Controls;
using Kindling.Core.Diagnostics;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Kindling.Client
{
    /// <summary>
    /// The M1 client: a menu (join a server, play offline, host on Wi-Fi), then first-person movement
    /// with touch controls (keyboard and mouse on desktop), prediction and remote players.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClientRoot : MonoBehaviour
    {
        const string LogCategory = "Client";

        enum Mode
        {
            Menu,
            Connecting,
            Playing,
        }

        readonly ControlSettings m_Controls = new ControlSettings();
        readonly GuiKit m_Kit = new GuiKit();
        readonly WorldView m_World = new WorldView();
        TouchSource m_Touch;
        DesktopControls m_Desktop;
        TouchRouter m_Router;
        GreyboxMaterials m_Materials;
        PlayerViews m_Views;
        Hud m_Hud;

        ClientSession m_Session;
        LocalServer m_LocalServer;
        Mode m_Mode = Mode.Menu;
        string m_Status = string.Empty;
        string m_NameField;
        string m_AddressField;
        string m_LastHost;
        int m_LastPort;
        int m_MenuPressCount;
        bool m_TouchMode;
        bool m_ShowSettings;
        string m_HostAddress;
        Vector2 m_LayoutScreen;
        Rect m_LayoutSafeArea;

        void Awake()
        {
            ClientPrefs.Load(m_Controls);
            m_NameField = ClientPrefs.DisplayName;
            m_AddressField = ClientPrefs.Address;
            m_Touch = new TouchSource();
            m_Desktop = new DesktopControls();
            m_Materials = new GreyboxMaterials();
            m_Views = new PlayerViews(m_Materials);
            m_Hud = new Hud(m_Kit);
            m_Router = new TouchRouter(BuildLayout(), m_Controls);
        }

        void OnDestroy()
        {
            EndSession();
            m_Touch?.Dispose();
            m_World.Destroy();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            m_TouchMode = m_Touch.HasTouchscreen;
            m_LocalServer?.Step(dt);

            if (m_Session == null)
            {
                return;
            }

            if (m_Mode == Mode.Playing)
            {
                ReadControls();
            }

            m_Session.Update(dt);
            m_Hud.Tick(dt);

            if (m_Mode == Mode.Connecting && m_Session.Status == ClientStatus.Connected)
            {
                EnterPlay();
            }
            else if (m_Mode != Mode.Menu && m_Session.Status == ClientStatus.Disconnected)
            {
                string reason = DescribeDisconnect(m_Session.LastDisconnectCode, m_Session.LastDisconnectMessage);
                Log.Info(LogCategory, $"Back to menu: {reason}");
                bool canResume = m_Session.CanResume && m_LocalServer == null;
                ReturnToMenu(reason, keepSession: canResume);
            }
        }

        void LateUpdate()
        {
            if (m_Mode == Mode.Playing && m_Session != null)
            {
                m_Views.Sync(m_Session, m_Router.Look.Yaw, m_Router.Look.Pitch, Time.unscaledDeltaTime);
            }
        }

        void OnGUI()
        {
            m_Kit.EnsureStyles();
            if (m_Mode == Mode.Playing && m_Session != null)
            {
                string hostInfo = m_LocalServer != null && m_LocalServer.IsUdp
                    ? $"Hosting on {m_HostAddress ?? "this device"}:{m_LocalServer.Config.Port} · {m_LocalServer.Server.Players.Count} player(s)"
                    : null;
                if (m_Hud.Draw(m_Session, m_Router, m_Controls, m_Views, m_TouchMode, ClientPrefs.ShowStats, hostInfo))
                {
                    LeaveGame();
                }
            }
            else
            {
                DrawMenu();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (m_Session == null)
            {
                return;
            }

            if (paused)
            {
                m_Router.ReleaseAll(Time.realtimeSinceStartupAsDouble);
                m_Session.NotifyBackground(true);
            }
            else if (m_Session.Status == ClientStatus.Connected)
            {
                m_Session.NotifyBackground(false);
            }
            else if (m_Session.CanResume && m_LastHost != null)
            {
                // The connection timed out while suspended: take the same player back.
                StartConnecting(m_LastHost, m_LastPort, "Reconnecting…");
            }
        }

        // ---------------------------------------------------------------- controls

        void ReadControls()
        {
            if (LayoutChanged())
            {
                m_Router.SetLayout(BuildLayout());
            }

            ControlFrame frame;
            if (m_TouchMode)
            {
                m_Router.Process(m_Touch.Read(1f / GuiKit.PixelsPerPoint), Time.realtimeSinceStartupAsDouble);
                frame = m_Router.Consume();
                TouchButton menu = m_Router.Layout.Find(ControlButton.Menu);
                if (menu != null && menu.PressCount != m_MenuPressCount)
                {
                    m_MenuPressCount = menu.PressCount;
                    LeaveGame();
                    return;
                }
            }
            else
            {
                frame = m_Desktop.Read(m_Router.Look, m_Router.Aiming);
                if (m_Desktop.MenuRequested)
                {
                    LeaveGame();
                    return;
                }
            }

            m_Session.SetInput(frame.Move, frame.Yaw, frame.Pitch, frame.Held);
            m_Session.Press(frame.Pressed);
        }

        ControlLayout BuildLayout()
        {
            float ppp = GuiKit.PixelsPerPoint;
            m_LayoutScreen = new Vector2(Screen.width, Screen.height);
            m_LayoutSafeArea = Screen.safeArea;
            Rect s = m_LayoutSafeArea;
            var screen = new NVector2(Screen.width / ppp, Screen.height / ppp);
            var safe = new ScreenRect(s.x / ppp, s.y / ppp, s.width / ppp, s.height / ppp);
            return ControlLayout.CreateDefault(screen, safe, m_Controls);
        }

        bool LayoutChanged() => m_LayoutScreen != new Vector2(Screen.width, Screen.height) || m_LayoutSafeArea != Screen.safeArea;

        // ---------------------------------------------------------------- session lifecycle

        void StartConnecting(string host, int port, string status)
        {
            m_LastHost = host;
            m_LastPort = port;
            if (m_Session == null)
            {
                m_Session = new ClientSession(new LiteNetClientTransport(), MakeSettings());
            }

            m_Session.Connect(host, port);
            m_Mode = Mode.Connecting;
            m_Status = status;
        }

        void JoinServer()
        {
            if (!TryParseAddress(m_AddressField, out string host, out int port))
            {
                m_Status = "Enter an address like 192.168.1.20 or 192.168.1.20:27015.";
                return;
            }

            SaveMenuFields();
            EndSession();
            StartConnecting(host, port, $"Connecting to {host}:{port}…");
        }

        void PlayOffline()
        {
            SaveMenuFields();
            EndSession();
            m_LocalServer = LocalServer.CreateOffline();
            if (m_LocalServer == null)
            {
                m_Status = "Could not start the offline server.";
                return;
            }

            m_Session = new ClientSession(m_LocalServer.CreateClientTransport(), MakeSettings());
            m_Session.Connect("offline", m_LocalServer.Config.Port);
            m_Mode = Mode.Connecting;
            m_Status = "Starting offline game…";
        }

        void HostOnWifi()
        {
            SaveMenuFields();
            EndSession();
            m_LocalServer = LocalServer.CreateUdp(NetProtocol.DefaultPort);
            if (m_LocalServer == null)
            {
                m_Status = $"Could not open UDP port {NetProtocol.DefaultPort} (is another server running?).";
                return;
            }

            m_HostAddress = LocalServer.GuessLanAddress();
            StartConnecting("127.0.0.1", NetProtocol.DefaultPort, "Starting local server…");
        }

        ClientSettings MakeSettings() => new ClientSettings
        {
            DisplayName = string.IsNullOrWhiteSpace(m_NameField) ? "Survivor" : m_NameField.Trim(),
            BuildVersion = Application.version,
        };

        void EnterPlay()
        {
            m_Mode = Mode.Playing;
            m_Status = string.Empty;
            m_World.Build(m_Session.Layout, m_Materials);
            m_Router.SetLayout(BuildLayout());
            m_MenuPressCount = m_Router.Layout.Find(ControlButton.Menu)?.PressCount ?? 0;
            if (!m_Session.LastJoinWasResume)
            {
                m_Router.Look.Set(m_Session.PredictedState.Yaw, 0f);
            }

            Application.targetFrameRate = 60;
            if (!m_TouchMode)
            {
                DesktopControls.SetCursorLocked(true);
            }

            Log.Info(LogCategory, $"Playing as player {m_Session.PlayerId}{(m_Session.LastJoinWasResume ? " (resumed)" : string.Empty)}.");
        }

        void LeaveGame()
        {
            EndSession();
            ReturnToMenu("You left the game.", keepSession: false);
        }

        void ReturnToMenu(string status, bool keepSession)
        {
            m_Mode = Mode.Menu;
            m_Status = status;
            m_Views.Clear();
            m_World.Hide();
            m_Router.ReleaseAll(Time.realtimeSinceStartupAsDouble);
            DesktopControls.SetCursorLocked(false);
            Application.targetFrameRate = 30;
            if (!keepSession)
            {
                EndSession();
            }
        }

        void EndSession()
        {
            if (m_Session != null)
            {
                m_Session.Dispose();
                m_Session = null;
            }

            if (m_LocalServer != null)
            {
                m_LocalServer.Dispose();
                m_LocalServer = null;
            }
        }

        // ---------------------------------------------------------------- menu

        void DrawMenu()
        {
            Rect safe = Screen.safeArea;
            float width = Mathf.Min(safe.width - m_Kit.Px(32), m_Kit.Px(460));
            var area = new Rect(safe.x + (safe.width - width) / 2f, Screen.height - safe.yMax + m_Kit.Px(12), width, safe.height - m_Kit.Px(24));
            GUILayout.BeginArea(area);
            GUILayout.Label("KINDLING", m_Kit.Title);
            GUILayout.Label($"Pre-alpha {Application.version} · movement test (M1)", m_Kit.SmallLabel);
            GUILayout.Space(m_Kit.Px(8));

            if (m_Mode == Mode.Connecting)
            {
                GUILayout.Label(m_Status, m_Kit.Label);
                if (GUILayout.Button("Cancel", m_Kit.Button))
                {
                    EndSession();
                    ReturnToMenu("Cancelled.", keepSession: false);
                }

                GUILayout.EndArea();
                return;
            }

            if (m_ShowSettings)
            {
                DrawSettings();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Name", m_Kit.SmallLabel);
            m_NameField = GUILayout.TextField(m_NameField ?? string.Empty, 24, m_Kit.TextField);
            GUILayout.Label("Server address", m_Kit.SmallLabel);
            m_AddressField = GUILayout.TextField(m_AddressField ?? string.Empty, 64, m_Kit.TextField);
            GUILayout.Space(m_Kit.Px(6));

            if (m_Session != null && m_Session.CanResume && m_LastHost != null && GUILayout.Button("Reconnect (keep my player)", m_Kit.Button))
            {
                StartConnecting(m_LastHost, m_LastPort, "Reconnecting…");
            }

            if (GUILayout.Button("Join server", m_Kit.Button))
            {
                JoinServer();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play offline", m_Kit.Button))
            {
                PlayOffline();
            }

            if (GUILayout.Button("Host on Wi-Fi", m_Kit.Button))
            {
                HostOnWifi();
            }

            if (GUILayout.Button("Settings", m_Kit.Button))
            {
                m_ShowSettings = true;
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(m_Status))
            {
                GUILayout.Space(m_Kit.Px(6));
                GUILayout.Label(m_Status, m_Kit.Label);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Technical test build: movement and networking only. {SystemInfo.deviceModel} · {SystemInfo.operatingSystem}", m_Kit.SmallLabel);
            GUILayout.EndArea();
        }

        void DrawSettings()
        {
            GUILayout.Label($"Look sensitivity: {ClientPrefs.SensitivityScale:0.00}×", m_Kit.Label);
            float scale = GUILayout.HorizontalSlider(ClientPrefs.SensitivityScale, 0.2f, 3f, GUILayout.Height(m_Kit.Px(30)));
            if (!Mathf.Approximately(scale, ClientPrefs.SensitivityScale))
            {
                ClientPrefs.SensitivityScale = scale;
                ClientPrefs.Load(m_Controls);
            }

            bool invert = GUILayout.Toggle(ClientPrefs.InvertY, " Invert look up/down", m_Kit.Toggle);
            if (invert != ClientPrefs.InvertY)
            {
                ClientPrefs.InvertY = invert;
                ClientPrefs.Load(m_Controls);
            }

            ClientPrefs.ShowStats = GUILayout.Toggle(ClientPrefs.ShowStats, " Show network stats", m_Kit.Toggle);
            GUILayout.Space(m_Kit.Px(8));
            if (GUILayout.Button("Done", m_Kit.Button))
            {
                ClientPrefs.Save();
                m_ShowSettings = false;
            }
        }

        void SaveMenuFields()
        {
            ClientPrefs.DisplayName = m_NameField;
            ClientPrefs.Address = m_AddressField;
            ClientPrefs.Save();
        }

        static bool TryParseAddress(string text, out string host, out int port)
        {
            host = null;
            port = NetProtocol.DefaultPort;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int end = text.IndexOf(']');
                if (end < 0)
                {
                    return false;
                }

                host = text.Substring(1, end - 1);
                string rest = text.Substring(end + 1);
                return rest.Length == 0 || (rest[0] == ':' && int.TryParse(rest.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536);
            }

            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                host = text.Substring(0, colon);
                return int.TryParse(text.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536;
            }

            host = text; // a hostname, IPv4, or a bare IPv6 address
            return true;
        }

        static string DescribeDisconnect(DisconnectCode code, string message)
        {
            switch (code)
            {
                case DisconnectCode.Timeout: return "Connection lost. You can reconnect and keep your player for 2 minutes.";
                case DisconnectCode.ConnectionFailed: return "Couldn't reach the server. Check the address and that you're on the same network.";
                case DisconnectCode.ProtocolMismatch: return string.IsNullOrEmpty(message) ? "Your game version doesn't match the server." : message;
                case DisconnectCode.ServerFull: return "The server is full.";
                case DisconnectCode.AuthFailed: return "The server refused to let you join.";
                case DisconnectCode.ServerShutdown: return "The server shut down.";
                case DisconnectCode.Kicked: return string.IsNullOrEmpty(message) ? "You were removed from the server." : message;
                case DisconnectCode.Replaced: return "You joined from another device.";
                case DisconnectCode.HandshakeTimeout: return "The server didn't answer in time.";
                default: return string.IsNullOrEmpty(message) ? $"Disconnected ({code})." : message;
            }
        }
    }
}
