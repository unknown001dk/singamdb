namespace MyDatabase;

public class Database
{
    private readonly StorageEngine storage;

    private readonly Dictionary<string, Collection> collections
        = new();

    public Database(string path)
    {
        storage = new StorageEngine(path);
    }

    public Collection CreateCollection(string name)
    {
        if (collections.ContainsKey(name))
        {
            return collections[name];
        }

        var collection = new Collection(name);

        collections.Add(name, collection);

        return collection;
    }

    public Collection GetCollection(string name)
    {
        if (!collections.ContainsKey(name))
        {
            var collection = CreateCollection(name);

            var documents = storage.Load(name);

            foreach (var document in documents)
            {
                collection.Insert(document);
            }
        }

        return collections[name];
    }

    public void Save(string collectionName)
    {
        var collection = GetCollection(collectionName);

        storage.Save(
            collectionName,
            collection.GetAll());
    }
}
