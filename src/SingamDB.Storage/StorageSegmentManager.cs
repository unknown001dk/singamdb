using SingamDB.Core;

namespace SingamDB.Storage;

/// <summary>
/// Manages physical storage segments, page allocations, and storage health checks for SingamDB.
/// </summary>
public class StorageSegmentManager
{
    private readonly StorageEngine _storageEngine;
    private readonly string _basePath;

    public StorageSegmentManager(StorageEngine storageEngine, string basePath = "singam_data")
    {
        _storageEngine = storageEngine;
        _basePath = basePath;
    }

    public StorageDiagnostics GetDiagnostics(string dbName, string collectionName)
    {
        var dataDir = _storageEngine.GetDataDir(dbName, collectionName);
        var pageFiles = Directory.Exists(dataDir) 
            ? Directory.GetFiles(dataDir, "*.page") 
            : Array.Empty<string>();

        long totalBytes = 0;
        foreach (var file in pageFiles)
        {
            var fi = new FileInfo(file);
            totalBytes += fi.Length;
        }

        var walDir = _storageEngine.GetWalDir(dbName, collectionName);
        var walFiles = Directory.Exists(walDir)
            ? Directory.GetFiles(walDir, "wal_*.log")
            : Array.Empty<string>();

        long totalWalBytes = 0;
        foreach (var file in walFiles)
        {
            var fi = new FileInfo(file);
            totalWalBytes += fi.Length;
        }

        return new StorageDiagnostics
        {
            DatabaseName = dbName,
            CollectionName = collectionName,
            PageCount = pageFiles.Length,
            TotalDataSizeBytes = totalBytes,
            WalSegmentCount = walFiles.Length,
            TotalWalSizeBytes = totalWalBytes,
            LastInspectedAt = DateTime.UtcNow
        };
    }
}

public class StorageDiagnostics
{
    public string DatabaseName { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public long TotalDataSizeBytes { get; set; }
    public int WalSegmentCount { get; set; }
    public long TotalWalSizeBytes { get; set; }
    public DateTime LastInspectedAt { get; set; }
}
