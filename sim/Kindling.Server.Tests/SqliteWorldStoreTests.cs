using System;
using System.Collections.Generic;
using System.IO;
using Kindling.Server;
using Kindling.Simulation.Persistence;
using Kindling.Tests.Pure.Persistence;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kindling.Server.Tests
{
    public class SqliteWorldStoreTests : WorldStoreContract
    {
        protected override IWorldStore CreateStore(string directory) => new SqliteWorldStore(Path.Combine(directory, "world.db"));

        [Test]
        public void UsesWalAndWritesInTheBackground()
        {
            using var store = new SqliteWorldStore(Path.Combine(Dir, "world.db"));
            using (var c = new SqliteConnection($"Data Source={store.Path};Pooling=False"))
            {
                c.Open();
                using SqliteCommand cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode;";
                Assert.That(cmd.ExecuteScalar(), Is.EqualTo("wal"));
            }

            var batch = new SaveBatch { Full = true };
            batch.Meta["world_time"] = "12.5";
            for (int i = 0; i < 2000; i++)
            {
                batch.Entities.Add(new EntityRecord { Id = (uint)(i + 1), TypeId = 1, Data = new byte[64] });
            }

            store.Write(batch);
            store.Flush();
            Assert.That(store.LastError, Is.Null);
            Assert.That(store.BatchesWritten, Is.EqualTo(1));
            WorldSaveData data = store.Load();
            Assert.That(data.Entities.Count, Is.EqualTo(2000));
            Assert.That(data.Meta["world_time"], Is.EqualTo("12.5"));
            TestContext.WriteLine($"2000 entities in one transaction: {store.LastBatchMs:0.0} ms");
        }

        [Test]
        public void BackupsAreLoadableAndRotated()
        {
            string backups = Path.Combine(Dir, "backups");
            using var store = new SqliteWorldStore(Path.Combine(Dir, "world.db"));
            var batch = new SaveBatch { Full = true };
            batch.Meta["world_time"] = "1";
            store.Write(batch);
            var start = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);
            string first = null;
            for (int i = 0; i < 5; i++)
            {
                string file = store.BackupWithRotation(backups, keep: 3, start.AddHours(i));
                first ??= file;
            }

            string[] kept = Directory.GetFiles(backups, "world-*.db");
            Assert.That(kept.Length, Is.EqualTo(3));
            Assert.That(File.Exists(first), Is.False, "oldest deleted");
            using var restored = new SqliteWorldStore(kept[0]);
            Assert.That(restored.Load().Meta["world_time"], Is.EqualTo("1"));
        }
    }
}
