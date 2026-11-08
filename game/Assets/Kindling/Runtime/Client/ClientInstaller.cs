using Kindling.Gameplay.Boot;
using UnityEngine;

namespace Kindling.Client
{
    /// <summary>Creates the client once the Boot scene has loaded (not in server processes).</summary>
    public static class ClientInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Bootstrapper.RunsServer || Object.FindAnyObjectByType<ClientRoot>() != null)
            {
                return;
            }

            var go = new GameObject("KindlingClient");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ClientRoot>();
        }
    }
}
