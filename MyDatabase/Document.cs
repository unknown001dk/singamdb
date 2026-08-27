namespace MyDatabase;

public class Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public Dictionary<string, object> Data { get; set; }
        = new Dictionary<string, object>();
}
