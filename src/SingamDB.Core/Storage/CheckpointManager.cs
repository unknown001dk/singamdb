using System.Diagnostics;
using System.Text.Json;

namespace SingamDB.Core;

public class CheckpointManager
{
    private readonly string dataDirectory;
    private readonly object checkpointLock = new();

    public CheckpointManager(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
    }

    public CheckpointStats CheckpointCollection(string dbName, Collection coll, WalEngine? walEngine)
    {
        lock (checkpointLock)
        {
            var sw = Stopwatch.StartNew();

            // 1. Get snapshot of all committed documents
            var docs = coll.GetAll(limit: int.MaxValue);

            // 2. Persist snapshot to disk atomically
            var dbDir = Path.Combine(dataDirectory, dbName);
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);

            var snapPath = Path.Combine(dbDir, $"{coll.Name}.json");
            var tempPath = $"{snapPath}.ckpt.{Guid.NewGuid():N}";

            var json = JsonSerializer.Serialize(docs, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, snapPath, overwrite: true);

            // 3. Append checkpoint entry & truncate WAL
            walEngine?.Append(WalOpType.TxCheckpoint, $"ckpt_{coll.Name}");
            walEngine?.Truncate();

            sw.Stop();

            return new CheckpointStats
            {
                CollectionName = coll.Name,
                DocumentsFlushed = docs.Count,
                DurationMs = Math.Round((double)sw.ElapsedTicks / Stopwatch.Frequency * 1000.0, 2),
                WalTruncated = true
            };
        }
    }
}

public class CheckpointStats
{
    public string CollectionName { get; set; } = string.Empty;
    public int DocumentsFlushed { get; set; }
    public double DurationMs { get; set; }
    public bool WalTruncated { get; set; }
}
