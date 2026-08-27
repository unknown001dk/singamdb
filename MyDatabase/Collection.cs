namespace MyDatabase;

public class Collection
{
    public string Name { get; }

    private readonly List<Document> documents = new();

    public Collection(string name)
    {
        Name = name;
    }

    public void Insert(Document document)
    {
        documents.Add(document);
    }

    public List<Document> GetAll()
    {
        return documents;
    }

    public Document? GetById(string id)
    {
        return documents.FirstOrDefault(x => x.Id == id);
    }

    public bool Update(string id, Dictionary<string, object> updatedData)
    {
        var document = GetById(id);
        if (document == null)
            return false;

        document.Data = updatedData;
        return true;
    }

    public bool Delete(string id)
    {
        var document = GetById(id);

        if (document == null)
            return false;

        documents.Remove(document);
        return true;
    }
}
