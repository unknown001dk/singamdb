using System.Collections.Concurrent;

namespace SingamDB.Core;

public class IndexManager
{
    // Primary index: Document ID -> Document (O(1) lookups)
    private readonly ConcurrentDictionary<string, Document> primaryIndex = new(StringComparer.OrdinalIgnoreCase);

    // Secondary Hash indexes: FieldName -> (FieldValue -> Set of Document IDs)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<string>>> secondaryHashIndexes = new(StringComparer.OrdinalIgnoreCase);

    // Secondary B-Tree Range indexes: FieldName -> BTreeIndex
    private readonly ConcurrentDictionary<string, BTreeIndex> bTreeIndexes = new(StringComparer.OrdinalIgnoreCase);

    // Composite Multi-Field Indexes: IndexName -> CompositeIndex
    private readonly ConcurrentDictionary<string, CompositeIndex> compositeIndexes = new(StringComparer.OrdinalIgnoreCase);

    // Unique Constraint Fields
    private readonly HashSet<string> uniqueFields = new(StringComparer.OrdinalIgnoreCase);

    public void AddHashIndex(string fieldName, bool isUnique = false)
    {
        secondaryHashIndexes.TryAdd(fieldName, new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        if (isUnique) uniqueFields.Add(fieldName);
    }

    public void AddBTreeIndex(string fieldName, bool isUnique = false)
    {
        bTreeIndexes.TryAdd(fieldName, new BTreeIndex(fieldName));
        if (isUnique) uniqueFields.Add(fieldName);
    }

    public void AddUniqueIndex(string fieldName, bool isBTree = false)
    {
        if (isBTree) AddBTreeIndex(fieldName, isUnique: true);
        else AddHashIndex(fieldName, isUnique: true);
    }

    public bool IsUnique(string fieldName) => uniqueFields.Contains(fieldName);

    public string? CheckUniqueConstraint(Document doc, string? excludeDocId = null)
    {
        foreach (var field in uniqueFields)
        {
            var val = doc.GetValue(field)?.ToString();
            if (val != null)
            {
                if (secondaryHashIndexes.TryGetValue(field, out var hashIdx) && hashIdx.TryGetValue(val, out var ids))
                {
                    lock (ids)
                    {
                        var violatingId = ids.FirstOrDefault(id => !string.Equals(id, excludeDocId, StringComparison.OrdinalIgnoreCase));
                        if (violatingId != null)
                        {
                            return $"Unique constraint violation: Duplicate value '{val}' already exists for field '{field}' (Document ID: {violatingId}).";
                        }
                    }
                }
            }
        }
        return null;
    }

    public void AddCompositeIndex(params string[] fieldNames)
    {
        var comp = new CompositeIndex(fieldNames);
        compositeIndexes.TryAdd(comp.IndexName, comp);
    }

    public bool RemoveIndexField(string fieldName)
    {
        uniqueFields.Remove(fieldName);
        bool r1 = secondaryHashIndexes.TryRemove(fieldName, out _);
        bool r2 = bTreeIndexes.TryRemove(fieldName, out _);
        bool r3 = compositeIndexes.TryRemove(fieldName, out _);
        return r1 || r2 || r3;
    }

    public bool HasIndex(string fieldName)
    {
        return secondaryHashIndexes.ContainsKey(fieldName) || bTreeIndexes.ContainsKey(fieldName);
    }

    public bool HasBTreeIndex(string fieldName)
    {
        return bTreeIndexes.ContainsKey(fieldName);
    }

    public bool HasCompositeIndex(params string[] fieldNames)
    {
        var name = string.Join("_", fieldNames);
        return compositeIndexes.ContainsKey(name);
    }

    public CompositeIndex? FindMatchingCompositeIndex(IEnumerable<string> fieldNames)
    {
        var set = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);
        foreach (var comp in compositeIndexes.Values)
        {
            if (comp.FieldNames.All(f => set.Contains(f)))
            {
                return comp;
            }
        }
        return null;
    }

