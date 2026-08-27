using System.Text.Json;

namespace SingamDB.Core;

public class StorageEngine
{
    private readonly string basePath;
    private readonly bool enableWal;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public StorageEngine(string basePath = "singam_data", bool enableWal = true)
    {
        this.basePath = basePath;
        this.enableWal = enableWal;
        if (!Directory.Exists(this.basePath))
        {
            Directory.CreateDirectory(this.basePath);
        }
    }

    private string GetDatabaseDir(string dbName)
    {
        var path = Path.Combine(basePath, dbName);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    private string GetCollectionFile(string dbName, string collectionName)
    {
        return Path.Combine(GetDatabaseDir(dbName), $"{collectionName}.json");
    }

    private string GetMetaFile(string dbName, string collectionName)
    {
        return Path.Combine(GetDatabaseDir(dbName), $"{collectionName}.meta.json");
    }

    public string GetWalFile(string dbName, string collectionName)
    {
        return Path.Combine(GetDatabaseDir(dbName), $"{collectionName}.wal");
    }

    private static readonly object fileLock = new();

    public void SaveCollection(string dbName, Collection collection)
    {
        lock (fileLock)
        {
            var filePath = GetCollectionFile(dbName, collection.Name);
            var metaPath = GetMetaFile(dbName, collection.Name);
            var walPath = GetWalFile(dbName, collection.Name);
            var tempFile = $"{filePath}.tmp.{Guid.NewGuid():N}";

            var docs = collection.GetAll(limit: int.MaxValue);
            var json = JsonSerializer.Serialize(docs, JsonOptions);

            try
            {
                // Atomic snapshot write
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, filePath, overwrite: true);

                // Save index metadata
                var meta = new CollectionMetadata
                {
                    Indexes = collection.GetIndexes().ToList()
                };
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));

                // Checkpoint: truncate WAL since state is safely stored in snapshot
                if (File.Exists(walPath))
                {
                    try { File.WriteAllText(walPath, string.Empty); } catch { }
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }

    public Collection LoadCollection(string dbName, string collectionName)
    {
        var walPath = GetWalFile(dbName, collectionName);
        var walEngine = enableWal ? new WalEngine(walPath, syncFsync: false) : null;
        var collection = new Collection(collectionName, walEngine);

        var filePath = GetCollectionFile(dbName, collectionName);
        var metaPath = GetMetaFile(dbName, collectionName);

        // 1. Load Indexes
        if (File.Exists(metaPath))
        {
            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<CollectionMetadata>(metaJson);
                if (meta?.Indexes != null)
                {
                    foreach (var idx in meta.Indexes)
                    {
                        collection.CreateIndex(idx);
                    }
                }
            }
            catch { }
        }

        // 2. Load Snapshot
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var docs = JsonSerializer.Deserialize<List<Document>>(json);
                if (docs != null)
                {
                    foreach (var doc in docs)
                    {
                        collection.Insert(doc);
                    }
                }
            }
            catch { }
        }

        // 3. Replay WAL entries with CRC32 & Torn-Write Protection (Crash Recovery)
        if (File.Exists(walPath))
        {
            var replayResult = WalEngine.ReadAndValidate(walPath);
            foreach (var entry in replayResult.Entries)
            {
                collection.ReplayWalEntry(entry);
            }
        }

        return collection;
    }

    public List<string> ListDatabases()
    {
        return Directory.GetDirectories(basePath)
            .Select(Path.GetFileName)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList()!;
    }

    public List<string> ListCollections(string dbName)
    {
        var dbDir = Path.Combine(basePath, dbName);
        if (!Directory.Exists(dbDir)) return new List<string>();

        var jsonFiles = Directory.GetFiles(dbDir, "*.json")
            .Where(f => !f.EndsWith(".meta.json") && !f.EndsWith(".tmp"));
        var walFiles = Directory.GetFiles(dbDir, "*.wal")
            .Where(f => !f.EndsWith(".tmp"));

        return jsonFiles.Concat(walFiles)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool DropCollection(string dbName, string collectionName)
    {
        var file = GetCollectionFile(dbName, collectionName);
        var meta = GetMetaFile(dbName, collectionName);
        var wal = GetWalFile(dbName, collectionName);

        bool deleted = false;
        if (File.Exists(file))
        {
            File.Delete(file);
            deleted = true;
        }
        if (File.Exists(meta))
        {
            File.Delete(meta);
        }
        if (File.Exists(wal))
        {
            File.Delete(wal);
        }
        return deleted;
    }

    public bool DropDatabase(string dbName)
    {
        var dir = Path.Combine(basePath, dbName);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        return false;
    }
}

public class CollectionMetadata
{
    public List<string> Indexes { get; set; } = new();
}
