using System.Collections.Concurrent;
using System.Text;

namespace SingamDB.Core;

public class CompositeIndex
{
    public string[] FieldNames { get; }
    public string IndexName => string.Join("_", FieldNames);

    private readonly ConcurrentDictionary<string, HashSet<string>> indexMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim syncLock = new(LockRecursionPolicy.SupportsRecursion);

    public CompositeIndex(params string[] fieldNames)
    {
        FieldNames = fieldNames;
    }

    public string BuildKey(Document doc)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < FieldNames.Length; i++)
        {
            if (i > 0) sb.Append("::");
            var val = doc.GetValue(FieldNames[i])?.ToString() ?? "__null__";
            sb.Append(val);
        }
        return sb.ToString();
    }

    public string BuildKeyFromValues(params object[] values)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append("::");
            sb.Append(values[i]?.ToString() ?? "__null__");
        }
        return sb.ToString();
    }

    public void Insert(Document doc)
    {
        syncLock.EnterWriteLock();
        try
        {
            var key = BuildKey(doc);
            if (!indexMap.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                indexMap[key] = set;
            }
            set.Add(doc.Id);
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public void Remove(Document doc)
    {
        syncLock.EnterWriteLock();
        try
        {
            var key = BuildKey(doc);
            if (indexMap.TryGetValue(key, out var set))
            {
                set.Remove(doc.Id);
                if (set.Count == 0)
                {
                    indexMap.TryRemove(key, out _);
                }
            }
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public List<string> SearchExact(params object[] values)
    {
        syncLock.EnterReadLock();
        try
        {
            var key = BuildKeyFromValues(values);
            if (indexMap.TryGetValue(key, out var set))
            {
                return set.ToList();
            }
            return new List<string>();
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public List<string> SearchPrefix(string prefixValue)
    {
        syncLock.EnterReadLock();
        try
        {
            var results = new List<string>();
            var prefix = prefixValue + "::";
            foreach (var (k, set) in indexMap)
            {
                if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || k.Equals(prefixValue, StringComparison.OrdinalIgnoreCase))
                {
                    results.AddRange(set);
                }
            }
            return results;
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public void Clear()
    {
        syncLock.EnterWriteLock();
        try
        {
            indexMap.Clear();
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }
}
