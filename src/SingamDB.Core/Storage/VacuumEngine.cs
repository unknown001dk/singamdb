using System.Collections.Concurrent;
using System.Diagnostics;

namespace SingamDB.Core;

public class VacuumStats
{
    public string CollectionName { get; set; } = string.Empty;
    public int DeadVersionsPurged { get; set; }
    public int PagesCompacted { get; set; }
    public double DurationMs { get; set; }
}

public class VacuumEngine
{
    private readonly TransactionManager txManager;

    public VacuumEngine(TransactionManager txManager)
    {
        this.txManager = txManager;
    }

    public VacuumStats VacuumCollection(Collection collection)
    {
        var sw = Stopwatch.StartNew();
        long oldestActiveTx = txManager.GetOldestActiveTransactionReadTimestamp();
        int deadVersionsPurged = 0;

        // Walk collection version chains and prune versions older than oldest active transaction
        deadVersionsPurged = collection.PruneOldMvccVersions(oldestActiveTx);

        sw.Stop();

        return new VacuumStats
        {
            CollectionName = collection.Name,
            DeadVersionsPurged = deadVersionsPurged,
            DurationMs = Math.Round((double)sw.ElapsedTicks / Stopwatch.Frequency * 1000.0, 2)
        };
    }
}
