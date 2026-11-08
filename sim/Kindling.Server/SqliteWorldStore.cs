using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Kindling.Simulation.Persistence;
using Microsoft.Data.Sqlite;

namespace Kindling.Server
{
    /// <summary>
    /// World saves in SQLite (docs/blueprint/06 §4, schema adapted in ADR 0009): WAL mode,
    /// synchronous=NORMAL, one writer thread, every batch one transaction (a save is never half
    /// written), online backups with rotation. <see cref="Write"/> only queues; <see cref="Flush"/> waits.
    /// </summary>
    public sealed class SqliteWorldStore : IWorldStore
    {
        public const int SchemaVersion = 1;

        readonly string m_Path;
        readonly string m_ConnectionString;
        readonly BlockingCollection<SaveBatch> m_Queue = new BlockingCollection<SaveBatch>();
        readonly object m_Lock = new object();
        readonly Thread m_Writer;
        int m_Pending;
        bool m_Disposed;

        public SqliteWorldStore(string path)
        {
            m_Path = System.IO.Path.GetFullPath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(m_Path));
            m_ConnectionString = new SqliteConnectionStringBuilder { DataSource = m_Path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
            using (SqliteConnection c = Open())
            {
                Exec(c, "PRAGMA journal_mode=WAL;");
                Exec(c, @"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS characters (
  account_id TEXT PRIMARY KEY, display_name TEXT NOT NULL, life INTEGER NOT NULL,
  px REAL, py REAL, pz REAL, yaw REAL, sleeping INTEGER NOT NULL, last_seen REAL NOT NULL,
  data BLOB NOT NULL, updated_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS entities (
  id INTEGER PRIMARY KEY, type_id INTEGER NOT NULL, chunk_x INTEGER NOT NULL, chunk_z INTEGER NOT NULL,
  px REAL NOT NULL, py REAL NOT NULL, pz REAL NOT NULL, rot REAL NOT NULL, owner_id TEXT,
  health REAL NOT NULL, flags INTEGER NOT NULL DEFAULT 0, data BLOB, updated_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_entities_chunk ON entities(chunk_x, chunk_z);
CREATE TABLE IF NOT EXISTS resource_nodes (idx INTEGER PRIMARY KEY, alive INTEGER NOT NULL, remaining REAL NOT NULL, respawn_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS loot_spawners (idx INTEGER PRIMARY KEY, entity_id INTEGER NOT NULL, next_at REAL NOT NULL);
");
                Exec(c, $"INSERT OR IGNORE INTO meta(key, value) VALUES ('store_schema', '{SchemaVersion}');");
            }

            m_Writer = new Thread(WriterLoop) { IsBackground = true, Name = "world-save-writer" };
            m_Writer.Start();
        }

        public string Path => m_Path;

        public long BatchesWritten { get; private set; }

        public double LastBatchMs { get; private set; }

        public Exception LastError { get; private set; }

        /// <summary>Called on the writer thread when a batch fails (it is retried once, then dropped).</summary>
        public Action<Exception> OnError { get; set; }

        public WorldSaveData Load()
        {
            var data = new WorldSaveData();
            using SqliteConnection c = Open();
            using (SqliteDataReader r = Query(c, "SELECT key, value FROM meta WHERE key <> 'store_schema'"))
            {
                while (r.Read())
                {
                    data.Meta[r.GetString(0)] = r.GetString(1);
                }
            }

            if (data.Meta.Count == 0)
            {
                return null;
            }

            using (SqliteDataReader r = Query(c, "SELECT account_id, display_name, life, px, py, pz, yaw, sleeping, last_seen, data FROM characters"))
            {
                while (r.Read())
                {
                    var rec = new CharacterRecord
                    {
                        AccountId = r.GetString(0),
                        Name = r.GetString(1),
                        Life = (byte)r.GetInt32(2),
                        Position = new System.Numerics.Vector3((float)r.GetDouble(3), (float)r.GetDouble(4), (float)r.GetDouble(5)),
                        Yaw = (float)r.GetDouble(6),
                        Sleeping = r.GetInt32(7) != 0,
                        LastSeen = r.GetDouble(8),
                        Data = (byte[])r[9],
                    };
                    data.Characters[rec.AccountId] = rec;
                }
            }

            using (SqliteDataReader r = Query(c, "SELECT id, type_id, chunk_x, chunk_z, px, py, pz, rot, owner_id, health, flags, data FROM entities"))
            {
                while (r.Read())
                {
                    var rec = new EntityRecord
                    {
                        Id = (uint)r.GetInt64(0),
                        TypeId = (ushort)r.GetInt32(1),
                        ChunkX = r.GetInt32(2),
                        ChunkZ = r.GetInt32(3),
                        Position = new System.Numerics.Vector3((float)r.GetDouble(4), (float)r.GetDouble(5), (float)r.GetDouble(6)),
                        Yaw = (float)r.GetDouble(7),
                        OwnerId = r.IsDBNull(8) ? null : r.GetString(8),
                        Health = (float)r.GetDouble(9),
                        Flags = (byte)r.GetInt32(10),
                        Data = r.IsDBNull(11) ? null : (byte[])r[11],
                    };
                    data.Entities[rec.Id] = rec;
                }
            }

            using (SqliteDataReader r = Query(c, "SELECT idx, alive, remaining, respawn_at FROM resource_nodes"))
            {
                while (r.Read())
                {
                    var n = new NodeRecord { Index = r.GetInt32(0), Alive = r.GetInt32(1) != 0, Remaining = (float)r.GetDouble(2), RespawnAt = r.GetDouble(3) };
                    data.Nodes[n.Index] = n;
                }
            }

            using (SqliteDataReader r = Query(c, "SELECT idx, entity_id, next_at FROM loot_spawners"))
            {
                while (r.Read())
                {
                    var l = new LootSpawnRecord { Index = r.GetInt32(0), EntityId = (uint)r.GetInt64(1), NextAt = r.GetDouble(2) };
                    data.LootSpawns[l.Index] = l;
                }
            }

            return data;
        }

        public void Write(SaveBatch batch)
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteWorldStore));
            }

