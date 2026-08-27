using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SingamDB.Core;

public class CollectionSchemaMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dbName")]
    public string DbName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("documentCount")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("dataSegmentPages")]
    public uint DataSegmentPages { get; set; }

    [JsonPropertyName("indexes")]
    public List<string> IndexedFields { get; set; } = new();

    [JsonPropertyName("bTreeIndexes")]
    public List<string> BTreeIndexedFields { get; set; } = new();

    [JsonPropertyName("compositeIndexes")]
    public List<string> CompositeIndexes { get; set; } = new();
}

public class StorageEngine
{
    private readonly string basePath;
    private readonly bool enableWal;
    private static readonly object fileLock = new();

    public StorageEngine(string basePath = "singam_data", bool enableWal = true)
    {
        this.basePath = basePath;
        this.enableWal = enableWal;
        if (!Directory.Exists(this.basePath))
        {
            Directory.CreateDirectory(this.basePath);
        }
    }

    public string GetCollectionBaseDir(string dbName, string collectionName)
    {
        return Path.Combine(basePath, dbName, collectionName);
    }

    public string GetDataDir(string dbName, string collectionName)
    {
        var dir = Path.Combine(GetCollectionBaseDir(dbName, collectionName), "data");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetIndexDir(string dbName, string collectionName)
    {
        var dir = Path.Combine(GetCollectionBaseDir(dbName, collectionName), "indexes");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetWalDir(string dbName, string collectionName)
    {
        var dir = Path.Combine(GetCollectionBaseDir(dbName, collectionName), "wal");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetMetadataDir(string dbName, string collectionName)
    {
        var dir = Path.Combine(GetCollectionBaseDir(dbName, collectionName), "metadata");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetPrimaryDataFile(string dbName, string collectionName)
    {
        return Path.Combine(GetDataDir(dbName, collectionName), "000001.bin");
    }

    public string GetWalFile(string dbName, string collectionName)
    {
        return Path.Combine(GetWalDir(dbName, collectionName), "000001.wal");
    }

    public string GetSchemaMetaFile(string dbName, string collectionName)
    {
        return Path.Combine(GetMetadataDir(dbName, collectionName), "schema.meta");
    }

    public void SaveCollection(string dbName, Collection collection)
    {
        lock (fileLock)
        {
            var dataFile = GetPrimaryDataFile(dbName, collection.Name);
            var metaFile = GetSchemaMetaFile(dbName, collection.Name);
            var tempBinFile = $"{dataFile}.tmp.{Guid.NewGuid():N}";

            var docs = collection.GetAll(limit: int.MaxValue);

            // 1. Write all documents into Binary 4KB Slotted Pages
            using (var pageMgr = new SlottedPageManager(tempBinFile))
            {
                var currentPage = pageMgr.AllocateNewPage();

                foreach (var doc in docs)
                {
                    var json = JsonSerializer.Serialize(doc);
                    var bytes = Encoding.UTF8.GetBytes(json);

                    if (!currentPage.TryInsertRecord(bytes, out _))
                    {
                        // Page full: allocate next 4KB page and link
                        var nextPage = pageMgr.AllocateNewPage();
                        currentPage.NextPageId = nextPage.PageId;
                        pageMgr.FlushPage(currentPage);
                        currentPage = nextPage;
                        currentPage.TryInsertRecord(bytes, out _);
                    }
                }

                pageMgr.FlushPage(currentPage);
                pageMgr.FlushAll();
            }

            // Atomic move to production data segment
            File.Move(tempBinFile, dataFile, overwrite: true);

            // 2. Write schema and index metadata
            var schema = new CollectionSchemaMeta
            {
                Name = collection.Name,
                DbName = dbName,
                DocumentCount = docs.Count,
                IndexedFields = collection.GetIndexes().ToList(),
                BTreeIndexedFields = collection.GetBTreeIndexes().ToList(),
                CompositeIndexes = collection.GetCompositeIndexes().ToList()
            };

            var metaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
            var tempMetaFile = $"{metaFile}.tmp.{Guid.NewGuid():N}";
            File.WriteAllText(tempMetaFile, metaJson);
            File.Move(tempMetaFile, metaFile, overwrite: true);
        }
    }

    public Collection LoadCollection(string dbName, string collectionName)
    {
        var dataFile = GetPrimaryDataFile(dbName, collectionName);
        var metaFile = GetSchemaMetaFile(dbName, collectionName);
        var walFile = GetWalFile(dbName, collectionName);

        WalEngine? wal = null;
        if (enableWal)
        {
            wal = new WalEngine(walFile);
        }

        var collection = new Collection(collectionName, wal);

        // 1. Load Indexes from Metadata
        if (File.Exists(metaFile))
        {
            try
            {
                var metaJson = File.ReadAllText(metaFile);
                var meta = JsonSerializer.Deserialize<CollectionSchemaMeta>(metaJson);
                if (meta != null)
                {
                    foreach (var f in meta.IndexedFields) collection.CreateIndex(f);
                    foreach (var bf in meta.BTreeIndexedFields) collection.CreateBTreeIndex(bf);
                    foreach (var cf in meta.CompositeIndexes)
                    {
                        var parts = cf.Split('_');
                        collection.CreateCompositeIndex(parts);
                    }
                }
            }
            catch { }
        }

        // 2. Load Documents from 4KB Binary Slotted Pages
        if (File.Exists(dataFile))
        {
            try
            {
                using var pageMgr = new SlottedPageManager(dataFile);
                uint totalPages = pageMgr.GetTotalPages();
                for (uint p = 0; p < totalPages; p++)
                {
                    var page = pageMgr.GetPage(p);
                    var records = page.GetAllRecords();
                    foreach (var recBytes in records)
                    {
                        var docJson = Encoding.UTF8.GetString(recBytes);
                        var doc = JsonSerializer.Deserialize<Document>(docJson);
                        if (doc != null)
                        {
                            collection.Insert(doc);
                        }
                    }
                }
            }
            catch { }
        }
        else
        {
            // Backward compatibility check for legacy .json snapshot
            var legacyJsonPath = Path.Combine(basePath, dbName, $"{collectionName}.json");
            if (File.Exists(legacyJsonPath))
            {
                try
                {
                    var legacyJson = File.ReadAllText(legacyJsonPath);
                    var docs = JsonSerializer.Deserialize<List<Document>>(legacyJson);
                    if (docs != null)
                    {
                        foreach (var d in docs) collection.Insert(d);
                    }
                }
                catch { }
            }
        }

        // 3. Replay WAL with CRC32 validation (Crash Recovery)
        if (File.Exists(walFile))
        {
            var replayResult = WalEngine.ReadAndValidate(walFile);
            foreach (var entry in replayResult.Entries)
            {
                collection.ReplayWalEntry(entry);
            }
        }

        return collection;
    }

    public List<string> ListDatabases()
    {
        if (!Directory.Exists(basePath)) return new List<string>();

        return Directory.GetDirectories(basePath)
            .Select(Path.GetFileName)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList()!;
    }

    public List<string> ListCollections(string dbName)
    {
        var dbDir = Path.Combine(basePath, dbName);
        if (!Directory.Exists(dbDir)) return new List<string>();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan for segmented directories: singam_data/{db}/{coll}/
        foreach (var dir in Directory.GetDirectories(dbDir))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && !name.StartsWith("."))
            {
                result.Add(name);
            }
        }

        // Backward compatibility: Scan for legacy .json or .wal files
        foreach (var file in Directory.GetFiles(dbDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".json" or ".wal")
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name) && !name.EndsWith(".meta"))
                {
                    result.Add(name);
                }
            }
        }

        return result.ToList();
    }

    public bool DropDatabase(string dbName)
    {
        lock (fileLock)
        {
            var path = Path.Combine(basePath, dbName);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            return false;
        }
    }

    public bool DropCollection(string dbName, string collectionName)
    {
        lock (fileLock)
        {
            bool removed = false;
            var collDir = GetCollectionBaseDir(dbName, collectionName);
            if (Directory.Exists(collDir))
            {
                Directory.Delete(collDir, recursive: true);
                removed = true;
            }

            // Also clean legacy files if any
            var legacyJson = Path.Combine(basePath, dbName, $"{collectionName}.json");
            var legacyWal = Path.Combine(basePath, dbName, $"{collectionName}.wal");
            if (File.Exists(legacyJson)) { File.Delete(legacyJson); removed = true; }
            if (File.Exists(legacyWal)) { File.Delete(legacyWal); removed = true; }

            return removed;
        }
    }
}
