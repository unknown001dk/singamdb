using System.Collections.Concurrent;

namespace SingamDB.Core;

public enum NodeRole
{
    Primary,
    Follower
}

public class ReplicationEngine : IDisposable
{
    public NodeRole Role { get; private set; }
    public long ReplicatedSequence { get; private set; }

    private readonly ConcurrentBag<Action<WalEntry>> followerListeners = new();
    private readonly object replicationLock = new();

    public ReplicationEngine(NodeRole role = NodeRole.Primary)
    {
        Role = role;
    }

    public void RegisterFollower(Action<WalEntry> onReceiveWalEntry)
    {
        followerListeners.Add(onReceiveWalEntry);
    }

    public void BroadcastWalEntry(WalEntry entry)
    {
        if (Role != NodeRole.Primary) return;

        lock (replicationLock)
        {
            ReplicatedSequence = entry.Sequence;
            foreach (var listener in followerListeners)
            {
                try
                {
                    listener(entry);
                }
                catch { }
            }
        }
    }

    public void ApplyFollowerEntry(Collection followerCollection, WalEntry entry)
    {
        followerCollection.ReplayWalEntry(entry);
        ReplicatedSequence = entry.Sequence;
    }

    public void Dispose()
    {
    }
}
