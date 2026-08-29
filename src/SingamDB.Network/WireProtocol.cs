using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SingamDB.Core;

public enum WireMessageType : int
{
    Ping = 1,
    Pong = 2,
    Handshake = 3,
    Insert = 10,
    Find = 11,
    GetById = 12,
    Update = 13,
    Delete = 14,
    CreateIndex = 20,
    Aggregate = 30,
    Explain = 40,
    ResponseOk = 100,
    ResponseError = 101
}

public class WireFrame
{
    public const uint Magic = 0x534E474D; // 'SNGM' (SingamDB)
    public WireMessageType MessageType { get; set; }
    public uint RequestId { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Encode()
    {
        // Layout: [Magic 4B] [MsgType 4B] [ReqId 4B] [PayloadLen 4B] [Payload bytes] [CRC32 4B]
        int totalLen = 4 + 4 + 4 + 4 + Payload.Length + 4;
        var buffer = new byte[totalLen];

        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(4, 4), (int)MessageType);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), RequestId);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(12, 4), Payload.Length);

        if (Payload.Length > 0)
        {
            Payload.CopyTo(buffer.AsSpan(16, Payload.Length));
        }

        var frameToHash = new byte[totalLen - 4];
        Array.Copy(buffer, 0, frameToHash, 0, totalLen - 4);
        uint crc = FastCrc32.Compute(frameToHash);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(totalLen - 4, 4), crc);

        return buffer;
    }

    public static async Task<WireFrame?> ReadFromStreamAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[16];
        int read = await stream.ReadAtLeastAsync(header, 16, throwOnEndOfStream: false, ct);
        if (read < 16) return null;

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (magic != Magic) throw new InvalidDataException("Invalid SingamDB wire frame magic header.");

        int msgType = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));
        uint reqId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        int payloadLen = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12, 4));

        var payloadAndCrc = new byte[payloadLen + 4];
        read = await stream.ReadAtLeastAsync(payloadAndCrc, payloadLen + 4, throwOnEndOfStream: false, ct);
        if (read < payloadLen + 4) return null;

        var payload = new byte[payloadLen];
        Array.Copy(payloadAndCrc, 0, payload, 0, payloadLen);

        uint receivedCrc = BinaryPrimitives.ReadUInt32BigEndian(payloadAndCrc.AsSpan(payloadLen, 4));

        // Verify CRC
        var fullHeaderAndPayload = new byte[16 + payloadLen];
        Array.Copy(header, 0, fullHeaderAndPayload, 0, 16);
        Array.Copy(payload, 0, fullHeaderAndPayload, 16, payloadLen);
        uint computedCrc = FastCrc32.Compute(fullHeaderAndPayload);

        if (computedCrc != receivedCrc)
        {
            throw new InvalidDataException("CRC32 frame checksum verification failed.");
        }

        return new WireFrame
        {
            MessageType = (WireMessageType)msgType,
            RequestId = reqId,
            Payload = payload
        };
    }
}

