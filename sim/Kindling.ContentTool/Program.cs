using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kindling.Content;

namespace Kindling.ContentTool
{
    public static class Program
    {
        const string Usage = @"Kindling content tool

  validate      load and validate all content (exit 1 on problems)
  update-lock   give new definitions permanent net ids in content.lock.json, retire removed ones
  export        write packages/content/content-export.json
  check         validate + fail if the lock file or export is out of date (CI)";

        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "help";
            DirectoryContentSource source = DirectoryContentSource.FindDefault();
            string repoRoot = Path.GetFullPath(Path.Combine(source.Directory, "..", "..", "..", "..", ".."));
            string exportPath = Path.Combine(repoRoot, "packages", "content", "content-export.json");
            string lockPath = Path.Combine(source.Directory, ContentFiles.Lock);

            switch (command)
            {
                case "validate":
                    return Validate(source, out _);
                case "update-lock":
                {
                    ContentDatabase db = ContentDatabase.Parse(source, requireLockIds: false, out List<string> problems);
                    ContentLock contentLock = File.Exists(lockPath) ? ContentLock.Parse(File.ReadAllText(lockPath)) : new ContentLock();
                    bool changed = contentLock.Update(db.AllKeys.Select(k => k.Value));
                    File.WriteAllText(lockPath, contentLock.ToJson());
                    Console.WriteLine(changed ? $"Updated {lockPath} ({contentLock.Ids.Count} ids, {contentLock.Retired.Count} retired)." : "Lock file already up to date.");
                    return Validate(source, out _);
                }

                case "export":
                {
                    if (Validate(source, out ContentDatabase db) != 0)
                    {
                        return 1;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
                    File.WriteAllText(exportPath, ContentExporter.Export(db));
                    Console.WriteLine($"Wrote {exportPath}");
                    return 0;
                }

                case "check":
                {
                    if (Validate(source, out ContentDatabase db) != 0)
                    {
                        return 1;
                    }

                    var contentLock = ContentLock.Parse(File.ReadAllText(lockPath));
                    if (contentLock.Update(db.AllKeys.Select(k => k.Value)))
                    {
                        Console.Error.WriteLine("content.lock.json is out of date: run `dotnet run --project sim/Kindling.ContentTool -- update-lock` and commit it.");
                        return 1;
                    }

                    string expected = ContentExporter.Export(db);
                    if (!File.Exists(exportPath) || File.ReadAllText(exportPath) != expected)
                    {
                        Console.Error.WriteLine("packages/content/content-export.json is out of date: run `dotnet run --project sim/Kindling.ContentTool -- export` and commit it.");
                        return 1;
                    }

                    Console.WriteLine("Content, lock file and export are up to date.");
                    return 0;
                }

                default:
                    Console.WriteLine(Usage);
                    return command == "help" ? 0 : 2;
            }
        }

        static int Validate(IContentSource source, out ContentDatabase db)
        {
            try
            {
                db = ContentDatabase.Load(source);
            }
            catch (ContentException e)
            {
                Console.Error.WriteLine(e.Message);
                db = null;
                return 1;
            }

            Console.WriteLine($"Content OK: {db.Items.Count} items, {db.Recipes.Count} recipes, {db.Nodes.Count} nodes, {db.Entities.Count} entities, {db.LootTables.Count} loot tables.");
            return 0;
        }
    }
}
