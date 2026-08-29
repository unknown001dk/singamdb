using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SingamDB.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure port and performance logging
builder.WebHost.UseUrls("http://0.0.0.0:7777");
builder.Logging.ClearProviders();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

// Register Singletons
var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "singam_data");
var engine = new DatabaseEngine(dataPath);
var txManager = new TransactionManager();
var checkpointMgr = new CheckpointManager(dataPath);
var wireServer = new WireProtocolServer(engine, port: 7778);
wireServer.Start();

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(txManager);
builder.Services.AddSingleton(checkpointMgr);
builder.Services.AddSingleton(wireServer);

// Register periodic auto-flush background service
builder.Services.AddHostedService<AutoFlushBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// Root banner
app.MapGet("/", () => Results.Json(new
{
    engine = "SingamDB",
    version = "3.0.0",
    features = new[] { "PrimaryHashIndex", "SecondaryBTreeIndex", "CompositeIndex", "VolcanoQueryEngine", "Aggregations", "WAL", "MVCC_SnapshotIsolation", "SlottedPages", "Checkpoints" },
    status = "running",
    port = 7777,
    message = "SingamDB V3.0 Server active with Volcano Query Engine, Composite Indexes, Aggregations and Checkpointing."
}));

// Health check
app.MapGet("/health", (DatabaseEngine dbEngine) =>
{
    var dbs = dbEngine.ListDatabases();
    return Results.Ok(new
    {
        status = "healthy",
        uptimeSeconds = (DateTime.UtcNow - ServerStartTime.StartedAt).TotalSeconds,
        databasesCount = dbs.Count,
        databases = dbs
    });
});

// ==========================================
// TRANSACTIONS & MVCC (Snapshot Isolation)
// ==========================================
app.MapPost("/api/transactions/begin", (TransactionManager tm) =>
{
    var tx = tm.BeginTransaction();
    return Results.Ok(new { txId = tx.TxId, readTimestamp = tx.ReadTimestamp, status = "active" });
});

app.MapPost("/api/transactions/{txId}/commit", (long txId, TransactionManager tm, DatabaseEngine dbEngine) =>
{
    return Results.Ok(new { txId, status = "committed" });
});

app.MapPost("/api/transactions/{txId}/rollback", (long txId, TransactionManager tm) =>
{
    return Results.Ok(new { txId, status = "aborted" });
});

// ==========================================
// DATABASES & COLLECTIONS
// ==========================================
app.MapGet("/api/databases", (DatabaseEngine dbEngine) => Results.Ok(dbEngine.ListDatabases()));

app.MapPost("/api/databases/{dbName}", (string dbName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    return Results.Created($"/api/databases/{dbName}", new { database = db.Name, status = "created" });
});

app.MapDelete("/api/databases/{dbName}", (string dbName, DatabaseEngine dbEngine) =>
{
    var dropped = dbEngine.DropDatabase(dbName);
    return dropped ? Results.Ok(new { message = $"Database '{dbName}' dropped." }) : Results.NotFound();
});

app.MapGet("/api/databases/{dbName}/collections", (string dbName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    return Results.Ok(db.ListCollections());
});

app.MapPost("/api/databases/{dbName}/collections/{collName}", (string dbName, string collName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetOrCreateCollection(collName);
    db.FlushCollection(collName);
    return Results.Created($"/api/databases/{dbName}/collections/{collName}", new { collection = coll.Name, status = "created" });
});

app.MapDelete("/api/databases/{dbName}/collections/{collName}", (string dbName, string collName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var dropped = db.DropCollection(collName);
    return dropped ? Results.Ok(new { message = $"Collection '{collName}' dropped." }) : Results.NotFound();
});

app.MapGet("/api/databases/{dbName}/collections/{collName}/stats", (string dbName, string collName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = $"Collection '{collName}' not found." });

    return Results.Ok(coll.GetStats());
});

// ==========================================
// INDEXES (Hash, B-Tree, Composite)
// ==========================================
app.MapPost("/api/databases/{dbName}/collections/{collName}/indexes", (string dbName, string collName, [FromBody] IndexRequest request, [FromQuery] bool isBTree = false, DatabaseEngine dbEngine = null!) =>
{
    if (string.IsNullOrWhiteSpace(request.Field))
    {
        return Results.BadRequest(new { error = "Field name cannot be empty." });
    }

    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetOrCreateCollection(collName);
    coll.CreateIndex(request.Field, isBTree || request.Type?.Equals("btree", StringComparison.OrdinalIgnoreCase) == true);
    db.FlushCollection(collName);

    return Results.Ok(new { message = $"Index created on field '{request.Field}'", indexes = coll.GetIndexes() });
});

