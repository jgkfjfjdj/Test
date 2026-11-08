using Kindling.Controls;
using UnityEngine;

namespace Kindling.Client
{
    /// <summary>Remembered menu fields and control settings (PlayerPrefs; cloud saves come later).</summary>
    public static class ClientPrefs
    {
        const string NameKey = "kindling.name";
        const string AddressKey = "kindling.address";
        const string SensitivityKey = "kindling.lookSensitivity";
        const string InvertKey = "kindling.invertY";
        const string StatsKey = "kindling.showStats";
        const string DeviceKey = "kindling.deviceIdentity";

        public static string DisplayName
        {
            get => PlayerPrefs.GetString(NameKey, "Survivor");
            set => PlayerPrefs.SetString(NameKey, value ?? string.Empty);
        }

        public static string Address
        {
            get => PlayerPrefs.GetString(AddressKey, "127.0.0.1:27015");
            set => PlayerPrefs.SetString(AddressKey, value ?? string.Empty);
        }

        public static bool ShowStats
        {
            get => PlayerPrefs.GetInt(StatsKey, 1) == 1;
            set => PlayerPrefs.SetInt(StatsKey, value ? 1 : 0);
        }

        /// <summary>
        /// A random id made on first launch: dev servers save your character under it, so you get the same
        /// character back. Replaced by real accounts and join tickets (M2 client work).
        /// </summary>
        public static string DeviceIdentity
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceKey, string.Empty);
                if (id.Length == 0)
                {
                    id = System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(DeviceKey, id);
                    PlayerPrefs.Save();
                }

                return id;
            }
        }

        public static void Load(ControlSettings settings)
        {
            float scale = PlayerPrefs.GetFloat(SensitivityKey, 1f);
            settings.LookSensitivityX = 20f * scale;
            settings.LookSensitivityY = 15f * scale;
            settings.InvertY = PlayerPrefs.GetInt(InvertKey, 0) == 1;
        }

        public static float SensitivityScale
        {
            get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
            set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.2f, 3f));
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(InvertKey, 0) == 1;
            set => PlayerPrefs.SetInt(InvertKey, value ? 1 : 0);
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
