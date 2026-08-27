using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SingamDB.Core;

public class Collection : IDisposable
{
    public string Name { get; }
    private readonly ReaderWriterLockSlim syncLock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly List<Document> documents = new();
    private readonly IndexManager indexManager = new();
    private readonly ConcurrentDictionary<string, MvccVersion> mvccChains = new(StringComparer.OrdinalIgnoreCase);
    private WalEngine? walEngine;

    public Collection(string name, WalEngine? walEngine = null)
    {
        Name = name;
        this.walEngine = walEngine;
    }

    public void SetWalEngine(WalEngine? wal)
    {
        this.walEngine = wal;
    }

    public void CreateIndex(string fieldName, bool isBTree = false)
    {
        syncLock.EnterWriteLock();
        try
        {
            if (isBTree)
            {
                indexManager.AddBTreeIndex(fieldName);
            }
            else
            {
                indexManager.AddHashIndex(fieldName);
            }

            foreach (var doc in documents)
            {
                indexManager.IndexDocument(doc);
            }
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public void CreateBTreeIndex(string fieldName) => CreateIndex(fieldName, isBTree: true);

    public void CreateCompositeIndex(params string[] fieldNames)
    {
        syncLock.EnterWriteLock();
        try
        {
            indexManager.AddCompositeIndex(fieldNames);
            foreach (var doc in documents)
            {
                indexManager.IndexDocument(doc);
            }
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public bool DropIndex(string fieldName)
    {
        syncLock.EnterWriteLock();
        try
        {
            return indexManager.RemoveIndexField(fieldName);
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public IEnumerable<string> GetIndexes() => indexManager.GetIndexedFields();
    public IEnumerable<string> GetBTreeIndexes() => indexManager.GetBTreeIndexedFields();
    public IEnumerable<string> GetCompositeIndexes() => indexManager.GetCompositeIndexNames();

    public Document Insert(Dictionary<string, object> data, string? customId = null, long txId = 0)
    {
        var doc = new Document(data, customId);
        Insert(doc, txId);
        return doc;
    }

    public void Insert(Document doc, long txId = 0)
    {
        // 1. Write-Ahead Log
        walEngine?.Append(WalOpType.Insert, doc.Id, doc.Data, txId);

        syncLock.EnterWriteLock();
        try
        {
            var existing = indexManager.GetById(doc.Id);
            if (existing != null)
            {
                documents.Remove(existing);
                indexManager.RemoveDocument(existing);
            }

            documents.Add(doc);
            indexManager.IndexDocument(doc);

            // MVCC Version Chain
            mvccChains.AddOrUpdate(doc.Id,
                _ => new MvccVersion(txId > 0 ? txId : 1, doc.Data),
                (_, prev) => new MvccVersion(txId > 0 ? txId : 1, doc.Data, prev));
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public void ReplayWalEntry(WalEntry entry)
    {
        syncLock.EnterWriteLock();
        try
        {
            switch (entry.Op)
            {
                case WalOpType.Insert:
                    var doc = new Document(entry.Data ?? new(), entry.DocId);
                    var existing = indexManager.GetById(doc.Id);
                    if (existing != null)
                    {
                        documents.Remove(existing);
                        indexManager.RemoveDocument(existing);
                    }
                    documents.Add(doc);
                    indexManager.IndexDocument(doc);
                    break;

                case WalOpType.Update:
                    var target = indexManager.GetById(entry.DocId);
                    if (target != null && entry.Data != null)
                    {
                        indexManager.RemoveDocument(target);
                        foreach (var (k, v) in entry.Data)
                        {
                            target.Data[k] = Document.NormalizeJsonValue(v) ?? "";
                        }
                        target.UpdatedAt = DateTime.UtcNow;
                        indexManager.IndexDocument(target);
                    }
                    break;

                case WalOpType.Delete:
                    var toDel = indexManager.GetById(entry.DocId);
                    if (toDel != null)
                    {
                        documents.Remove(toDel);
                        indexManager.RemoveDocument(toDel);
                    }
                    break;
            }
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public Document? GetById(string id)
    {
        syncLock.EnterReadLock();
        try
        {
            return indexManager.GetById(id);
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public Document? GetSnapshot(string id, long readTimestamp)
    {
        if (mvccChains.TryGetValue(id, out var head))
        {
            var curr = head;
            while (curr != null)
            {
                if (curr.CreatedTxId <= readTimestamp && (curr.DeletedTxId == 0 || curr.DeletedTxId > readTimestamp))
                {
                    return new Document(curr.Data, id);
                }
                curr = curr.PrevVersion;
            }
        }
        return GetById(id);
    }

    public List<Document> GetAll(int limit = 100, int skip = 0)
    {
        syncLock.EnterReadLock();
        try
        {
            return documents.Skip(skip).Take(limit).ToList();
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public List<Document> Find(string field, object value)
    {
        syncLock.EnterReadLock();
        try
        {
            string strVal = value?.ToString() ?? "__null__";

            if (indexManager.HasIndex(field))
            {
                return indexManager.FindByIndexedField(field, strVal).ToList();
            }

            return ScanInternal(field, strVal);
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public List<Document> Scan(string field, string strVal)
    {
        syncLock.EnterReadLock();
        try
        {
            return ScanInternal(field, strVal);
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    private List<Document> ScanInternal(string field, string strVal)
    {
        return documents.Where(d =>
        {
            var docVal = d.GetValue(field)?.ToString();
            return string.Equals(docVal, strVal, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    public List<Document> Query(Dictionary<string, object> filter, string? sortField = null, bool ascending = true, List<string>? projectFields = null, int limit = 100, int skip = 0)
    {
        syncLock.EnterReadLock();
        try
        {
            var candidateSeq = ResolveFilterCandidates(filter, out _);

            IPhysicalExecutor pipeline = new SeqScanExecutor(candidateSeq);

            if (!string.IsNullOrWhiteSpace(sortField))
            {
                pipeline = new SortExecutor(pipeline, sortField, ascending);
            }

            if (projectFields != null && projectFields.Count > 0)
            {
                pipeline = new ProjectExecutor(pipeline, projectFields);
            }

            pipeline = new LimitSkipExecutor(pipeline, limit, skip);

            var results = new List<Document>();
            pipeline.Open();
            try
            {
                Document? doc;
                while ((doc = pipeline.Next()) != null)
                {
                    results.Add(doc);
                }
            }
            finally
            {
                pipeline.Close();
            }

            return results;
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public List<AggregateResult> Aggregate(AggregateRequest request, Dictionary<string, object>? filter = null)
    {
        syncLock.EnterReadLock();
        try
        {
            IEnumerable<Document> source = filter != null && filter.Count > 0
                ? ResolveFilterCandidates(filter, out _)
                : documents;

            return AggregationPipeline.Execute(source, request);
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    private IEnumerable<Document> ResolveFilterCandidates(Dictionary<string, object> filter, out QueryExecutionPlan planInfo)
    {
        planInfo = new QueryExecutionPlan { Plan = "FULL_SCAN" };
        IEnumerable<Document> candidates = documents;

        // 1. Check for Composite Index Match
        var compIndex = indexManager.FindMatchingCompositeIndex(filter.Keys);
        if (compIndex != null)
        {
            candidates = indexManager.SearchComposite(compIndex, filter);
            planInfo.Plan = "COMPOSITE_INDEX_SCAN";
            planInfo.Index = compIndex.IndexName;
            return candidates;
        }

        // 2. Check for Range / B-Tree or Hash filters
        foreach (var (key, value) in filter)
        {
            var parsedRange = ParseRangeFilter(value);
            if (parsedRange != null && indexManager.HasBTreeIndex(key))
            {
                candidates = indexManager.SearchRange(key, parsedRange.Op, parsedRange.Val1, parsedRange.Val2);
                planInfo.Plan = "BTREE_RANGE_SCAN";
                planInfo.Index = key;
                break;
            }
            else if (parsedRange == null && indexManager.HasIndex(key))
            {
                string indexVal = value?.ToString() ?? "__null__";
                candidates = indexManager.FindByIndexedField(key, indexVal);
                planInfo.Plan = indexManager.HasBTreeIndex(key) ? "BTREE_INDEX_SCAN" : "HASH_INDEX_SCAN";
                planInfo.Index = key;
                break;
            }
        }

        // Apply remaining filters
        foreach (var (key, value) in filter)
        {
            if (key.Equals(planInfo.Index, StringComparison.OrdinalIgnoreCase)) continue;

            var range = ParseRangeFilter(value);
            if (range != null)
            {
                candidates = candidates.Where(d => EvaluateRange(d.GetValue(key), range));
                continue;
            }

            string expectedVal = value?.ToString() ?? "";
            candidates = candidates.Where(d =>
            {
                var actualVal = d.GetValue(key)?.ToString() ?? "";
                return string.Equals(actualVal, expectedVal, StringComparison.OrdinalIgnoreCase);
            });
        }

        return candidates;
    }

    private RangeCondition? ParseRangeFilter(object filterVal)
    {
        if (filterVal is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
        {
            if (elem.TryGetProperty("$gt", out var gt))
                return new RangeCondition(ComparisonOp.GreaterThan, GetComparable(gt));
            if (elem.TryGetProperty("$gte", out var gte))
                return new RangeCondition(ComparisonOp.GreaterThanOrEqual, GetComparable(gte));
            if (elem.TryGetProperty("$lt", out var lt))
                return new RangeCondition(ComparisonOp.LessThan, GetComparable(lt));
            if (elem.TryGetProperty("$lte", out var lte))
                return new RangeCondition(ComparisonOp.LessThanOrEqual, GetComparable(lte));
            if (elem.TryGetProperty("$between", out var bet) && bet.ValueKind == JsonValueKind.Array && bet.GetArrayLength() == 2)
                return new RangeCondition(ComparisonOp.Between, GetComparable(bet[0]), GetComparable(bet[1]));
            if (elem.TryGetProperty("$ne", out var ne))
                return new RangeCondition(ComparisonOp.NotEqual, GetComparable(ne));
        }
        else if (filterVal is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("$gt", out var gt) && gt is IComparable cgt)
                return new RangeCondition(ComparisonOp.GreaterThan, cgt);
            if (dict.TryGetValue("$gte", out var gte) && gte is IComparable cgte)
                return new RangeCondition(ComparisonOp.GreaterThanOrEqual, cgte);
            if (dict.TryGetValue("$lt", out var lt) && lt is IComparable clt)
                return new RangeCondition(ComparisonOp.LessThan, clt);
            if (dict.TryGetValue("$lte", out var lte) && lte is IComparable clte)
                return new RangeCondition(ComparisonOp.LessThanOrEqual, clte);
            if (dict.TryGetValue("$between", out var betObj))
            {
                if (betObj is IList<object> list && list.Count == 2 && list[0] is IComparable c1 && list[1] is IComparable c2)
                    return new RangeCondition(ComparisonOp.Between, c1, c2);
                if (betObj is Array arr && arr.Length == 2 && arr.GetValue(0) is IComparable a1 && arr.GetValue(1) is IComparable a2)
                    return new RangeCondition(ComparisonOp.Between, a1, a2);
            }
            if (dict.TryGetValue("$ne", out var ne) && ne is IComparable cne)
                return new RangeCondition(ComparisonOp.NotEqual, cne);
        }

        return null;
    }

    private static IComparable GetComparable(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out var l)) return l;
        if (elem.ValueKind == JsonValueKind.Number) return elem.GetDouble();
        return elem.GetString() ?? "";
    }

    private static bool EvaluateRange(object? docVal, RangeCondition range)
    {
        if (docVal == null) return false;
        if (docVal is not IComparable comp)
        {
            comp = docVal.ToString() ?? "";
        }

        return range.Op switch
        {
            ComparisonOp.GreaterThan => KeyComparer.Instance.Compare(comp, range.Val1) > 0,
            ComparisonOp.GreaterThanOrEqual => KeyComparer.Instance.Compare(comp, range.Val1) >= 0,
            ComparisonOp.LessThan => KeyComparer.Instance.Compare(comp, range.Val1) < 0,
            ComparisonOp.LessThanOrEqual => KeyComparer.Instance.Compare(comp, range.Val1) <= 0,
            ComparisonOp.Between when range.Val2 != null =>
                KeyComparer.Instance.Compare(comp, range.Val1) >= 0 &&
                KeyComparer.Instance.Compare(comp, range.Val2) <= 0,
            _ => false
        };
    }

    public ExplainResult ExplainQuery(Dictionary<string, object> filter, string? sortField = null, List<string>? projectFields = null, int limit = 100, int skip = 0)
    {
        syncLock.EnterReadLock();
        try
        {
            var sw = Stopwatch.StartNew();
            var candidates = ResolveFilterCandidates(filter, out var planInfo).ToList();
            sw.Stop();

            double executionTimeUs = (double)sw.ElapsedTicks / Stopwatch.Frequency * 1_000_000.0;
            double selectivity = documents.Count > 0 ? ((double)candidates.Count / documents.Count) * 100.0 : 0.0;
            double cost = planInfo.Plan == "FULL_SCAN"
                ? Math.Round(documents.Count * 1.0, 2)
                : Math.Round(1.0 + (candidates.Count * 0.05), 2);

            var pipelineSteps = new List<string> { planInfo.Plan };
            if (!string.IsNullOrWhiteSpace(sortField)) pipelineSteps.Add($"SORT({sortField})");
            if (projectFields != null && projectFields.Count > 0) pipelineSteps.Add($"PROJECT({string.Join(",", projectFields)})");
            pipelineSteps.Add($"LIMIT({limit})");

            return new ExplainResult
            {
                Operation = "FIND",
                Filter = filter,
                Plan = string.Join(" -> ", pipelineSteps),
                Index = planInfo.Index,
                EstimatedCost = cost,
                DocumentsExamined = planInfo.Plan == "FULL_SCAN" ? documents.Count : candidates.Count,
                DocumentsReturned = candidates.Skip(skip).Take(limit).Count(),
                Selectivity = $"{selectivity:F2}%",
                ExecutionTimeUs = Math.Round(executionTimeUs, 1)
            };
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public Document? Update(string id, Dictionary<string, object> updatedData, bool merge = true, long txId = 0)
    {
        syncLock.EnterWriteLock();
        try
        {
            var doc = indexManager.GetById(id);
            if (doc == null) return null;

            indexManager.RemoveDocument(doc);

            if (merge)
            {
                foreach (var (k, v) in updatedData)
                {
                    doc.Data[k] = Document.NormalizeJsonValue(v) ?? "";
                }
            }
            else
            {
                doc.Data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in updatedData)
                {
                    doc.Data[k] = Document.NormalizeJsonValue(v) ?? "";
                }
            }

            doc.UpdatedAt = DateTime.UtcNow;
            indexManager.IndexDocument(doc);

            mvccChains.AddOrUpdate(id,
                _ => new MvccVersion(txId > 0 ? txId : 1, doc.Data),
                (_, prev) => new MvccVersion(txId > 0 ? txId : 1, doc.Data, prev));

            walEngine?.Append(WalOpType.Update, id, doc.Data, txId);
            return doc;
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public bool Delete(string id, long txId = 0)
    {
        walEngine?.Append(WalOpType.Delete, id, txId: txId);

        syncLock.EnterWriteLock();
        try
        {
            var doc = indexManager.GetById(id);
            if (doc == null) return false;

            documents.Remove(doc);
            indexManager.RemoveDocument(doc);

            if (mvccChains.TryGetValue(id, out var curr))
            {
                curr.DeletedTxId = txId > 0 ? txId : 1;
            }

            return true;
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public int Count()
    {
        syncLock.EnterReadLock();
        try
        {
            return documents.Count;
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public CollectionStats GetStats()
    {
        syncLock.EnterReadLock();
        try
        {
            return new CollectionStats
            {
                Name = Name,
                DocumentCount = documents.Count,
                IndexedFields = indexManager.GetIndexedFields().ToList(),
                BTreeIndexedFields = indexManager.GetBTreeIndexedFields().ToList(),
                CompositeIndexes = indexManager.GetCompositeIndexNames().ToList()
            };
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        walEngine?.Dispose();
        syncLock.Dispose();
    }
}

public class QueryExecutionPlan
{
    public string Plan { get; set; } = "FULL_SCAN";
    public string? Index { get; set; }
}

public class RangeCondition
{
    public ComparisonOp Op { get; }
    public IComparable Val1 { get; }
    public IComparable? Val2 { get; }

    public RangeCondition(ComparisonOp op, IComparable val1, IComparable? val2 = null)
    {
        Op = op;
        Val1 = val1;
        Val2 = val2;
    }
}

public class CollectionStats
{
    public string Name { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public List<string> IndexedFields { get; set; } = new();
    public List<string> BTreeIndexedFields { get; set; } = new();
    public List<string> CompositeIndexes { get; set; } = new();
}

public class ExplainResult
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "FIND";

    [JsonPropertyName("filter")]
    public Dictionary<string, object> Filter { get; set; } = new();

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = "FULL_SCAN";

    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Index { get; set; }

    [JsonPropertyName("estimatedCost")]
    public double EstimatedCost { get; set; }

    [JsonPropertyName("documentsExamined")]
    public int DocumentsExamined { get; set; }

    [JsonPropertyName("documentsReturned")]
    public int DocumentsReturned { get; set; }

    [JsonPropertyName("selectivity")]
    public string Selectivity { get; set; } = "0.00%";

    [JsonPropertyName("executionTimeUs")]
    public double ExecutionTimeUs { get; set; }
}