public class WireProtocolServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly DatabaseEngine engine;
    private readonly CancellationTokenSource cts = new();
    private bool isRunning = false;

    public int Port { get; }

    public WireProtocolServer(DatabaseEngine engine, int port = 7778)
    {
        this.engine = engine;
        Port = port;
        listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        listener.Start();
        isRunning = true;
        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (!cts.Token.IsCancellationRequested && isRunning)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cts.Token);
                client.NoDelay = true; // Disable Nagle's algorithm for low latency
                _ = HandleClientAsync(client, cts.Token);
            }
            catch (Exception) when (cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                WireFrame? request;
                try
                {
                    request = await WireFrame.ReadFromStreamAsync(stream, ct);
                    if (request == null) break;
                }
                catch
                {
                    break;
                }

                var response = ProcessWireRequest(request);
                var encodedResponse = response.Encode();
                await stream.WriteAsync(encodedResponse, ct);
                await stream.FlushAsync(ct);
            }
        }
    }

    private WireFrame ProcessWireRequest(WireFrame request)
    {
        try
        {
            string jsonPayload = Encoding.UTF8.GetString(request.Payload);

            switch (request.MessageType)
            {
                case WireMessageType.Ping:
                    return new WireFrame
                    {
                        MessageType = WireMessageType.Pong,
                        RequestId = request.RequestId,
                        Payload = Encoding.UTF8.GetBytes("PONG")
                    };

                case WireMessageType.Insert:
                    {
                        var req = JsonSerializer.Deserialize<WireInsertRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetOrCreateCollection(req.Collection);
                        var doc = coll.Insert(req.Document, req.CustomId);
                        var respJson = JsonSerializer.Serialize(doc);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.Find:
                    {
                        var req = JsonSerializer.Deserialize<WireFindRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        var results = coll != null
                            ? coll.Query(req.Filter ?? new(), sortField: req.SortField, ascending: req.Ascending, projectFields: req.ProjectFields, limit: req.Limit, skip: req.Skip)
                            : new List<Document>();

                        var respJson = JsonSerializer.Serialize(results);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.GetById:
                    {
                        var req = JsonSerializer.Deserialize<WireGetByIdRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        var doc = coll?.GetById(req.DocId);
                        var respJson = JsonSerializer.Serialize(doc);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.Update:
                    {
                        var req = JsonSerializer.Deserialize<WireUpdateRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        var doc = coll?.Update(req.DocId, req.UpdateData, req.Merge);
                        var respJson = JsonSerializer.Serialize(doc);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.Delete:
                    {
                        var req = JsonSerializer.Deserialize<WireDeleteRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        bool deleted = coll?.Delete(req.DocId) ?? false;
                        var respJson = JsonSerializer.Serialize(new { deleted });
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.CreateIndex:
                    {
                        var req = JsonSerializer.Deserialize<WireIndexRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetOrCreateCollection(req.Collection);
                        if (req.IsComposite && req.Fields != null)
                        {
                            coll.CreateCompositeIndex(req.Fields);
                        }
                        else if (req.IsUnique)
                        {
                            coll.CreateUniqueIndex(req.Field ?? "", req.IsBTree);
                        }
                        else
                        {
                            coll.CreateIndex(req.Field ?? "", req.IsBTree);
                        }
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes("{\"ok\":1}") };
                    }

                case WireMessageType.Aggregate:
                    {
                        var req = JsonSerializer.Deserialize<WireAggregateRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        var results = coll != null ? coll.Aggregate(req.Request, req.Filter) : new List<AggregateResult>();
                        var respJson = JsonSerializer.Serialize(results);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                case WireMessageType.Explain:
                    {
                        var req = JsonSerializer.Deserialize<WireFindRequest>(jsonPayload)!;
                        var db = engine.GetOrCreateDatabase(req.Database);
                        var coll = db.GetCollection(req.Collection);
                        var plan = coll?.ExplainQuery(req.Filter ?? new(), sortField: req.SortField, projectFields: req.ProjectFields, limit: req.Limit, skip: req.Skip);
                        var respJson = JsonSerializer.Serialize(plan);
                        return new WireFrame { MessageType = WireMessageType.ResponseOk, RequestId = request.RequestId, Payload = Encoding.UTF8.GetBytes(respJson) };
                    }

                default:
                    return new WireFrame
                    {
                        MessageType = WireMessageType.ResponseError,
                        RequestId = request.RequestId,
                        Payload = Encoding.UTF8.GetBytes("{\"error\": \"Unsupported message type\"}")
                    };
            }
        }
        catch (Exception ex)
        {
            var errJson = JsonSerializer.Serialize(new { error = ex.Message });
            return new WireFrame
            {
                MessageType = WireMessageType.ResponseError,
                RequestId = request.RequestId,
                Payload = Encoding.UTF8.GetBytes(errJson)
            };
        }
    }

    public void Dispose()
    {
        isRunning = false;
        cts.Cancel();
        listener.Stop();
    }
}

public class WireInsertRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public Dictionary<string, object> Document { get; set; } = new();
    public string? CustomId { get; set; }
}

public class WireFindRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public Dictionary<string, object>? Filter { get; set; }
    public string? SortField { get; set; }
    public bool Ascending { get; set; } = true;
    public List<string>? ProjectFields { get; set; }
    public int Limit { get; set; } = 100;
    public int Skip { get; set; } = 0;
}

public class WireGetByIdRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public string DocId { get; set; } = string.Empty;
}

public class WireUpdateRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public string DocId { get; set; } = string.Empty;
    public Dictionary<string, object> UpdateData { get; set; } = new();
    public bool Merge { get; set; } = true;
}

public class WireDeleteRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public string DocId { get; set; } = string.Empty;
}

public class WireIndexRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public string? Field { get; set; }
    public bool IsBTree { get; set; }
    public bool IsUnique { get; set; }
    public bool IsComposite { get; set; }
    public string[]? Fields { get; set; }
}

public class WireAggregateRequest
{
    public string Database { get; set; } = "default";
    public string Collection { get; set; } = "users";
    public AggregateRequest Request { get; set; } = new();
    public Dictionary<string, object>? Filter { get; set; }
}
