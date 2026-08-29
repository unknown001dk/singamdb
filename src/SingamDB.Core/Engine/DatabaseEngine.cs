using System.Collections.Concurrent;

namespace SingamDB.Core;

public class DatabaseEngine
{
    private readonly StorageEngine storage;
    private readonly ConcurrentDictionary<string, Database> databases = new(StringComparer.OrdinalIgnoreCase);

    public DatabaseEngine(string dataPath = "singam_data")
    {
        storage = new StorageEngine(dataPath);

        // Pre-load known databases
        var dbs = storage.ListDatabases();
        foreach (var dbName in dbs)
        {
            databases[dbName] = new Database(dbName, storage);
        }

        // Ensure default database exists
        if (!databases.ContainsKey("default"))
        {
            databases["default"] = new Database("default", storage);
        }
    }

    public Database GetOrCreateDatabase(string name = "default")
    {
        return databases.GetOrAdd(name, n => new Database(n, storage));
    }

    public Database? GetDatabase(string name)
    {
        return databases.TryGetValue(name, out var db) ? db : null;
    }

    public List<string> ListDatabases()
    {
        return databases.Keys.ToList();
    }

    public bool DropDatabase(string name)
    {
        databases.TryRemove(name, out _);
        return storage.DropDatabase(name);
    }

    public void FlushAll()
    {
        foreach (var db in databases.Values)
        {
            db.Flush();
        }
    }
}
