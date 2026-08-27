using System.Collections.Concurrent;

namespace SingamDB.Core;

public class Database
{
    public string Name { get; }
    private readonly StorageEngine storage;
    private readonly ConcurrentDictionary<string, Collection> collections = new(StringComparer.OrdinalIgnoreCase);

    public Database(string name, StorageEngine storage)
    {
        Name = name;
        this.storage = storage;

        // Auto-load existing collections from disk
        var existingColls = storage.ListCollections(name);
        foreach (var collName in existingColls)
        {
            var coll = storage.LoadCollection(name, collName);
            collections[collName] = coll;
        }
    }

    public Collection GetOrCreateCollection(string name)
    {
        return collections.GetOrAdd(name, n => storage.LoadCollection(Name, n));
    }

    public Collection? GetCollection(string name)
    {
        if (collections.TryGetValue(name, out var coll))
        {
            return coll;
        }
        return null;
    }

    public bool DropCollection(string name)
    {
        collections.TryRemove(name, out _);
        return storage.DropCollection(Name, name);
    }

    public List<string> ListCollections()
    {
        return collections.Keys.ToList();
    }

    public void Flush()
    {
        foreach (var coll in collections.Values)
        {
            storage.SaveCollection(Name, coll);
        }
    }

    public void FlushCollection(string name)
    {
        if (collections.TryGetValue(name, out var coll))
        {
            storage.SaveCollection(Name, coll);
        }
    }
}
