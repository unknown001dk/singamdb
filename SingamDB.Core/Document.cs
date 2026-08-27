using System.Text.Json;
using System.Text.Json.Serialization;

namespace SingamDB.Core;

public class Document
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("_createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("_updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Document() { }

    public Document(Dictionary<string, object> data, string? id = null)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Id = id;
        }
        Data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in data)
        {
            Data[k] = NormalizeJsonValue(v) ?? "";
        }
    }

    public object? GetValue(string field)
    {
        if (field == "_id" || field == "id") return Id;
        if (field == "_createdAt") return CreatedAt;
        if (field == "_updatedAt") return UpdatedAt;

        if (Data.TryGetValue(field, out var val))
        {
            return NormalizeJsonValue(val);
        }
        return null;
    }

    public static object? NormalizeJsonValue(object? val)
    {
        if (val is JsonElement elem)
        {
            return elem.ValueKind switch
            {
                JsonValueKind.String => elem.GetString(),
                JsonValueKind.Number => elem.TryGetInt64(out var l) ? l : elem.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => elem.ToString()
            };
        }
        return val;
    }
}
