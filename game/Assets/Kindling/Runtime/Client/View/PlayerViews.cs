using System.Collections.Generic;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>First-person camera for the local player and simple avatars for remote players.</summary>
    public sealed class PlayerViews
    {
        const float StandingVisualHeight = 1.8f;
        const float CrouchVisualHeight = 1.2f;
        const float EyeHeightSharpness = 12f;

        readonly Dictionary<ushort, Avatar> m_Avatars = new Dictionary<ushort, Avatar>();
        readonly List<RemotePlayer> m_Remotes = new List<RemotePlayer>();
        readonly List<ushort> m_Stale = new List<ushort>();
        readonly GreyboxMaterials m_Materials;
        Camera m_Camera;
        Transform m_AvatarRoot;
        float m_EyeHeight = 1.62f;

        sealed class Avatar
        {
            public GameObject Root;
            public Transform Body;
            public MeshRenderer BodyRenderer;
            public bool Away;
        }

        public PlayerViews(GreyboxMaterials materials)
        {
            m_Materials = materials;
        }

        public Camera Camera => m_Camera;

        public IReadOnlyList<RemotePlayer> Remotes => m_Remotes;

        public void Sync(ClientSession session, float viewYaw, float viewPitch, float deltaTime)
        {
            EnsureCamera();
            bool crouched = session.PredictedState.Crouched;
            float targetEye = session.Movement.EyeHeightFor(crouched);
            m_EyeHeight = Mathf.Lerp(m_EyeHeight, targetEye, 1f - Mathf.Exp(-EyeHeightSharpness * deltaTime));

            Vector3 feet = session.GetRenderPosition().ToUnity();
            m_Camera.transform.SetPositionAndRotation(feet + new Vector3(0f, m_EyeHeight, 0f), Conversions.ViewRotation(viewYaw, viewPitch));

            session.GetRemotePlayers(m_Remotes);
            m_Stale.Clear();
            foreach (ushort id in m_Avatars.Keys)
            {
                m_Stale.Add(id);
            }

            foreach (RemotePlayer remote in m_Remotes)
            {
                m_Stale.Remove(remote.Id);
                if (!m_Avatars.TryGetValue(remote.Id, out Avatar avatar))
                {
                    avatar = CreateAvatar(remote.Id);
                    m_Avatars[remote.Id] = avatar;
                }

                bool remoteCrouched = (remote.Flags & EntityFlags.Crouched) != 0;
                bool downed = (remote.Flags & EntityFlags.Downed) != 0;
                float height = remoteCrouched ? CrouchVisualHeight : StandingVisualHeight;
                avatar.Root.transform.SetPositionAndRotation(remote.Position.ToUnity(), Quaternion.Euler(0f, remote.Yaw, 0f));
                avatar.Body.localScale = new Vector3(0.6f, height * 0.5f, 0.6f);
                avatar.Body.localPosition = downed ? new Vector3(0f, 0.3f, 0f) : new Vector3(0f, height * 0.5f, 0f);
                avatar.Body.localRotation = downed ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;

                bool away = (remote.Flags & EntityFlags.Away) != 0;
                if (away != avatar.Away)
                {
                    avatar.Away = away;
                    avatar.BodyRenderer.sharedMaterial = away ? m_Materials.RemoteAway : m_Materials.Remote;
                }
            }

            foreach (ushort id in m_Stale)
            {
                Object.Destroy(m_Avatars[id].Root);
                m_Avatars.Remove(id);
            }
        }

        /// <summary>Head position of a remote player (for name labels).</summary>
        public static Vector3 HeadOf(in RemotePlayer remote) =>
            remote.Position.ToUnity() + new Vector3(0f, ((remote.Flags & EntityFlags.Crouched) != 0 ? CrouchVisualHeight : StandingVisualHeight) + 0.35f, 0f);

        public void Clear()
        {
            foreach (Avatar avatar in m_Avatars.Values)
            {
                Object.Destroy(avatar.Root);
            }

            m_Avatars.Clear();
            m_Remotes.Clear();
        }

        void EnsureCamera()
        {
            if (m_Camera != null)
            {
                return;
            }

            m_Camera = Camera.main;
            if (m_Camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                m_Camera = go.AddComponent<Camera>();
            }

            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = WorldView.SkyColor;
            m_Camera.fieldOfView = 75f;
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 700f;
        }

        Avatar CreateAvatar(ushort id)
        {
            if (m_AvatarRoot == null)
            {
                var root = new GameObject("Remote players");
                Object.DontDestroyOnLoad(root);
                m_AvatarRoot = root.transform;
            }

            var avatar = new Avatar { Root = new GameObject($"Player {id}") };
            avatar.Root.transform.SetParent(m_AvatarRoot, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(avatar.Root.transform, false);
            avatar.Body = body.transform;
            avatar.BodyRenderer = body.GetComponent<MeshRenderer>();
            avatar.BodyRenderer.sharedMaterial = m_Materials.Remote;

            // A dark visor shows which way they face.
            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(visor.GetComponent<Collider>());
            visor.name = "Visor";
            visor.transform.SetParent(body.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0.55f, 0.42f);
            visor.transform.localScale = new Vector3(0.7f, 0.15f, 0.25f);
            visor.GetComponent<MeshRenderer>().sharedMaterial = m_Materials.Visor;
            return avatar;
        }
    }
}
