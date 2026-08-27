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

    public void AddHashIndex(string fieldName)
    {
        secondaryHashIndexes.TryAdd(fieldName, new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
    }

    public void AddBTreeIndex(string fieldName)
    {
        bTreeIndexes.TryAdd(fieldName, new BTreeIndex(fieldName));
    }

    public bool RemoveIndexField(string fieldName)
    {
        bool r1 = secondaryHashIndexes.TryRemove(fieldName, out _);
        bool r2 = bTreeIndexes.TryRemove(fieldName, out _);
        return r1 || r2;
    }

    public bool HasIndex(string fieldName)
    {
        return secondaryHashIndexes.ContainsKey(fieldName) || bTreeIndexes.ContainsKey(fieldName);
    }

    public bool HasBTreeIndex(string fieldName)
    {
        return bTreeIndexes.ContainsKey(fieldName);
    }

    public IEnumerable<string> GetIndexedFields()
    {
        return secondaryHashIndexes.Keys.Concat(bTreeIndexes.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> GetBTreeIndexedFields()
    {
        return bTreeIndexes.Keys;
    }

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

    public void Clear()
    {
        primaryIndex.Clear();
        foreach (var index in secondaryHashIndexes.Values)
        {
            index.Clear();
        }
        bTreeIndexes.Clear();
    }
}
