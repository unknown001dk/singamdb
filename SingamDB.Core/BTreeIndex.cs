using System.Collections.Concurrent;

namespace SingamDB.Core;

public enum ComparisonOp
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    In,
    NotEqual
}

public class KeyComparer : IComparer<IComparable>
{
    public static readonly KeyComparer Instance = new();

    public int Compare(IComparable? a, IComparable? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        if (IsNumeric(a) && IsNumeric(b))
        {
            double da = Convert.ToDouble(a);
            double db = Convert.ToDouble(b);
            return da.CompareTo(db);
        }

        try
        {
            return a.CompareTo(Convert.ChangeType(b, a.GetType()));
        }
        catch
        {
            return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsNumeric(object val)
    {
        return val is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}

public class BTreeIndex
{
    public string FieldName { get; }
    private readonly SortedDictionary<IComparable, HashSet<string>> tree = new(KeyComparer.Instance);
    private readonly ReaderWriterLockSlim syncLock = new(LockRecursionPolicy.SupportsRecursion);

    public BTreeIndex(string fieldName)
    {
        FieldName = fieldName;
    }

    public void Insert(IComparable key, string docId)
    {
        syncLock.EnterWriteLock();
        try
        {
            if (!tree.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                tree[key] = set;
            }
            set.Add(docId);
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public void Remove(IComparable key, string docId)
    {
        syncLock.EnterWriteLock();
        try
        {
            if (tree.TryGetValue(key, out var set))
            {
                set.Remove(docId);
                if (set.Count == 0)
                {
                    tree.Remove(key);
                }
            }
        }
        finally
        {
            syncLock.ExitWriteLock();
        }
    }

    public List<string> Search(ComparisonOp op, IComparable val1, IComparable? val2 = null)
    {
        syncLock.EnterReadLock();
        try
        {
            var results = new List<string>();

            switch (op)
            {
                case ComparisonOp.Equal:
                    if (tree.TryGetValue(val1, out var exactSet))
                    {
                        results.AddRange(exactSet);
                    }
                    break;

                case ComparisonOp.GreaterThan:
                    foreach (var (k, set) in tree)
                    {
                        if (KeyComparer.Instance.Compare(k, val1) > 0)
                        {
                            results.AddRange(set);
                        }
                    }
                    break;

                case ComparisonOp.GreaterThanOrEqual:
                    foreach (var (k, set) in tree)
                    {
                        if (KeyComparer.Instance.Compare(k, val1) >= 0)
                        {
                            results.AddRange(set);
                        }
                    }
                    break;

                case ComparisonOp.LessThan:
                    foreach (var (k, set) in tree)
                    {
                        if (KeyComparer.Instance.Compare(k, val1) < 0)
                        {
                            results.AddRange(set);
                        }
                    }
                    break;

                case ComparisonOp.LessThanOrEqual:
                    foreach (var (k, set) in tree)
                    {
                        if (KeyComparer.Instance.Compare(k, val1) <= 0)
                        {
                            results.AddRange(set);
                        }
                    }
                    break;

                case ComparisonOp.Between:
                    if (val2 != null)
                    {
                        foreach (var (k, set) in tree)
                        {
                            if (KeyComparer.Instance.Compare(k, val1) >= 0 && KeyComparer.Instance.Compare(k, val2) <= 0)
                            {
                                results.AddRange(set);
                            }
                        }
                    }
                    break;

                case ComparisonOp.NotEqual:
                    foreach (var (k, set) in tree)
                    {
                        if (KeyComparer.Instance.Compare(k, val1) != 0)
                        {
                            results.AddRange(set);
                        }
                    }
                    break;
            }

            return results;
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }

    public int KeyCount()
    {
        syncLock.EnterReadLock();
        try
        {
            return tree.Count;
        }
        finally
        {
            syncLock.ExitReadLock();
        }
    }
}