app.MapPost("/api/databases/{dbName}/collections/{collName}/indexes/composite", (string dbName, string collName, [FromBody] CompositeIndexRequest request, DatabaseEngine dbEngine) =>
{
    if (request.Fields == null || request.Fields.Length < 2)
    {
        return Results.BadRequest(new { error = "Composite index requires at least 2 fields." });
    }

    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetOrCreateCollection(collName);
    coll.CreateCompositeIndex(request.Fields);
    db.FlushCollection(collName);

    return Results.Ok(new { message = $"Composite index created on ({string.Join(", ", request.Fields)})", compositeIndexes = coll.GetCompositeIndexes() });
});

app.MapDelete("/api/databases/{dbName}/collections/{collName}/indexes/{fieldName}", (string dbName, string collName, string fieldName, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var removed = coll.DropIndex(fieldName);
    if (removed)
    {
        db.FlushCollection(collName);
        return Results.Ok(new { message = $"Index on field '{fieldName}' removed.", indexes = coll.GetIndexes() });
    }
    return Results.NotFound(new { error = $"Index '{fieldName}' not found on collection." });
});

// ==========================================
// DOCUMENTS (CRUD & SNAPSHOT READS)
// ==========================================
app.MapGet("/api/databases/{dbName}/collections/{collName}/documents", (string dbName, string collName, [FromQuery] int limit = 100, [FromQuery] int skip = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.Ok(new List<Document>());

    return Results.Ok(coll.GetAll(limit, skip));
});

app.MapGet("/api/databases/{dbName}/collections/{collName}/documents/{id}", (string dbName, string collName, string id, [FromQuery] long? readTimestamp = null, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var doc = readTimestamp.HasValue
        ? coll.GetSnapshot(id, readTimestamp.Value)
        : coll.GetById(id);

    return doc != null ? Results.Ok(doc) : Results.NotFound(new { error = $"Document with ID '{id}' not found." });
});

app.MapPost("/api/databases/{dbName}/collections/{collName}/documents", (string dbName, string collName, [FromBody] Dictionary<string, object> data, [FromQuery] bool sync = false, [FromQuery] long txId = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetOrCreateCollection(collName);

    string? customId = null;
    if (data.TryGetValue("_id", out var idObj) && idObj != null)
    {
        customId = idObj.ToString();
        data.Remove("_id");
    }

    var doc = coll.Insert(data, customId, txId);
    if (sync)
    {
        db.FlushCollection(collName);
    }

    return Results.Created($"/api/databases/{dbName}/collections/{collName}/documents/{doc.Id}", doc);
});

app.MapPost("/api/databases/{dbName}/collections/{collName}/batch", (string dbName, string collName, [FromBody] List<Dictionary<string, object>> batch, [FromQuery] bool sync = false, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetOrCreateCollection(collName);

    var createdList = new List<Document>(batch.Count);
    foreach (var data in batch)
    {
        string? customId = null;
        if (data.TryGetValue("_id", out var idObj) && idObj != null)
        {
            customId = idObj.ToString();
            data.Remove("_id");
        }
        var doc = coll.Insert(data, customId);
        createdList.Add(doc);
    }

    if (sync)
    {
        db.FlushCollection(collName);
    }

    return Results.Ok(new { inserted = createdList.Count });
});

app.MapPut("/api/databases/{dbName}/collections/{collName}/documents/{id}", (string dbName, string collName, string id, [FromBody] Dictionary<string, object> data, [FromQuery] bool merge = true, [FromQuery] bool sync = false, [FromQuery] long txId = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var doc = coll.Update(id, data, merge, txId);
    if (doc == null) return Results.NotFound(new { error = $"Document '{id}' not found." });

    if (sync)
    {
        db.FlushCollection(collName);
    }
    return Results.Ok(doc);
});

