using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kindling.Editor.Rendering
{
    /// <summary>
    /// Creates one URP pipeline asset per device tier (docs/blueprint/04-client-and-ios-controls.md §7).
    /// Values are M0 starting points; M6 profiling tunes them. Only compiled once URP is installed
    /// (see the asmdef's version define). Existing assets are never overwritten.
    /// </summary>
    public static class RenderPipelineTierSetup
    {
        const string Folder = "Assets/Kindling/Settings/Rendering";

        struct Tier
        {
            public string Name;
            public float RenderScale;
            public float ShadowDistance;
            public int ShadowCascades;
            public int Msaa;
            public bool Hdr;
        }

        static readonly Tier[] s_Tiers =
        {
            new Tier { Name = "Low", RenderScale = 0.75f, ShadowDistance = 35f, ShadowCascades = 1, Msaa = 1, Hdr = false },
            new Tier { Name = "Medium", RenderScale = 0.85f, ShadowDistance = 60f, ShadowCascades = 1, Msaa = 1, Hdr = false },
            new Tier { Name = "High", RenderScale = 0.95f, ShadowDistance = 90f, ShadowCascades = 2, Msaa = 2, Hdr = true },
        };

        [MenuItem("Kindling/Setup/3. Create Render Pipeline Tiers", priority = 3)]
        public static void CreateTiers()
        {
            Directory.CreateDirectory(Folder);
            UniversalRenderPipelineAsset medium = null;

            foreach (var tier in s_Tiers)
            {
                string assetPath = $"{Folder}/URP-{tier.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
                if (asset == null)
                {
                    var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, $"{Folder}/URP-{tier.Name}-Renderer.asset");

                    asset = UniversalRenderPipelineAsset.Create(renderer);
                    asset.renderScale = tier.RenderScale;
                    asset.shadowDistance = tier.ShadowDistance;
                    asset.shadowCascadeCount = tier.ShadowCascades;
                    asset.msaaSampleCount = tier.Msaa;
                    asset.supportsHDR = tier.Hdr;
                    AssetDatabase.CreateAsset(asset, assetPath);
                    Debug.Log("[KINDLING] Created " + assetPath);
                }

                if (tier.Name == "Medium")
                {
                    medium = asset;
                }
            }

            AssetDatabase.SaveAssets();

            // Medium is the default; the runtime quality-tier service (M6) swaps assets per device.
            GraphicsSettings.defaultRenderPipeline = medium;
            QualitySettings.renderPipeline = medium;
            Debug.Log("[KINDLING] Render pipeline tiers ready (default: Medium).");
        }
    }
}
