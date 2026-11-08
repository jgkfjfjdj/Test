using System;
using System.Collections.Generic;
using System.Globalization;
using Kindling.Client.Input;
using Kindling.Client.View;
using Kindling.Content;
using Kindling.Controls;
using Kindling.Core.Diagnostics;
using Kindling.Gameplay.Content;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Kindling.Client
{
    /// <summary>
    /// The client: a menu (join a server, play offline, host on Wi-Fi), then the survival game with touch
    /// controls (keyboard and mouse on desktop), prediction, the world, inventory and crafting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClientRoot : MonoBehaviour
    {
        const string LogCategory = "Client";
        const float InteractReach = 3f;

        enum Mode
        {
            Menu,
            Connecting,
            Playing,
        }

        readonly ControlSettings m_Controls = new ControlSettings();
        readonly GuiKit m_Kit = new GuiKit();
        readonly WorldView m_World = new WorldView();
        readonly List<Hud.Toast> m_Toasts = new List<Hud.Toast>();
        readonly List<GameEvent> m_Events = new List<GameEvent>();
        readonly Dictionary<ControlButton, int> m_PressCounts = new Dictionary<ControlButton, int>();
        ContentDatabase m_Content;
        TouchSource m_Touch;
        DesktopControls m_Desktop;
        TouchRouter m_Router;
        GreyboxMaterials m_Materials;
        PlayerViews m_Views;
        EntityViews m_Entities;
        Hud m_Hud;
        InventoryScreen m_Inventory;
        LifeOverlay m_LifeOverlay;

        ClientSession m_Session;
        LocalServer m_LocalServer;
        Mode m_Mode = Mode.Menu;
        string m_Status = string.Empty;
        string m_NameField;
        string m_AddressField;
        string m_LastHost;
        int m_LastPort;
        bool m_TouchMode;
        bool m_ShowSettings;
        bool m_OverlayActive;
        string m_HostAddress;
        string m_Prompt;
        InteractTarget m_TargetKind;
        uint m_TargetId;
        uint m_AutoOpenedContainer;
        Vector2 m_LayoutScreen;
        Rect m_LayoutSafeArea;

        void Awake()
        {
            try
            {
                m_Content = UnityContent.Database;
            }
            catch (ContentException e)
            {
                Log.Error(LogCategory, "Game content is broken.", e);
                m_Status = "The game's content failed to load. Please reinstall or update.";
            }

            ClientPrefs.Load(m_Controls);
            m_NameField = ClientPrefs.DisplayName;
            m_AddressField = ClientPrefs.Address;
            m_Touch = new TouchSource();
            m_Desktop = new DesktopControls();
            m_Materials = new GreyboxMaterials();
            m_Views = new PlayerViews(m_Materials);
            m_Entities = new EntityViews(m_Materials);
            m_Hud = new Hud(m_Kit);
            m_Inventory = new InventoryScreen(m_Kit);
            m_LifeOverlay = new LifeOverlay(m_Kit);
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
                ReturnToMenu(reason, keepSession: m_Session.CanResume && m_LocalServer == null);
            }

            if (m_Mode == Mode.Playing)
            {
                CollectEvents();
            }
        }

        void LateUpdate()
        {
            if (m_Mode != Mode.Playing || m_Session == null)
            {
                return;
            }

            m_Views.Sync(m_Session, m_Router.Look.Yaw, m_Router.Look.Pitch, Time.unscaledDeltaTime);
            m_Entities.Sync(m_Session.WorldState);
            Camera cam = m_Views.Camera;
            m_World.Sync(m_Session.WorldState.Nodes, cam != null ? cam.transform.position : Vector3.zero);
            m_World.UpdateSky(cam, m_Session.WorldState.TimeOfDay, m_Session.WorldState.NightFraction);
        }

        void OnGUI()
        {
            m_Kit.EnsureStyles();
            if (m_Mode == Mode.Playing && m_Session != null)
            {
                string hostInfo = m_LocalServer != null && m_LocalServer.IsUdp
                    ? $"Hosting on {m_HostAddress ?? "this device"}:{m_LocalServer.Config.Port} · {m_LocalServer.Server.Players.Count} player(s)"
                    : null;
                m_Toasts.RemoveAll(t => t.Until < Time.unscaledTime);
                bool leave = m_Hud.Draw(m_Session, m_Router, m_Controls, m_Views, m_Entities, m_TouchMode, ClientPrefs.ShowStats, hostInfo, m_OverlayActive ? null : m_Prompt, m_Toasts);
                bool inventory = m_Inventory.Draw(m_Session);
                bool life = !inventory && m_LifeOverlay.Draw(m_Session);
                if (leave && !inventory && !life)
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
                m_LocalServer?.Save(); // iOS may kill a backgrounded app without warning
            }
            else if (m_Session.Status == ClientStatus.Connected)
            {
                m_Session.NotifyBackground(false);
            }
            else if (m_Session.CanResume && m_LastHost != null)
            {
                StartConnecting(m_LastHost, m_LastPort, "Reconnecting…");
            }
        }

        // ---------------------------------------------------------------- controls

        void ReadControls()
        {
            if (LayoutChanged())
            {
                m_Router.SetLayout(BuildLayout());
                m_PressCounts.Clear();
            }

            ClientWorld w = m_Session.WorldState;
            bool alive = w.Life == LifeState.Alive;
            m_OverlayActive = m_Inventory.IsOpen || !alive;
            UpdateLookTarget(w, alive);

            if (w.OpenContainerId != 0 && w.OpenContainerId != m_AutoOpenedContainer && !m_Inventory.IsOpen)
            {
                m_AutoOpenedContainer = w.OpenContainerId;
                m_Inventory.Open(); // looting: show the container
            }
            else if (w.OpenContainerId == 0)
            {
                m_AutoOpenedContainer = 0;
            }

            if (m_OverlayActive)
            {
                m_Router.ReleaseAll(Time.realtimeSinceStartupAsDouble);
                if (!m_TouchMode)
                {
                    DesktopControls.SetCursorLocked(false);
                    m_Desktop.ReadScreenKeys();
                    if (m_Desktop.InventoryPressed && m_Inventory.IsOpen)
                    {
                        CloseInventory();
                    }
                }

                m_Session.SetInput(NVector2.Zero, m_Router.Look.Yaw, m_Router.Look.Pitch, InputButtons.None);
                return;
            }

            ControlFrame frame;
            if (m_TouchMode)
            {
                m_Router.Process(m_Touch.Read(1f / GuiKit.PixelsPerPoint), Time.realtimeSinceStartupAsDouble);
                frame = m_Router.Consume();
                if (Pressed(ControlButton.Menu))
                {
                    LeaveGame();
                    return;
                }

                if (Pressed(ControlButton.Bag))
                {
                    m_Inventory.Open();
                }

                if (Pressed(ControlButton.Interact))
                {
                    Interact();
                }

                for (int i = 0; i < CharacterInventory.HotbarSlots; i++)
                {
                    if (Pressed((ControlButton)((int)ControlButton.Hotbar1 + i)))
                    {
                        HotbarTap(i);
                    }
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

                if (m_Desktop.InventoryPressed)
                {
                    m_Inventory.Open();
                }

                if (m_Desktop.InteractPressed)
                {
                    Interact();
                }

                if (m_Desktop.HotbarKey >= 0)
                {
                    m_Session.SelectHotbar(w.Owner.ActiveSlot == m_Desktop.HotbarKey ? -1 : m_Desktop.HotbarKey);
                }

                if (m_Desktop.UsePressed && w.Owner.ActiveSlot >= 0)
                {
                    UseOrPlace(w.Owner.ActiveSlot);
                }
            }

            m_Session.SetInput(frame.Move, frame.Yaw, frame.Pitch, frame.Held);
            m_Session.Press(frame.Pressed);
        }

        bool Pressed(ControlButton id)
        {
            TouchButton b = m_Router.Layout.Find(id);
            if (b == null)
            {
                return false;
            }

            m_PressCounts.TryGetValue(id, out int seen);
            m_PressCounts[id] = b.PressCount;
            return b.PressCount != seen;
        }

        void UpdateLookTarget(ClientWorld w, bool alive)
        {
            m_Prompt = null;
            m_TargetKind = InteractTarget.None;
            if (alive && w.FindInteractTarget(m_Session.EyePosition, GameWorld.ViewDirection(m_Router.Look.Yaw, m_Router.Look.Pitch), InteractReach, out InteractTarget kind, out uint id))
            {
                m_TargetKind = kind;
                m_TargetId = id;
                m_Prompt = Describe(w, kind, id);
            }

            TouchButton interact = m_Router.Layout.Find(ControlButton.Interact);
            if (interact != null)
            {
                interact.Visible = m_TargetKind != InteractTarget.None;
            }
        }

        string Describe(ClientWorld w, InteractTarget kind, uint id)
        {
            switch (kind)
            {
                case InteractTarget.Pond:
                    return "Drink";
                case InteractTarget.Node:
                    return "Pick " + w.Map.Nodes[(int)id].Def.Name;
                case InteractTarget.Entity:
                    if (!w.Entities.TryGetValue(id, out ChunkEntity e))
                    {
                        return null;
                    }

                    EntityDef def = w.DefOf(e);
                    if (def.Kind == EntityKind.DroppedItem)
                    {
                        ItemDef item = m_Content.ByNetId<ItemDef>(e.ItemId);
                        return item != null ? $"Pick up {item.Name} ×{e.ItemAmount}" : "Pick up";
                    }

                    return e.Label != null ? $"Open {def.Name} ({e.Label})" : "Open " + def.Name;
                default:
                    return null;
            }
        }

        void Interact()
        {
            if (m_TargetKind != InteractTarget.None)
            {
                m_Session.Interact(m_TargetKind, m_TargetId);
            }
        }

        void HotbarTap(int slot)
        {
            ClientWorld w = m_Session.WorldState;
            if (w.Owner.ActiveSlot != slot)
            {
                m_Session.SelectHotbar(slot);
                return;
            }

            ItemStack s = w.Inventory.Hotbar[slot];
            if (s.IsEmpty || (s.Def.Deploys == null && s.Def.Consume == null && s.Def.Slot == EquipSlot.None))
            {
                m_Session.SelectHotbar(-1); // tap the selected tool again to put it away
                return;
            }

            UseOrPlace(slot);
        }

        void UseOrPlace(int slot)
        {
            ItemStack s = m_Session.WorldState.Inventory.Hotbar[slot];
            if (s.IsEmpty)
            {
                return;
            }

            if (s.Def.Deploys != null)
            {
                NVector3 forward = GameWorld.ViewDirection(m_Router.Look.Yaw, 0f);
                m_Session.Deploy(slot, m_Session.PredictedState.Position + forward * 2.5f, m_Router.Look.Yaw + 180f);
            }
            else
            {
                m_Session.Use(ContainerRef.Hotbar, slot);
            }
        }

        void CloseInventory()
        {
            m_Inventory.Close();
            if (m_Session.WorldState.OpenContainerId != 0)
            {
                m_Session.CloseContainer();
            }
        }

        void CollectEvents()
        {
            m_Events.Clear();
            m_Session.WorldState.DrainEvents(m_Events);
            foreach (GameEvent e in m_Events)
            {
                string text = null;
                bool bad = false;
                ItemDef item = e.ItemNetId != 0 ? m_Content.ByNetId<ItemDef>(e.ItemNetId) : null;
                switch (e.Kind)
                {
                    case GameEventKind.ItemsGained:
                        text = $"+{e.Amount} {item?.Name}";
                        break;
                    case GameEventKind.ToolBroke:
                        text = $"Your {item?.Name} broke";
                        bad = true;
                        break;
                    case GameEventKind.CraftFinished:
                        text = $"Crafted {item?.Name}";
                        break;
                    case GameEventKind.GatherHit when e.Flag:
                        text = "Sweet spot!";
                        break;
                    case GameEventKind.Notice:
                        text = DescribeError((ActionError)e.Code);
                        bad = true;
                        break;
                }

                if (text != null)
                {
                    Toast(text, bad);
                }
            }
        }

        void OnActionCompleted(ushort requestId, ActionError error)
        {
            if (error != ActionError.None && error != ActionError.Cooldown)
            {
                Toast(DescribeError(error), true);
            }
        }

        void Toast(string text, bool bad)
        {
            m_Toasts.Add(new Hud.Toast { Text = text, Bad = bad, Until = Time.unscaledTime + 3f });
            if (m_Toasts.Count > 8)
            {
                m_Toasts.RemoveAt(0);
            }
        }

        static string DescribeError(ActionError error)
        {
            switch (error)
            {
                case ActionError.NotPermitted: return "You need a better tool for that.";
                case ActionError.NotEnoughItems: return "You don't have the materials.";
                case ActionError.NotEnoughSpace: return "No room.";
                case ActionError.StationMissing: return "You need to be at the right bench.";
                case ActionError.QueueFull: return "Crafting queue is full.";
                case ActionError.InvalidPlacement: return "Can't place that here.";
                case ActionError.TooFar: return "Too far away.";
                case ActionError.NoAccess: return "You can't use that.";
                case ActionError.NotKnown: return "You don't know that recipe yet.";
                case ActionError.Cooldown: return "Not yet.";
                case ActionError.SlotRestricted: return "That doesn't go there.";
                default: return error.ToString();
            }
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

        ClientSession NewSession(IClientTransport transport)
        {
            var session = new ClientSession(transport, MakeSettings(), m_Content);
            session.ActionCompleted += OnActionCompleted;
            return session;
        }

        void StartConnecting(string host, int port, string status)
        {
            m_LastHost = host;
            m_LastPort = port;
            m_Session ??= NewSession(new LiteNetClientTransport());
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
            m_LocalServer = LocalServer.CreateOffline(m_Content);
            if (m_LocalServer == null)
            {
                m_Status = "Could not start the offline world.";
                return;
            }

            m_Session = NewSession(m_LocalServer.CreateClientTransport());
            m_Session.Connect("offline", m_LocalServer.Config.Port);
            m_Mode = Mode.Connecting;
            m_Status = "Loading your offline world…";
        }

        void HostOnWifi()
        {
            SaveMenuFields();
            EndSession();
            m_LocalServer = LocalServer.CreateUdp(m_Content, NetProtocol.DefaultPort);
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
            AuthToken = ClientPrefs.DeviceIdentity,
        };

        void EnterPlay()
        {
            m_Mode = Mode.Playing;
            m_Status = string.Empty;
            m_World.Build(m_Session.Map, m_Materials);
            m_Router.SetLayout(BuildLayout());
            m_PressCounts.Clear();
            foreach (TouchButton b in m_Router.Layout.Buttons)
            {
                m_PressCounts[b.Id] = b.PressCount;
            }

            m_Router.Look.Set(m_Session.PredictedState.Yaw, 0f);
            Application.targetFrameRate = 60;
            if (!m_TouchMode)
            {
                DesktopControls.SetCursorLocked(true);
            }

            Log.Info(LogCategory, $"Playing as player {m_Session.PlayerId}{(m_Session.LastJoinWasResume ? " (returning character)" : string.Empty)}.");
        }

        void LeaveGame()
        {
            EndSession();
            ReturnToMenu("You left the game. Your character stays in the world, asleep.", keepSession: false);
        }

        void ReturnToMenu(string status, bool keepSession)
        {
            m_Mode = Mode.Menu;
            m_Status = status;
            m_Views.Clear();
            m_Entities.Clear();
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
                m_Session.ActionCompleted -= OnActionCompleted;
                m_Session.Dispose();
                m_Session = null;
            }

            if (m_LocalServer != null)
            {
                m_LocalServer.Dispose(); // saves the local world
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
            GUILayout.Label($"Pre-alpha {Application.version} · survival test (M3)", m_Kit.SmallLabel);
            GUILayout.Space(m_Kit.Px(8));

            if (m_Content == null)
            {
                GUILayout.Label(m_Status, m_Kit.Label);
                GUILayout.EndArea();
                return;
            }

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

            if (m_Session != null && m_Session.CanResume && m_LastHost != null && GUILayout.Button("Reconnect", m_Kit.Button))
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
            GUILayout.Label($"Technical test build: survival basics with greybox art. {SystemInfo.deviceModel} · {SystemInfo.operatingSystem}", m_Kit.SmallLabel);
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

            host = text;
            return true;
        }

        static string DescribeDisconnect(DisconnectCode code, string message)
        {
            switch (code)
            {
                case DisconnectCode.Timeout: return "Connection lost. Your character is asleep where you left; reconnect to wake up.";
                case DisconnectCode.ConnectionFailed: return "Couldn't reach the server. Check the address and that you're on the same network.";
                case DisconnectCode.ProtocolMismatch:
                case DisconnectCode.ContentMismatch: return string.IsNullOrEmpty(message) ? "Your game version doesn't match the server." : message;
                case DisconnectCode.ServerFull: return "The server is full.";
                case DisconnectCode.AuthFailed: return "The server refused to let you join.";
                case DisconnectCode.ServerShutdown: return "The server shut down (the world was saved).";
                case DisconnectCode.Kicked: return string.IsNullOrEmpty(message) ? "You were removed from the server." : message;
                case DisconnectCode.Replaced: return "You joined from another device.";
                case DisconnectCode.HandshakeTimeout: return "The server didn't answer in time.";
                default: return string.IsNullOrEmpty(message) ? $"Disconnected ({code})." : message;
            }
        }
    }
}
