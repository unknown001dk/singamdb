using System.Text.Json;

namespace MyDatabase;

public class StorageEngine
{
    private readonly string databasePath;

    public StorageEngine(string databasePath)
    {
        this.databasePath = databasePath;

        if (!Directory.Exists(databasePath))
        {
            Directory.CreateDirectory(databasePath);
        }
    }

    public void Save(
        string collectionName,
        List<Document> documents)
    {
        string filePath =
            Path.Combine(databasePath, $"{collectionName}.json");

        string json = JsonSerializer.Serialize(
            documents,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(filePath, json);
    }

    public List<Document> Load(string collectionName)
    {
        string filePath =
            Path.Combine(databasePath, $"{collectionName}.json");

        if (!File.Exists(filePath))
        {
            return new List<Document>();
        }

        string json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<List<Document>>(json)
               ?? new List<Document>();
    }
}