            Interlocked.Increment(ref m_Pending);
            m_Queue.Add(batch);
        }

        public void Flush()
        {
            lock (m_Lock)
            {
                while (Volatile.Read(ref m_Pending) > 0)
                {
                    Monitor.Wait(m_Lock, 100);
                }
            }
        }

        /// <summary>Online backup (SQLite backup API) to <paramref name="destination"/> after pending writes.</summary>
        public void Backup(string destination)
        {
            Flush();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination)));
            using SqliteConnection source = Open();
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            target.Open();
            source.BackupDatabase(target);
        }

        /// <summary>Writes a timestamped backup into <paramref name="directory"/> and deletes all but the newest <paramref name="keep"/>.</summary>
        public string BackupWithRotation(string directory, int keep, DateTime utcNow)
        {
            string file = System.IO.Path.Combine(directory, $"world-{utcNow:yyyyMMdd-HHmmss}.db");
            Backup(file);
            foreach (string old in Directory.GetFiles(directory, "world-*.db").OrderByDescending(f => f, StringComparer.Ordinal).Skip(Math.Max(1, keep)))
            {
                File.Delete(old);
            }

            return file;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            m_Queue.CompleteAdding();
            m_Writer.Join();
            m_Queue.Dispose();
        }

        void WriterLoop()
        {
            using SqliteConnection c = Open();
            Exec(c, "PRAGMA synchronous=NORMAL;");
            foreach (SaveBatch batch in m_Queue.GetConsumingEnumerable())
            {
                var sw = Stopwatch.StartNew();
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        WriteBatch(c, batch);
                        LastError = null;
                        break;
                    }
                    catch (SqliteException e)
                    {
                        LastError = e;
                        OnError?.Invoke(e);
                        Thread.Sleep(50);
                    }
                }

                LastBatchMs = sw.Elapsed.TotalMilliseconds;
                BatchesWritten++;
                lock (m_Lock)
                {
                    Interlocked.Decrement(ref m_Pending);
                    Monitor.PulseAll(m_Lock);
                }
            }
        }

        static void WriteBatch(SqliteConnection c, SaveBatch batch)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using SqliteTransaction tx = c.BeginTransaction();
            if (batch.Full)
            {
                foreach (string table in new[] { "characters", "entities", "resource_nodes", "loot_spawners" })
                {
                    Exec(c, $"DELETE FROM {table};", tx);
                }

                Exec(c, "DELETE FROM meta WHERE key <> 'store_schema';", tx);
            }

            using (SqliteCommand cmd = Command(c, tx, "INSERT INTO meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", "$k", "$v"))
            {
                foreach (var pair in batch.Meta)
                {
                    Run(cmd, pair.Key, pair.Value);
                }
            }

            using (SqliteCommand cmd = Command(c, tx,
                "INSERT OR REPLACE INTO characters(account_id, display_name, life, px, py, pz, yaw, sleeping, last_seen, data, updated_at) VALUES ($a, $n, $l, $x, $y, $z, $yaw, $s, $seen, $d, $u)",
                "$a", "$n", "$l", "$x", "$y", "$z", "$yaw", "$s", "$seen", "$d", "$u"))
            {
                foreach (CharacterRecord r in batch.Characters)
                {
                    Run(cmd, r.AccountId, r.Name, (int)r.Life, (double)r.Position.X, (double)r.Position.Y, (double)r.Position.Z, (double)r.Yaw, r.Sleeping ? 1 : 0, r.LastSeen, r.Data, now);
                }
            }

            using (SqliteCommand cmd = Command(c, tx, "DELETE FROM entities WHERE id = $id", "$id"))
            {
                foreach (uint id in batch.RemovedEntities)
                {
                    Run(cmd, (long)id);
                }
            }

            using (SqliteCommand cmd = Command(c, tx,
                "INSERT OR REPLACE INTO entities(id, type_id, chunk_x, chunk_z, px, py, pz, rot, owner_id, health, flags, data, updated_at) VALUES ($id, $t, $cx, $cz, $x, $y, $z, $r, $o, $h, $f, $d, $u)",
                "$id", "$t", "$cx", "$cz", "$x", "$y", "$z", "$r", "$o", "$h", "$f", "$d", "$u"))
            {
                foreach (EntityRecord r in batch.Entities)
                {
                    Run(cmd, (long)r.Id, (int)r.TypeId, r.ChunkX, r.ChunkZ, (double)r.Position.X, (double)r.Position.Y, (double)r.Position.Z, (double)r.Yaw, (object)r.OwnerId ?? DBNull.Value, (double)r.Health, (int)r.Flags, (object)r.Data ?? DBNull.Value, now);
                }
            }

            using (SqliteCommand upsert = Command(c, tx, "INSERT OR REPLACE INTO resource_nodes(idx, alive, remaining, respawn_at) VALUES ($i, $a, $r, $t)", "$i", "$a", "$r", "$t"))
            using (SqliteCommand delete = Command(c, tx, "DELETE FROM resource_nodes WHERE idx = $i", "$i"))
            {
                foreach (NodeRecord n in batch.Nodes)
                {
                    if (n.IsDefault)
                    {
                        Run(delete, n.Index);
                    }
                    else
                    {
                        Run(upsert, n.Index, n.Alive ? 1 : 0, (double)n.Remaining, n.RespawnAt);
                    }
                }
            }

            using (SqliteCommand cmd = Command(c, tx, "INSERT OR REPLACE INTO loot_spawners(idx, entity_id, next_at) VALUES ($i, $e, $t)", "$i", "$e", "$t"))
            {
                foreach (LootSpawnRecord l in batch.LootSpawns)
                {
                    Run(cmd, l.Index, (long)l.EntityId, l.NextAt);
                }
            }

            tx.Commit();
        }

        SqliteConnection Open()
        {
            var c = new SqliteConnection(m_ConnectionString);
            c.Open();
            return c;
        }

        static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params string[] parameters)
        {
            SqliteCommand cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (string p in parameters)
            {
                cmd.Parameters.Add(new SqliteParameter { ParameterName = p });
            }

            cmd.Prepare();
            return cmd;
        }

        static void Run(SqliteCommand cmd, params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                cmd.Parameters[i].Value = values[i];
            }

            cmd.ExecuteNonQuery();
        }

        static void Exec(SqliteConnection c, string sql, SqliteTransaction tx = null)
        {
            using SqliteCommand cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        static SqliteDataReader Query(SqliteConnection c, string sql)
        {
            SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteReader(System.Data.CommandBehavior.Default);
        }

        internal static string Invariant(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    }
}