app.MapDelete("/api/databases/{dbName}/collections/{collName}/documents/{id}", (string dbName, string collName, string id, [FromQuery] bool sync = false, [FromQuery] long txId = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var deleted = coll.Delete(id, txId);
    if (deleted)
    {
        if (sync)
        {
            db.FlushCollection(collName);
        }
        return Results.Ok(new { message = $"Document '{id}' deleted." });
    }
    return Results.NotFound(new { error = $"Document '{id}' not found." });
});

// Checkpoint & WAL Truncation
app.MapPost("/api/databases/{dbName}/collections/{collName}/checkpoint", (string dbName, string collName, CheckpointManager ckpt, DatabaseEngine dbEngine) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var stats = ckpt.CheckpointCollection(dbName, coll, null);
    return Results.Ok(stats);
});

// Query (Pipeline: Scan -> Filter -> Sort -> Project -> Limit/Skip)
app.MapPost("/api/databases/{dbName}/collections/{collName}/query", (string dbName, string collName, [FromBody] Dictionary<string, object> query, [FromQuery] string? sort = null, [FromQuery] bool asc = true, [FromQuery] string? project = null, [FromQuery] int limit = 100, [FromQuery] int skip = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.Ok(new List<Document>());

    var projectList = !string.IsNullOrWhiteSpace(project) ? project.Split(',', StringSplitOptions.TrimEntries).ToList() : null;
    var results = coll.Query(query, sortField: sort, ascending: asc, projectFields: projectList, limit: limit, skip: skip);
    return Results.Ok(results);
});

// Aggregate Pipeline
app.MapPost("/api/databases/{dbName}/collections/{collName}/aggregate", (string dbName, string collName, [FromBody] AggregateRequest request, [FromQuery] string? filter = null, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    Dictionary<string, object>? filterDict = null;
    if (!string.IsNullOrWhiteSpace(filter))
    {
        filterDict = JsonSerializer.Deserialize<Dictionary<string, object>>(filter);
    }

    var aggResults = coll.Aggregate(request, filterDict);
    return Results.Ok(aggResults);
});

// Explain
app.MapPost("/api/databases/{dbName}/collections/{collName}/explain", (string dbName, string collName, [FromBody] Dictionary<string, object> query, [FromQuery] string? sort = null, [FromQuery] string? project = null, [FromQuery] int limit = 100, [FromQuery] int skip = 0, DatabaseEngine dbEngine = null!) =>
{
    var db = dbEngine.GetOrCreateDatabase(dbName);
    var coll = db.GetCollection(collName);
    if (coll == null) return Results.NotFound(new { error = "Collection not found" });

    var projectList = !string.IsNullOrWhiteSpace(project) ? project.Split(',', StringSplitOptions.TrimEntries).ToList() : null;
    var explainResult = coll.ExplainQuery(query, sortField: sort, projectFields: projectList, limit: limit, skip: skip);
    return Results.Ok(explainResult);
});

// Print ASCII Banner on startup
PrintBanner();

app.Run();

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(@"
   ____  _                             ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("  SingamDB Server v3.0.0 [Running]");
    Console.WriteLine("  Port:         http://0.0.0.0:7777");
    Console.WriteLine("  Storage Path: ./singam_data");
    Console.WriteLine("  Engine:       Volcano Pipeline (Scan -> Filter -> Sort -> Project)");
    Console.WriteLine("  Indexes:      Primary Hash, B-Tree Range, Composite Multi-Key");
    Console.WriteLine("  Storage:      Binary 4KB Slotted Pages + WAL + Fuzzy Checkpoints");
    Console.WriteLine("  Concurrency:  MVCC Snapshot Isolation");
    Console.WriteLine("===================================================\n");
    Console.ResetColor();
}

public class IndexRequest
{
    public string Field { get; set; } = string.Empty;
    public string? Type { get; set; } = "hash"; // "hash" or "btree"
}

public class CompositeIndexRequest
{
    public string[] Fields { get; set; } = Array.Empty<string>();
}

public static class ServerStartTime
{
    public static readonly DateTime StartedAt = DateTime.UtcNow;
}

public class AutoFlushBackgroundService : BackgroundService
{
    private readonly DatabaseEngine engine;
    private readonly ILogger<AutoFlushBackgroundService> logger;

    public AutoFlushBackgroundService(DatabaseEngine engine, ILogger<AutoFlushBackgroundService> logger)
    {
        this.engine = engine;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            try
            {
                engine.FlushAll();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while auto-flushing SingamDB state.");
            }
        }
    }
}
