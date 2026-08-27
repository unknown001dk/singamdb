using System.Collections.Concurrent;

namespace SingamDB.Core;

public enum TransactionStatus
{
    Active,
    Committed,
    Aborted
}

public class MvccVersion
{
    public long CreatedTxId { get; set; }
    public long DeletedTxId { get; set; } = 0;
    public Dictionary<string, object> Data { get; set; }
    public MvccVersion? PrevVersion { get; set; }

    public MvccVersion(long txId, Dictionary<string, object> data, MvccVersion? prev = null)
    {
        CreatedTxId = txId;
        Data = new Dictionary<string, object>(data, StringComparer.OrdinalIgnoreCase);
        PrevVersion = prev;
    }
}

public class StagedOperation
{
    public string CollectionName { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public WalOpType OpType { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

public class Transaction
{
    public long TxId { get; }
    public long ReadTimestamp { get; }
    public TransactionStatus Status { get; internal set; } = TransactionStatus.Active;
    public List<StagedOperation> StagedOps { get; } = new();
    public HashSet<string> ModifiedDocKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Transaction(long txId, long readTimestamp)
    {
        TxId = txId;
        ReadTimestamp = readTimestamp;
    }
}

public class TransactionManager
{
    private long globalTxCounter = 100;
    private readonly ConcurrentDictionary<long, Transaction> activeTransactions = new();
    private readonly ConcurrentDictionary<string, long> lastCommittedTxPerDoc = new(StringComparer.OrdinalIgnoreCase);
    private readonly object commitLock = new();

    public long GetOldestActiveTransactionReadTimestamp()
    {
        if (activeTransactions.IsEmpty)
        {
            return Interlocked.Read(ref globalTxCounter);
        }
        return activeTransactions.Values.Min(t => t.ReadTimestamp);
    }

    public Transaction BeginTransaction()
    {
        long txId = Interlocked.Increment(ref globalTxCounter);
        long readTs = Interlocked.Read(ref globalTxCounter);
        var tx = new Transaction(txId, readTs);
        activeTransactions[txId] = tx;
        return tx;
    }

    public void StageInsert(Transaction tx, string collectionName, string docId, Dictionary<string, object> data)
    {
        ValidateActive(tx);
        var key = $"{collectionName}::{docId}";
        tx.ModifiedDocKeys.Add(key);
        tx.StagedOps.Add(new StagedOperation
        {
            CollectionName = collectionName,
            DocId = docId,
            OpType = WalOpType.Insert,
            Data = data
        });
    }

    public void StageUpdate(Transaction tx, string collectionName, string docId, Dictionary<string, object> data)
    {
        ValidateActive(tx);
        var key = $"{collectionName}::{docId}";
        tx.ModifiedDocKeys.Add(key);
        tx.StagedOps.Add(new StagedOperation
        {
            CollectionName = collectionName,
            DocId = docId,
            OpType = WalOpType.Update,
            Data = data
        });
    }

    public void StageDelete(Transaction tx, string collectionName, string docId)
    {
        ValidateActive(tx);
        var key = $"{collectionName}::{docId}";
        tx.ModifiedDocKeys.Add(key);
        tx.StagedOps.Add(new StagedOperation
        {
            CollectionName = collectionName,
            DocId = docId,
            OpType = WalOpType.Delete
        });
    }

    public bool Commit(Transaction tx, DatabaseEngine engine, out string? conflictError)
    {
        conflictError = null;

        lock (commitLock)
        {
            ValidateActive(tx);

            // 1. First-Committer-Wins Conflict Detection (Snapshot Isolation validation)
            foreach (var docKey in tx.ModifiedDocKeys)
            {
                if (lastCommittedTxPerDoc.TryGetValue(docKey, out long lastCommittedTx))
                {
                    if (lastCommittedTx > tx.ReadTimestamp)
                    {
                        // Serialization Failure: concurrent transaction already committed changes to this document!
                        Rollback(tx);
                        conflictError = $"Write conflict on '{docKey}'. Document was modified by concurrent transaction (TxId={lastCommittedTx}) after this transaction started (ReadTs={tx.ReadTimestamp}). First-Committer-Wins rule applied.";
                        return false;
                    }
                }
            }

            // 2. Apply all staged operations atomically to target collections and WAL
            foreach (var op in tx.StagedOps)
            {
                var db = engine.GetOrCreateDatabase("default");
                var coll = db.GetOrCreateCollection(op.CollectionName);

                switch (op.OpType)
                {
                    case WalOpType.Insert:
                        coll.Insert(new Document(op.Data ?? new(), op.DocId), tx.TxId);
                        break;
                    case WalOpType.Update:
                        coll.Update(op.DocId, op.Data ?? new(), merge: true, txId: tx.TxId);
                        break;
                    case WalOpType.Delete:
                        coll.Delete(op.DocId, tx.TxId);
                        break;
                }

                var key = $"{op.CollectionName}::{op.DocId}";
                lastCommittedTxPerDoc[key] = tx.TxId;
            }

            tx.Status = TransactionStatus.Committed;
            activeTransactions.TryRemove(tx.TxId, out _);
            return true;
        }
    }

    public void Rollback(Transaction tx)
    {
        lock (commitLock)
        {
            tx.Status = TransactionStatus.Aborted;
            tx.StagedOps.Clear();
            tx.ModifiedDocKeys.Clear();
            activeTransactions.TryRemove(tx.TxId, out _);
        }
    }

    private static void ValidateActive(Transaction tx)
    {
        if (tx.Status != TransactionStatus.Active)
        {
            throw new InvalidOperationException($"Transaction {tx.TxId} is not active (Status: {tx.Status}).");
        }
    }
}
