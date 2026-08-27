using System.Collections.Concurrent;

namespace SingamDB.Core;

public enum LockMode
{
    Shared,          // S lock for reading
    Exclusive,       // X lock for writing
    IntentShared,    // IS lock
    IntentExclusive  // IX lock
}

public class LockRequest
{
    public long TxId { get; }
    public LockMode Mode { get; }
    public TaskCompletionSource<bool> GrantPromise { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LockRequest(long txId, LockMode mode)
    {
        TxId = txId;
        Mode = mode;
    }
}

public class ResourceLock
{
    public string ResourceKey { get; }
    public HashSet<long> Holders { get; } = new();
    public LockMode CurrentMode { get; set; } = LockMode.Shared;
    public Queue<LockRequest> WaitQueue { get; } = new();

    public ResourceLock(string key)
    {
        ResourceKey = key;
    }
}

public class LockManager
{
    private readonly ConcurrentDictionary<string, ResourceLock> lockTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<long, HashSet<string>> txHeldLocks = new();
    private readonly ConcurrentDictionary<long, string> txWaitingOn = new(); // TxId -> ResourceKey
    private readonly object lockManagerLock = new();

    public async Task<bool> AcquireLockAsync(long txId, string resourceKey, LockMode mode, TimeSpan timeout)
    {
        LockRequest? request = null;

        lock (lockManagerLock)
        {
            var resLock = lockTable.GetOrAdd(resourceKey, k => new ResourceLock(k));

            // Check if lock can be granted immediately
            if (CanGrantImmediately(resLock, txId, mode))
            {
                GrantLock(resLock, txId, mode);
                return true;
            }

            // Check for deadlock cycle before waiting
            if (DetectDeadlock(txId, resLock))
            {
                throw new InvalidOperationException($"Deadlock detected! Transaction {txId} aborted to resolve cycle.");
            }

            request = new LockRequest(txId, mode);
            resLock.WaitQueue.Enqueue(request);
            txWaitingOn[txId] = resourceKey;
        }

        // Await grant or timeout
        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(request.GrantPromise.Task, timeoutTask);

        if (completed == timeoutTask)
        {
            lock (lockManagerLock)
            {
                txWaitingOn.TryRemove(txId, out _);
            }
            return false; // Timed out
        }

        return await request.GrantPromise.Task;
    }

    public void ReleaseAllLocks(long txId)
    {
        lock (lockManagerLock)
        {
            txWaitingOn.TryRemove(txId, out _);

            if (txHeldLocks.TryRemove(txId, out var heldResources))
            {
                foreach (var resKey in heldResources)
                {
                    if (lockTable.TryGetValue(resKey, out var resLock))
                    {
                        resLock.Holders.Remove(txId);

                        // If all holders released, grant to next waiting requests
                        if (resLock.Holders.Count == 0 && resLock.WaitQueue.Count > 0)
                        {
                            var nextReq = resLock.WaitQueue.Dequeue();
                            GrantLock(resLock, nextReq.TxId, nextReq.Mode);
                            txWaitingOn.TryRemove(nextReq.TxId, out _);
                            nextReq.GrantPromise.TrySetResult(true);
                        }
                    }
                }
            }
        }
    }

    private bool CanGrantImmediately(ResourceLock resLock, long txId, LockMode mode)
    {
        if (resLock.Holders.Count == 0) return true;
        if (resLock.Holders.Contains(txId) && resLock.Holders.Count == 1) return true;

        if (mode == LockMode.Shared && resLock.CurrentMode == LockMode.Shared && resLock.WaitQueue.Count == 0)
        {
            return true;
        }

        return false;
    }

    private void GrantLock(ResourceLock resLock, long txId, LockMode mode)
    {
        resLock.Holders.Add(txId);
        resLock.CurrentMode = mode;

        var held = txHeldLocks.GetOrAdd(txId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        held.Add(resLock.ResourceKey);
    }

    private bool DetectDeadlock(long waitingTxId, ResourceLock targetLock)
    {
        // Cycle detection in wait-for graph: Check if any holder of targetLock is waiting for waitingTxId
        var visited = new HashSet<long>();
        var queue = new Queue<long>(targetLock.Holders);

        while (queue.Count > 0)
        {
            var holderTx = queue.Dequeue();
            if (holderTx == waitingTxId) return true; // Cycle!

            if (visited.Add(holderTx) && txWaitingOn.TryGetValue(holderTx, out var waitingRes))
            {
                if (lockTable.TryGetValue(waitingRes, out var blockingLock))
                {
                    foreach (var nextHolder in blockingLock.Holders)
                    {
                        queue.Enqueue(nextHolder);
                    }
                }
            }
        }

        return false;
    }
}
