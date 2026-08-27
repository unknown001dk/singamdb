using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SingamDB.Core;

public enum WalOpType
{
    Insert,
    Update,
    Delete,
    TxBegin,
    TxCommit,
    TxAbort
}

public class WalEntry
{
    [JsonPropertyName("seq")]
    public long Sequence { get; set; }

    [JsonPropertyName("op")]
    public WalOpType Op { get; set; }

    [JsonPropertyName("txId")]
    public long TxId { get; set; } = 0;

    [JsonPropertyName("id")]
    public string DocId { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Data { get; set; }

    [JsonPropertyName("ts")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("crc")]
    public uint Crc32 { get; set; }

    public uint ComputeChecksum()
    {
        var raw = $"{Sequence}:{Op}:{TxId}:{DocId}:{Timestamp}";
        return FastCrc32.Compute(Encoding.UTF8.GetBytes(raw));
    }
}

public class WalEngine : IDisposable
{
    private readonly string walFilePath;
    private readonly FileStream fileStream;
    private readonly StreamWriter streamWriter;
    private readonly object writeLock = new();
    private long currentSequence = 0;
    private readonly bool syncFsync;

    public WalEngine(string walFilePath, bool syncFsync = false)
    {
        this.walFilePath = walFilePath;
        this.syncFsync = syncFsync;

        var dir = Path.GetDirectoryName(walFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Open in Append mode with sequential access
        var fileOptions = syncFsync ? FileOptions.WriteThrough : FileOptions.SequentialScan;
        fileStream = new FileStream(
            walFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 65536,
            fileOptions);

        streamWriter = new StreamWriter(fileStream) { AutoFlush = syncFsync };
    }

    public void Append(WalOpType op, string docId, Dictionary<string, object>? data = null, long txId = 0)
    {
        lock (writeLock)
        {
            currentSequence++;
            var entry = new WalEntry
            {
                Sequence = currentSequence,
                Op = op,
                TxId = txId,
                DocId = docId,
                Data = data,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            entry.Crc32 = entry.ComputeChecksum();

            var jsonLine = JsonSerializer.Serialize(entry);
            streamWriter.WriteLine(jsonLine);

            if (syncFsync)
            {
                fileStream.Flush(flushToDisk: true);
            }
        }
    }

    public static WalReplayResult ReadAndValidate(string walFilePath)
    {
        var validRawEntries = new List<WalEntry>();
        int corruptedEntriesCount = 0;
        bool tornWriteEncountered = false;

        if (!File.Exists(walFilePath))
        {
            return new WalReplayResult();
        }

        using var fs = new FileStream(walFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<WalEntry>(line);
                if (entry != null)
                {
                    uint expectedCrc = entry.ComputeChecksum();
                    if (entry.Crc32 == expectedCrc)
                    {
                        validRawEntries.Add(entry);
                    }
                    else
                    {
                        corruptedEntriesCount++;
                        tornWriteEncountered = true;
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                tornWriteEncountered = true;
                corruptedEntriesCount++;
                break;
            }
        }

        // Transactional Replay Filter: Only replay operations of COMMITTED transactions or standalone writes (TxId == 0)
        var committedTxs = new HashSet<long>();
        var abortedTxs = new HashSet<long>();

        foreach (var entry in validRawEntries)
        {
            if (entry.Op == WalOpType.TxCommit)
            {
                committedTxs.Add(entry.TxId);
            }
            else if (entry.Op == WalOpType.TxAbort)
            {
                abortedTxs.Add(entry.TxId);
            }
        }

        var replayableEntries = new List<WalEntry>();
        foreach (var entry in validRawEntries)
        {
            if (entry.Op is WalOpType.TxBegin or WalOpType.TxCommit or WalOpType.TxAbort)
            {
                continue;
            }

            // Standalone write OR explicitly committed transaction
            if (entry.TxId == 0 || committedTxs.Contains(entry.TxId))
            {
                replayableEntries.Add(entry);
            }
        }

        return new WalReplayResult
        {
            Entries = replayableEntries,
            CorruptedEntriesCount = corruptedEntriesCount,
            TornWriteEncountered = tornWriteEncountered,
            CommittedTransactionsCount = committedTxs.Count,
            RolledBackTransactionsCount = validRawEntries.Select(e => e.TxId).Distinct().Count(id => id > 0 && !committedTxs.Contains(id))
        };
    }

    public void Truncate()
    {
        lock (writeLock)
        {
            fileStream.SetLength(0);
            currentSequence = 0;
        }
    }

    public void Dispose()
    {
        lock (writeLock)
        {
            streamWriter.Flush();
            fileStream.Flush(flushToDisk: true);
            streamWriter.Dispose();
            fileStream.Dispose();
        }
    }
}

public class WalReplayResult
{
    public List<WalEntry> Entries { get; set; } = new();
    public int CorruptedEntriesCount { get; set; }
    public bool TornWriteEncountered { get; set; }
    public int CommittedTransactionsCount { get; set; }
    public int RolledBackTransactionsCount { get; set; }
}

public static class FastCrc32
{
    private static readonly uint[] Table;

    static FastCrc32()
    {
        const uint poly = 0xedb88320u;
        Table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ poly;
                else
                    crc >>= 1;
            }
            Table[i] = crc;
        }
    }

    public static uint Compute(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte b in bytes)
        {
            byte index = (byte)((crc & 0xff) ^ b);
            crc = (crc >> 8) ^ Table[index];
        }
        return ~crc;
    }
}
