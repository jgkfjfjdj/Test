using System.Collections.Generic;
using System.IO;
using Kindling.Simulation.Items;
using Kindling.Simulation.Persistence;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Persistence
{
    public class FileWorldStoreTests : WorldStoreContract
    {
        protected override IWorldStore CreateStore(string directory) => new FileWorldStore(Path.Combine(directory, "world.kws"));

        [Test]
        public void ADamagedSaveFallsBackToTheBackup()
        {
            string path = Path.Combine(Dir, "world.kws");
            var store = new FileWorldStore(path);
            store.Load();
            var batch = new SaveBatch { Full = true };
            batch.Meta["world_time"] = "10";
            store.Write(batch);
            batch.Meta["world_time"] = "20";
            store.Write(batch);
            Assert.That(File.Exists(path + ".bak"), Is.True);

            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            var reopened = new FileWorldStore(path);
            WorldSaveData data = reopened.Load();
            Assert.That(data.Meta["world_time"], Is.EqualTo("10"), "previous save");
            Assert.That(reopened.Warnings, Has.Some.Contains("Checksum"));

            File.WriteAllText(path + ".bak", "garbage");
            Assert.Throws<InvalidDataException>(() => new FileWorldStore(path).Load());
        }

        [Test]
        public void UnknownItemsInASaveAreDroppedWithAWarning()
        {
            var c = new ItemContainer(ContainerRole.Storage, 4);
            c.Set(1, ItemStack.Of(Db.Item("item.resource.wood"), 10));
            var w = new Kindling.Core.Serialization.PacketWriter();
            SaveCodec.WriteContainer(w, c);
            byte[] bytes = w.ToArray();
            bytes[3] = 0xFE; // net id low byte → an id that doesn't exist
            bytes[4] = 0xFF;
            var r = new Kindling.Core.Serialization.PacketReader(bytes);
            var warnings = new List<string>();
            var target = new ItemContainer(ContainerRole.Storage, 4);
            SaveCodec.ReadContainer(ref r, target, Db, warnings);
            Assert.That(target.IsEmpty, Is.True);
            Assert.That(warnings, Has.Count.EqualTo(1));
        }
    }
}
