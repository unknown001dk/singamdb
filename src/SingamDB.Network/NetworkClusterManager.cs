using SingamDB.Core;

namespace SingamDB.Network;

/// <summary>
/// Manages cluster nodes, discovery, and network topology for SingamDB.
/// </summary>
public class NetworkClusterManager
{
    private readonly List<ClusterNode> _nodes = new();
    private readonly object _lock = new();

    public void RegisterNode(string nodeId, string host, int port, bool isLeader = false)
    {
        lock (_lock)
        {
            _nodes.RemoveAll(n => n.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
            _nodes.Add(new ClusterNode
            {
                NodeId = nodeId,
                Host = host,
                Port = port,
                IsLeader = isLeader,
                LastHeartbeat = DateTime.UtcNow
            });
        }
    }

    public List<ClusterNode> GetActiveNodes()
    {
        lock (_lock)
        {
            return new List<ClusterNode>(_nodes);
        }
    }
}

public class ClusterNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsLeader { get; set; }
    public DateTime LastHeartbeat { get; set; }
}
