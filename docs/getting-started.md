# Getting Started with SingamDB

SingamDB is an ultra-fast, embedded & networked document database engine with Slotted Pages, LRU Buffer Pool, WAL crash recovery, MVCC Snapshot Isolation, Volcano Query Engine, B+ Tree & Composite Indexing, and high-throughput Binary Wire Protocol.

---

## 1. Quick Installation

### Linux / macOS
```bash
curl -fsSL https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.sh | bash
```

### Windows (PowerShell)
```powershell
iwr -useb https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1 | iex
```

---

## 2. Interactive CLI

Launch the interactive terminal shell:
```bash
singam-cli
```

### Basic Commands
```text
singamdb> use ecommerce
singamdb> insert users {"name": "Suriya", "tier": "gold", "credits": 500}
singamdb> find users {"tier": "gold"}
singamdb> create-index users tier --btree
singamdb> explain users {"tier": "gold"}
singamdb> stats
```

---

## 3. Embedding in .NET Applications

Add reference to `SingamDB.Core`:
```csharp
using SingamDB.Core;

// Initialize Storage & Database Engine
var engine = new DatabaseEngine("singam_data");
var db = engine.GetOrCreateDatabase("production");
var collection = db.GetOrCreateCollection("orders");

// Index field for accelerated B+ Tree queries
collection.CreateIndex("status", isBTree: true);

// Insert document
var order = new Document(new Dictionary<string, object>
{
    { "orderId", "ORD-9901" },
    { "amount", 249.50 },
    { "status", "CONFIRMED" },
    { "items", 3 }
});

collection.Insert(order);

// Query collection
var result = collection.FindById(order.Id);
Console.WriteLine($"Order status: {result?.GetValue("status")}");

// Flush changes to durable storage
db.Flush();
```

---

## 4. Starting the Daemon Server

Run SingamDB server with HTTP REST API (port `7777`) and Binary Wire Protocol (port `7778`):
```bash
dotnet run --project src/SingamDB.Server
```
Or with Docker:
```bash
docker-compose up -d
```
