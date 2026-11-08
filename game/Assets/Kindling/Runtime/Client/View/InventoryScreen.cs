using System.Collections.Generic;
using Kindling.Content;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>
    /// Inventory, clothing, an open container and crafting (IMGUI, works with touch and mouse).
    /// Tap a slot to select it, tap another slot to move the stack there. Every change is a request to the
    /// server; the screen shows whatever the server sends back.
    /// </summary>
    public sealed class InventoryScreen
    {
        readonly GuiKit m_Kit;
        ContainerRef m_Selected;
        int m_SelectedSlot = -1;
        bool m_Crafting;
        Vector2 m_RecipeScroll;

        public InventoryScreen(GuiKit kit)
        {
            m_Kit = kit;
        }

        public bool IsOpen { get; private set; }

        public void Open(bool crafting = false)
        {
            IsOpen = true;
            m_Crafting = crafting;
            m_SelectedSlot = -1;
        }

        public void Close()
        {
            IsOpen = false;
            m_SelectedSlot = -1;
        }

        /// <summary>Draws the screen. Returns false when the player closed it.</summary>
        public bool Draw(ClientSession session)
        {
            if (!IsOpen)
            {
                return false;
            }

            m_Kit.EnsureStyles();
            ClientWorld w = session.WorldState;
            Rect safe = Screen.safeArea;
            var area = new Rect(safe.x + m_Kit.Px(8), Screen.height - safe.yMax + m_Kit.Px(8), safe.width - m_Kit.Px(16), safe.height - m_Kit.Px(16));
            m_Kit.DrawRect(area, new Color(0.05f, 0.05f, 0.06f, 0.88f));
            GUILayout.BeginArea(new Rect(area.x + m_Kit.Px(8), area.y + m_Kit.Px(6), area.width - m_Kit.Px(16), area.height - m_Kit.Px(12)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!m_Crafting, " Inventory", m_Kit.Button, GUILayout.Width(m_Kit.Px(140))))
            {
                m_Crafting = false;
            }

            if (GUILayout.Toggle(m_Crafting, " Crafting", m_Kit.Button, GUILayout.Width(m_Kit.Px(140))))
            {
                m_Crafting = true;
            }

            GUILayout.FlexibleSpace();
            bool close = GUILayout.Button("Close", m_Kit.Button, GUILayout.Width(m_Kit.Px(110)));
            GUILayout.EndHorizontal();
            GUILayout.Space(m_Kit.Px(6));

            if (m_Crafting)
            {
                DrawCrafting(session);
            }
            else
            {
                DrawInventory(session, w);
            }

            GUILayout.EndArea();
            if (close)
            {
                IsOpen = false;
                if (w.OpenContainerId != 0)
                {
                    session.CloseContainer();
                }
            }

            return IsOpen;
        }

        void DrawInventory(ClientSession session, ClientWorld w)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(m_Kit.Px(6 * 58)));
            GUILayout.Label("Hotbar", m_Kit.SmallLabel);
            Grid(session, ContainerRef.Hotbar, w.Inventory.Hotbar, 6);
            GUILayout.Label("Inventory", m_Kit.SmallLabel);
            Grid(session, ContainerRef.Main, w.Inventory.Main, 6);
            GUILayout.Label("Clothing (head, chest, legs, feet, hands, back)", m_Kit.SmallLabel);
            Grid(session, ContainerRef.Wear, w.Inventory.Wear, 6);
            GUILayout.EndVertical();

            GUILayout.Space(m_Kit.Px(12));
            GUILayout.BeginVertical();
            if (w.OpenContainer != null && w.OpenContainerId != 0 && w.Entities.TryGetValue(w.OpenContainerId, out ChunkEntity entity))
            {
                EntityDef def = w.DefOf(entity);
                GUILayout.Label(def != null ? (entity.Label != null ? $"{def.Name}: {entity.Label}" : def.Name) : "Container", m_Kit.Label);
                Grid(session, ContainerRef.OfEntity(w.OpenContainerId), w.OpenContainer, 6);
                if (def?.Processor != null)
                {
                    bool lit = (entity.Flags & Simulation.World.WorldEntityFlags.Lit) != 0;
                    if (GUILayout.Button(lit ? "Put out" : "Light", m_Kit.Button, GUILayout.Width(m_Kit.Px(140))))
                    {
                        session.ToggleProcessor(entity.Id);
                    }
                }
            }

            GUILayout.FlexibleSpace();
            DrawSelectionActions(session, w);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void DrawSelectionActions(ClientSession session, ClientWorld w)
        {
            ItemContainer container = Resolve(w, m_Selected);
            if (container == null || m_SelectedSlot < 0 || m_SelectedSlot >= container.Capacity || container[m_SelectedSlot].IsEmpty)
            {
                GUILayout.Label("Tap an item, then tap where it should go.", m_Kit.SmallLabel);
                return;
            }

            ItemStack s = container[m_SelectedSlot];
            GUILayout.Label($"{s.Def.Name} ×{s.Amount}" + (s.Def.HasCondition ? $" · {s.Condition:0}%" : string.Empty), m_Kit.Label);
            GUILayout.BeginHorizontal();
            if ((s.Def.Consume != null || s.Def.Slot != EquipSlot.None) && m_Selected.Kind != ContainerKind.Entity && m_Selected.Kind != ContainerKind.Wear && GUILayout.Button(s.Def.Consume != null ? "Use" : "Wear", m_Kit.Button))
            {
                session.Use(m_Selected, m_SelectedSlot);
            }

            if (s.Amount > 1 && GUILayout.Button("Split", m_Kit.Button))
            {
                int empty = container.FirstEmptySlot(s.Def);
                if (empty >= 0)
                {
                    session.MoveItem(m_Selected, m_SelectedSlot, m_Selected, empty, s.Amount / 2);
                }
            }

            if (w.OpenContainerId != 0 && GUILayout.Button(m_Selected.Kind == ContainerKind.Entity ? "Take" : "Put in", m_Kit.Button))
            {
                session.QuickMove(m_Selected, m_SelectedSlot, m_Selected.Kind == ContainerKind.Entity ? ContainerRef.Main : ContainerRef.OfEntity(w.OpenContainerId));
            }

            if (GUILayout.Button("Drop", m_Kit.Button))
            {
                session.Drop(m_Selected, m_SelectedSlot, s.Amount);
                m_SelectedSlot = -1;
            }

            GUILayout.EndHorizontal();
        }

        void Grid(ClientSession session, ContainerRef where, ItemContainer container, int columns)
        {
            float size = m_Kit.Px(54);
            for (int row = 0; row * columns < container.Capacity; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int slot = row * columns + col;
                    if (slot >= container.Capacity)
                    {
                        break;
                    }

                    ItemStack s = container[slot];
                    bool selected = m_SelectedSlot == slot && Same(m_Selected, where);
                    string label = s.IsEmpty ? string.Empty : s.Amount > 1 ? $"{Hud.ShortName(s.Def)}\n{s.Amount}" : Hud.ShortName(s.Def);
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = selected ? new Color(0.98f, 0.55f, 0.2f) : old;
                    if (GUILayout.Button(label, m_Kit.Button, GUILayout.Width(size), GUILayout.Height(size)))
                    {
                        OnSlot(session, where, slot, s);
                    }

                    GUI.backgroundColor = old;
                }

                GUILayout.EndHorizontal();
            }
        }

        void OnSlot(ClientSession session, ContainerRef where, int slot, ItemStack s)
        {
            if (m_SelectedSlot < 0)
            {
                if (!s.IsEmpty)
                {
                    m_Selected = where;
                    m_SelectedSlot = slot;
                }

                return;
            }

            if (!(Same(m_Selected, where) && m_SelectedSlot == slot))
            {
                ItemContainer from = Resolve(session.WorldState, m_Selected);
                if (from != null && m_SelectedSlot < from.Capacity && !from[m_SelectedSlot].IsEmpty)
                {
                    session.MoveItem(m_Selected, m_SelectedSlot, where, slot, from[m_SelectedSlot].Amount);
                }
            }

            m_SelectedSlot = -1;
        }

        void DrawCrafting(ClientSession session)
        {
            ClientWorld w = session.WorldState;
            ContentDatabase content = session.Content;
            GUILayout.BeginHorizontal();
            m_RecipeScroll = GUILayout.BeginScrollView(m_RecipeScroll, GUILayout.Width(Screen.safeArea.width * 0.62f));
            foreach (RecipeDef r in content.Recipes)
            {
                bool can = w.Inventory.Has(r.Inputs);
                var needs = new List<string>();
                foreach (ItemAmount input in r.Inputs)
                {
                    needs.Add($"{input.Amount} {input.Item.Name} ({w.Inventory.Count(input.Item)})");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{r.Name}{(r.Station != StationKind.Hand ? " · needs " + r.Station : string.Empty)}\n{string.Join(", ", needs)}", can ? m_Kit.Label : m_Kit.SmallLabel);
                GUI.enabled = can;
                if (GUILayout.Button("Craft", m_Kit.Button, GUILayout.Width(m_Kit.Px(90))))
                {
                    session.Craft(r, 1);
                }

                GUI.enabled = can && w.Inventory.Has(r.Inputs, 5);
                if (GUILayout.Button("×5", m_Kit.Button, GUILayout.Width(m_Kit.Px(60))))
                {
                    session.Craft(r, 5);
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.BeginVertical();
            GUILayout.Label("Queue", m_Kit.Label);
            foreach (CraftJobInfo job in w.Owner.Crafting)
            {
                RecipeDef r = content.ByNetId<RecipeDef>(job.RecipeId);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{r?.Name} ×{job.Remaining} ({(r != null ? r.Seconds - job.Progress : 0):0}s)", m_Kit.SmallLabel);
                if (GUILayout.Button("Cancel", m_Kit.Button, GUILayout.Width(m_Kit.Px(90))))
                {
                    session.CancelCraft(job.Id);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        static ItemContainer Resolve(ClientWorld w, ContainerRef r)
        {
            switch (r.Kind)
            {
                case ContainerKind.Main: return w.Inventory.Main;
                case ContainerKind.Hotbar: return w.Inventory.Hotbar;
                case ContainerKind.Wear: return w.Inventory.Wear;
                default: return r.EntityId == w.OpenContainerId ? w.OpenContainer : null;
            }
        }

        static bool Same(ContainerRef a, ContainerRef b) => a.Kind == b.Kind && a.EntityId == b.EntityId;
    }

    /// <summary>Downed and death screens.</summary>
    public sealed class LifeOverlay
    {
        readonly GuiKit m_Kit;

        public LifeOverlay(GuiKit kit)
        {
            m_Kit = kit;
        }

        /// <summary>Draws the overlay for the player's life state. Returns true while it is showing (no gameplay input).</summary>
        public bool Draw(ClientSession session)
        {
            ClientWorld w = session.WorldState;
            if (!w.HasOwnerState || w.Owner.Life == LifeState.Alive)
            {
                return false;
            }

            m_Kit.EnsureStyles();
            Rect safe = Screen.safeArea;
            float width = Mathf.Min(safe.width - m_Kit.Px(40), m_Kit.Px(420));
            var area = new Rect(safe.x + (safe.width - width) / 2f, Screen.height - safe.yMax + safe.height * 0.25f, width, safe.height * 0.6f);
            m_Kit.DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.15f, 0f, 0f, w.Owner.Life == LifeState.Dead ? 0.55f : 0.3f));
            GUILayout.BeginArea(area);
            if (w.Owner.Life == LifeState.Downed)
            {
                GUILayout.Label($"You're down — {w.Owner.DownedLeft:0} s", m_Kit.Title);
                GUILayout.Label("A teammate can help you up.", m_Kit.Label);
                if (GUILayout.Button("Give up", m_Kit.Button))
                {
                    session.GiveUp();
                }
            }
            else
            {
                GUILayout.Label("You died", m_Kit.Title);
                GUILayout.Label(Cause((DamageCause)w.Owner.LastDamage), m_Kit.Label);
                GUILayout.Space(m_Kit.Px(8));
                if (GUILayout.Button("Respawn somewhere random", m_Kit.Button))
                {
                    session.Respawn(0);
                }

                foreach (RespawnPointInfo bed in w.Owner.RespawnPoints)
                {
                    GUI.enabled = bed.CooldownLeft <= 0;
                    string label = bed.CooldownLeft <= 0 ? "Respawn at your bedroll" : $"Bedroll ready in {Mathf.FloorToInt(bed.CooldownLeft / 60)}:{Mathf.FloorToInt(bed.CooldownLeft % 60):00}";
                    if (GUILayout.Button(label, m_Kit.Button))
                    {
                        session.Respawn(bed.EntityId);
                    }

                    GUI.enabled = true;
                }
            }

            GUILayout.EndArea();
            return true;
        }

        static string Cause(DamageCause cause)
        {
            switch (cause)
            {
                case DamageCause.Starvation: return "You starved.";
                case DamageCause.Dehydration: return "You died of thirst.";
                case DamageCause.Cold: return "You froze.";
                case DamageCause.Bleeding: return "You bled out.";
                case DamageCause.Poison: return "Something you ate was poisonous.";
                case DamageCause.GaveUp: return "You gave up.";
                default: return string.Empty;
            }
        }
    }
}
