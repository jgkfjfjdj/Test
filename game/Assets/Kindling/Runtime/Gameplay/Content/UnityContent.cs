using System.IO;
using Kindling.Content;
using UnityEngine;

namespace Kindling.Gameplay.Content
{
    /// <summary>Loads the game content JSON (Assets/Kindling/Resources/Content/*.json) through Unity's Resources.</summary>
    public sealed class ResourcesContentSource : IContentSource
    {
        public string Read(string fileName)
        {
            string name = ContentFiles.ResourcesPath + "/" + Path.GetFileNameWithoutExtension(fileName);
            TextAsset asset = Resources.Load<TextAsset>(name);
            if (asset == null)
            {
                throw new FileNotFoundException($"Content file Resources/{name} is missing from the build.");
            }

            string text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }

    public static class UnityContent
    {
        static ContentDatabase s_Database;

        /// <summary>The game's content, loaded and validated once (throws <see cref="ContentException"/> if broken).</summary>
        public static ContentDatabase Database => s_Database ??= ContentDatabase.Load(new ResourcesContentSource());
    }
}
