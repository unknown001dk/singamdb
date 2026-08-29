using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SingamDB.Core;

public interface IPhysicalExecutor
{
    void Open();
    Document? Next();
    void Close();
}

public class SeqScanExecutor : IPhysicalExecutor
{
    private readonly IEnumerable<Document> source;
    private IEnumerator<Document>? enumerator;

    public SeqScanExecutor(IEnumerable<Document> source)
    {
        this.source = source;
    }

    public void Open() => enumerator = source.GetEnumerator();
    public Document? Next() => enumerator != null && enumerator.MoveNext() ? enumerator.Current : null;
    public void Close() => enumerator?.Dispose();
}

public class FilterExecutor : IPhysicalExecutor
{
    private readonly IPhysicalExecutor child;
    private readonly Func<Document, bool> predicate;

    public FilterExecutor(IPhysicalExecutor child, Func<Document, bool> predicate)
    {
        this.child = child;
        this.predicate = predicate;
    }

    public void Open() => child.Open();
    public Document? Next()
    {
        Document? doc;
        while ((doc = child.Next()) != null)
        {
            if (predicate(doc)) return doc;
        }
        return null;
    }
    public void Close() => child.Close();
}

public class SortExecutor : IPhysicalExecutor
{
    private readonly IPhysicalExecutor child;
    private readonly string sortField;
    private readonly bool ascending;
    private List<Document>? sortedResults;
    private int currentIndex = 0;

    public SortExecutor(IPhysicalExecutor child, string sortField, bool ascending = true)
    {
        this.child = child;
        this.sortField = sortField;
        this.ascending = ascending;
    }

    public void Open()
    {
        child.Open();
        var list = new List<Document>();
        Document? doc;
        while ((doc = child.Next()) != null)
        {
            list.Add(doc);
        }

        list.Sort((a, b) =>
        {
            var valA = a.GetValue(sortField) as IComparable ?? a.GetValue(sortField)?.ToString() ?? "";
            var valB = b.GetValue(sortField) as IComparable ?? b.GetValue(sortField)?.ToString() ?? "";
            int cmp = KeyComparer.Instance.Compare(valA, valB);
            return ascending ? cmp : -cmp;
        });

        sortedResults = list;
        currentIndex = 0;
    }

    public Document? Next()
    {
        if (sortedResults != null && currentIndex < sortedResults.Count)
        {
            return sortedResults[currentIndex++];
        }
        return null;
    }

    public void Close()
    {
        child.Close();
        sortedResults = null;
    }
}

public class ProjectExecutor : IPhysicalExecutor
{
    private readonly IPhysicalExecutor child;
    private readonly HashSet<string> projectedFields;

    public ProjectExecutor(IPhysicalExecutor child, IEnumerable<string> fields)
    {
        this.child = child;
        projectedFields = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
    }

    public void Open() => child.Open();

    public Document? Next()
    {
        var doc = child.Next();
        if (doc == null) return null;

        var projectedData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in projectedFields)
        {
            var val = doc.GetValue(f);
            if (val != null)
            {
                projectedData[f] = val;
            }
        }
        return new Document(projectedData, doc.Id)
        {
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    public void Close() => child.Close();
}

public class LimitSkipExecutor : IPhysicalExecutor
{
    private readonly IPhysicalExecutor child;
    private readonly int limit;
    private readonly int skip;
    private int skipped = 0;
    private int emitted = 0;

    public LimitSkipExecutor(IPhysicalExecutor child, int limit = 100, int skip = 0)
    {
        this.child = child;
        this.limit = limit;
        this.skip = skip;
    }

    public void Open()
    {
        child.Open();
        skipped = 0;
        emitted = 0;
        while (skipped < skip && child.Next() != null)
        {
            skipped++;
        }
    }

    public Document? Next()
    {
        if (emitted >= limit) return null;
        var doc = child.Next();
        if (doc != null)
        {
            emitted++;
            return doc;
        }
        return null;
    }

    public void Close() => child.Close();
}

public class AggregateRequest
{
    [JsonPropertyName("groupBy")]
    public string? GroupByField { get; set; }

    [JsonPropertyName("sum")]
    public string? SumField { get; set; }

    [JsonPropertyName("avg")]
    public string? AvgField { get; set; }

    [JsonPropertyName("min")]
    public string? MinField { get; set; }

    [JsonPropertyName("max")]
    public string? MaxField { get; set; }

    [JsonPropertyName("count")]
    public bool Count { get; set; } = true;
}

public class AggregateResult
{
    [JsonPropertyName("group")]
    public string GroupKey { get; set; } = "ALL";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("sum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Sum { get; set; }

    [JsonPropertyName("avg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Avg { get; set; }

    [JsonPropertyName("min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }
}

public static class AggregationPipeline
{
    public static List<AggregateResult> Execute(IEnumerable<Document> documents, AggregateRequest request)
    {
        var results = new List<AggregateResult>();

        if (string.IsNullOrWhiteSpace(request.GroupByField))
        {
            // Single global group aggregation
            var res = ComputeGroup("ALL", documents, request);
            results.Add(res);
        }
        else
        {
            // Group by field
            var groups = documents.GroupBy(d => d.GetValue(request.GroupByField)?.ToString() ?? "__null__");
            foreach (var g in groups)
            {
                var res = ComputeGroup(g.Key, g, request);
                results.Add(res);
            }
        }

        return results;
    }

    private static AggregateResult ComputeGroup(string groupKey, IEnumerable<Document> docs, AggregateRequest req)
    {
        var docList = docs.ToList();
        var res = new AggregateResult
        {
            GroupKey = groupKey,
            Count = docList.Count
        };

        if (!string.IsNullOrWhiteSpace(req.SumField) || !string.IsNullOrWhiteSpace(req.AvgField))
        {
            var targetField = req.SumField ?? req.AvgField;
            var numbers = docList
                .Select(d => ConvertToDouble(d.GetValue(targetField!)))
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (numbers.Count > 0)
            {
                double sum = numbers.Sum();
                if (!string.IsNullOrWhiteSpace(req.SumField)) res.Sum = sum;
                if (!string.IsNullOrWhiteSpace(req.AvgField)) res.Avg = Math.Round(sum / numbers.Count, 2);
            }
        }

        if (!string.IsNullOrWhiteSpace(req.MinField))
        {
            var nums = docList
                .Select(d => ConvertToDouble(d.GetValue(req.MinField)))
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();
            if (nums.Count > 0) res.Min = nums.Min();
        }

        if (!string.IsNullOrWhiteSpace(req.MaxField))
        {
            var nums = docList
                .Select(d => ConvertToDouble(d.GetValue(req.MaxField)))
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();
            if (nums.Count > 0) res.Max = nums.Max();
        }

        return res;
    }

    private static double? ConvertToDouble(object? val)
    {
        if (val == null) return null;
        if (val is double d) return d;
        if (val is long l) return l;
        if (val is int i) return i;
        if (double.TryParse(val.ToString(), out var parsed)) return parsed;
        return null;
    }
}
