using System;
using System.Collections.Generic;
using System.IO;

namespace Kindling.Content
{
    /// <summary>Supplies the text of content files by name (items.json, …). Unity reads TextAssets; .NET reads files.</summary>
    public interface IContentSource
    {
        string Read(string fileName);
    }

    public sealed class DirectoryContentSource : IContentSource
    {
        public DirectoryContentSource(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }

        public string Read(string fileName) => File.ReadAllText(Path.Combine(Directory, fileName));

        /// <summary>Finds game/Assets/Kindling/Resources/Content by walking up from the working directory or the app's directory.</summary>
        public static DirectoryContentSource FindDefault()
        {
            foreach (string start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    foreach (string candidate in new[] { Path.Combine(dir.FullName, "game", ContentFiles.AssetsRelativeDirectory), Path.Combine(dir.FullName, ContentFiles.AssetsRelativeDirectory) })
                    {
                        if (File.Exists(Path.Combine(candidate, ContentFiles.Items)))
                        {
                            return new DirectoryContentSource(candidate);
                        }
                    }

                    dir = dir.Parent;
                }
            }

            throw new DirectoryNotFoundException("Content directory not found (expected game/" + ContentFiles.AssetsRelativeDirectory + ").");
        }
    }

    public sealed class DictionaryContentSource : IContentSource
    {
        readonly Dictionary<string, string> m_Files;

        public DictionaryContentSource(Dictionary<string, string> files)
        {
            m_Files = files;
        }

        public string Read(string fileName) => m_Files.TryGetValue(fileName, out string text) ? text : throw new FileNotFoundException(fileName);
    }

    public static class ContentFiles
    {
        /// <summary>Under Assets/: Unity loads these as TextAssets from Resources/Content.</summary>
        public const string AssetsRelativeDirectory = "Assets/Kindling/Resources/Content";
        public const string ResourcesPath = "Content";

        public const string Items = "items.json";
        public const string Recipes = "recipes.json";
        public const string Nodes = "nodes.json";
        public const string Entities = "entities.json";
        public const string Loot = "loot.json";
        public const string Rules = "rules.json";
        public const string Lock = "content.lock.json";

        public static readonly string[] DefinitionFiles = { Items, Recipes, Nodes, Entities, Loot };
    }
}