    public IEnumerable<string> GetIndexedFields()
    {
        return secondaryHashIndexes.Keys
            .Concat(bTreeIndexes.Keys)
            .Concat(compositeIndexes.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> GetBTreeIndexedFields() => bTreeIndexes.Keys;
    public IEnumerable<string> GetCompositeIndexNames() => compositeIndexes.Keys;

    public void IndexDocument(Document doc)
    {
        primaryIndex[doc.Id] = doc;

        // 1. Update Hash Indexes
        foreach (var (fieldName, fieldIndex) in secondaryHashIndexes)
        {
            var val = doc.GetValue(fieldName)?.ToString() ?? "__null__";
            fieldIndex.AddOrUpdate(val,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { doc.Id },
                (_, set) =>
                {
                    lock (set) { set.Add(doc.Id); }
                    return set;
                });
        }

        // 2. Update B-Tree Range Indexes
        foreach (var (fieldName, btree) in bTreeIndexes)
        {
            var rawVal = doc.GetValue(fieldName);
            if (rawVal is IComparable comp)
            {
                btree.Insert(comp, doc.Id);
            }
            else if (rawVal != null)
            {
                btree.Insert(rawVal.ToString() ?? "", doc.Id);
            }
        }

        // 3. Update Composite Indexes
        foreach (var compIndex in compositeIndexes.Values)
        {
            compIndex.Insert(doc);
        }
    }

    public void RemoveDocument(Document doc)
    {
        primaryIndex.TryRemove(doc.Id, out _);

        foreach (var (fieldName, fieldIndex) in secondaryHashIndexes)
        {
            var val = doc.GetValue(fieldName)?.ToString() ?? "__null__";
            if (fieldIndex.TryGetValue(val, out var set))
            {
                lock (set)
                {
                    set.Remove(doc.Id);
                }
            }
        }

        foreach (var (fieldName, btree) in bTreeIndexes)
        {
            var rawVal = doc.GetValue(fieldName);
            if (rawVal is IComparable comp)
            {
                btree.Remove(comp, doc.Id);
            }
            else if (rawVal != null)
            {
                btree.Remove(rawVal.ToString() ?? "", doc.Id);
            }
        }

        foreach (var compIndex in compositeIndexes.Values)
        {
            compIndex.Remove(doc);
        }
    }

    public Document? GetById(string id)
    {
        return primaryIndex.TryGetValue(id, out var doc) ? doc : null;
    }

    public IEnumerable<Document> FindByIndexedField(string fieldName, string value)
    {
        if (secondaryHashIndexes.TryGetValue(fieldName, out var fieldIndex) &&
            fieldIndex.TryGetValue(value, out var idSet))
        {
            List<string> ids;
            lock (idSet)
            {
                ids = idSet.ToList();
            }

            foreach (var id in ids)
            {
                if (primaryIndex.TryGetValue(id, out var doc))
                {
                    yield return doc;
                }
            }
        }
        else if (bTreeIndexes.TryGetValue(fieldName, out var btree))
        {
            var ids = btree.Search(ComparisonOp.Equal, value);
            foreach (var id in ids)
            {
                if (primaryIndex.TryGetValue(id, out var doc))
                {
                    yield return doc;
                }
            }
        }
    }

    public IEnumerable<Document> SearchRange(string fieldName, ComparisonOp op, IComparable val1, IComparable? val2 = null)
    {
        if (bTreeIndexes.TryGetValue(fieldName, out var btree))
        {
            var ids = btree.Search(op, val1, val2);
            foreach (var id in ids)
            {
                if (primaryIndex.TryGetValue(id, out var doc))
                {
                    yield return doc;
                }
            }
        }
    }

    public IEnumerable<Document> SearchComposite(CompositeIndex comp, Dictionary<string, object> filter)
    {
        var values = comp.FieldNames.Select(f => filter[f]).ToArray();
        var ids = comp.SearchExact(values);
        foreach (var id in ids)
        {
            if (primaryIndex.TryGetValue(id, out var doc))
            {
                yield return doc;
            }
        }
    }

    public void Clear()
    {
        primaryIndex.Clear();
        foreach (var index in secondaryHashIndexes.Values)
        {
            index.Clear();
        }
        bTreeIndexes.Clear();
        foreach (var comp in compositeIndexes.Values)
        {
            comp.Clear();
        }
    }
}
