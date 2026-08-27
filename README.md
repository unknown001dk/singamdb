# SingamDB

<p align="center">
  <img src="https://img.shields.io/badge/Language-C%23%2012%20%2F%20.NET%208-blue" alt="C# .NET 8">
  <img src="https://img.shields.io/badge/Architecture-Slotted%20Pages%20%2B%20WAL%20%2B%20MVCC-success" alt="Architecture">
  <img src="https://img.shields.io/badge/Wire%20Protocol-TCP%20Binary%20(Port%207778)-orange" alt="Wire Protocol">
  <img src="https://img.shields.io/badge/Performance-79%2C590%20RPS-brightgreen" alt="Performance">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
</p>

**SingamDB** is a high-performance database server and storage engine built from scratch in **C# / .NET 8**. It features **Binary Slotted 4KB Page Block Storage**, **B-Tree Range Indexes**, **Composite Multi-Field Indexes**, **Unique Key Constraints**, **CRC32-Checksummed Write-Ahead Logging (WAL)**, **LRU Buffer Pool Caching**, **Snapshot Isolation (MVCC)** with first-committer-wins conflict detection, **Background Vacuum Garbage Collection**, **Streaming Primary-Replica WAL Replication**, **Hierarchical Lock Management with Deadlock Cycle Detection**, a **Native TCP Binary Wire Protocol (Port 7778)** with official **Node.js and Python Drivers**, a **REST API (Port 7777)**, and an **Interactive CLI Shell**.

---

## Architecture Overview

```text
                               SingamDB Server
                                      │
            ┌─────────────────────────┼─────────────────────────┐
            ▼                         ▼                         ▼
    [Layer 1: Native Wire]   [Layer 2: HTTP REST]       [Layer 3: CLI]
       TCP Socket: 7778          HTTP Port: 7777        Interactive Shell
       (Binary + CRC32)         (JSON Web API)          (Admin Terminal)
            │                         │                         │
            ▼                         ▼                         ▼
   Official Client Drivers    Web Apps / Microservices     singam-cli
  (Node.js / Python / .NET)      (curl / fetch API)
            │                         │                         │
            └─────────────────────────┼─────────────────────────┘
                                      ▼
                           Query Engine 2.0
                                      │
                               Query Optimizer
                                      │
                           Volcano Iterator Engine
        [ScanNode -> FilterNode -> SortNode -> ProjectNode -> LimitSkipNode]
                                      │
                          Index & Transaction Manager
          (Primary Hash, B-Tree Range, Composite, Unique Keys, MVCC)
                                      │
                        Vacuum & Lock Manager (Deadlocks)
                                      │
                              LRU Buffer Pool
                                      │
                        Binary 4KB Slotted Pages & WAL
                                      │
                        Fuzzy Checkpoints & Replication
```

---

## Production Segmented Storage Layout

SingamDB stores database files in a partitioned, production-grade directory layout:

```text
singam_data/
└── <database_name>/
    └── <collection_name>/
        ├── data/
        │   ├── 000001.bin           <-- 4KB Binary Slotted Block Pages
        │   └── ...
        ├── indexes/
        │   ├── rank.idx             <-- B-Tree / Hash Index Segments
        │   └── ...
        ├── wal/
        │   ├── 000001.wal           <-- Append-Only CRC32 Transaction Log
        │   └── ...
        └── metadata/
            └── schema.meta          <-- Collection Metadata & Schema Definition
```

---

## Feature Comparison Matrix

| Capability | SingamDB | PostgreSQL | MongoDB / WiredTiger |
| :--- | :--- | :--- | :--- |
| **Page-oriented storage** | **Yes (4KB Slotted Pages)** | Yes (8KB) | Yes |
| **Binary persistent data** | **Yes (.bin blocks)** | Yes | Yes |
| **WAL & Crash Recovery** | **Yes (CRC32 Checksummed)** | Yes | Yes |
| **B-Tree Range Indexes** | **Yes ($gt, $lt, $between)** | Yes | Yes |
| **Hash Indexes** | **Yes (O(1) Point Lookups)** | Yes | Yes |
| **Composite Multi-Key Indexes**| **Yes (e.g. `city, rank`)** | Yes | Yes |
| **Unique Key Constraints** | **Yes (Duplicate Rejection)**| Yes | Yes |
| **Foreign Keys & Referential Actions** | **Yes (RESTRICT / CASCADE)** | Yes | Manual / Document Embedding |
| **MVCC Snapshot Isolation**| **Yes (First-Committer-Wins)**| Yes | Yes |
| **Vacuum / Garbage Collector**| **Yes (`VacuumEngine`)** | Yes | Internal |
| **Streaming Replication** | **Yes (`ReplicationEngine`)**| Yes | Yes |
| **Hierarchical Lock Manager**| **Yes (Wait-For Deadlock Detection)**| Yes | Yes |
| **Online Index Maintenance**| **Yes (Non-blocking concurrent build)**| Yes | Yes |
| **Native Wire Protocol** | **Yes (TCP Port 7778)** | Yes (Port 5432) | Yes (Port 27017) |
| **REST API** | **Yes (HTTP Port 7777)** | Extension | Extension |
| **Interactive Terminal CLI** | **Yes (`singam-cli`)** | Yes (`psql`) | Yes (`mongosh`) |

---

## Benchmark & Verification Results

### 1. In-Memory Index Scaling Benchmark

| Dataset Size | Full Scan (No Index) | Hash / B-Tree Index | Speedup |
| :--- | :--- | :--- | :--- |
| **10,000 docs** | 1.52 ms | 0.0014 ms | **1,084.0x faster** |
| **100,000 docs** | 12.94 ms | 0.0068 ms | **1,894.7x faster** |
| **1,000,000 docs** | 132.16 ms | 0.0030 ms | **44,224.0x faster** |

### 2. High-Concurrency Client Saturation Curve

| Concurrent Clients | Throughput | Median (P50) Latency | Tail (P99) Latency |
| :--- | :--- | :--- | :--- |
| 1 client | 6,193 req/s | 0.144 ms | 0.318 ms |
| 8 clients | 49,004 req/s | 0.151 ms | 0.258 ms |
| 32 clients | 72,898 req/s | 0.374 ms | 2.416 ms |
| **64 clients** | **79,590 req/s** | **0.669 ms** | **3.018 ms** |
| 128 clients | 76,247 req/s | 1.358 ms | 4.686 ms |
| 256 clients | 63,863 req/s | 3.146 ms | 10.622 ms |

### 3. Systems Invariant Verification Suite
```text
==================================================================================================
                 SINGAMDB ADVANCED SYSTEMS CAPABILITIES VERIFICATION SUITE
==================================================================================================
[OK] TEST 1: MVCC SNAPSHOT ISOLATION & CONFLICT DETECTION          : PASS (First Committer Wins)
[OK] TEST 2: VACUUM & DEAD-VERSION GARBAGE COLLECTION              : PASS (Stale versions purged)
[OK] TEST 3: PRIMARY-FOLLOWER STREAMING WAL REPLICATION            : PASS (100% Exact Sync)
[OK] TEST 4: HIERARCHICAL LOCK MANAGER & DEADLOCK DETECTION        : PASS (Cycle Aborted)
[OK] TEST 5: ONLINE NON-BLOCKING INDEX MAINTENANCE                 : PASS (Zero write blocking)
==================================================================================================
```

---

## 1-Click Installation & Global Commands

### Online 1-Line Installer

**macOS & Linux (Terminal)**:
```bash
curl -fsSL https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.sh | bash
```

**Windows (PowerShell)**:
```powershell
irm https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1 | iex
```

### Local Build Installation
```bash
./install.sh
```

Once installed, you can access SingamDB from **any directory** on your computer:

```bash
singam-server       # Start the SingamDB Server daemon (Ports 7777 & 7778)
singam              # Open the Interactive SingamDB Shell
singam-cli          # (Alias) Open the Interactive Shell
```

To uninstall:
```bash
./uninstall.sh
```
*The server starts listening on:*
- **HTTP REST API**: `http://localhost:7777`
- **Native TCP Wire Protocol**: `singam://localhost:7778`

### 3. Launch the Interactive CLI Shell
```bash
./singam-cli.sh
```

---

## Interactive Shell Commands

```text
SHOW DBS                                - List all databases
USE <db_name>                           - Switch or create active database
SHOW COLLECTIONS                        - List collections in active database
COLL <coll_name>                        - Switch or create collection

INSERT <json>                           - Insert single document
BATCH [<json>, <json>]                  - Bulk/Batch insert multiple documents
GET <id>                                - Instant O(1) Primary Key lookup
UPDATE <id> <json>                      - Update document fields by ID
DELETE <id>                             - Delete document by ID

FIND [json] [SORT <f> [ASC|DESC]]       - Execute full Volcano query pipeline
     [PROJECT <f1,f2>] [LIMIT n]        - e.g. FIND {"age":{"$gt":25}} SORT age DESC LIMIT 5

EXPLAIN FIND [json] [SORT ...]          - Show Volcano Execution Plan & estimated cost
AGGREGATE <json>                        - Run Analytics ($groupBy, count, avg, sum, min, max)

INDEX <field> [btree]                   - Create Hash or B-Tree Range Index
INDEX <field> UNIQUE                    - Create Unique Key Constraint
INDEX <field1,field2>                   - Create Composite Multi-Key Index

STATS                                   - Show collection document counts & index metadata
CHECKPOINT                              - Execute Fuzzy Checkpoint and WAL Truncation
DROP DB <name> | COLL <name>            - Drop database or collection
CLEAR                                   - Clear terminal screen
EXIT                                    - Quit shell
```

---

## Client Connection & Official Drivers

### 1. Node.js Native Driver (`singamdb`)

```javascript
const { SingamClient } = require('./drivers/node/singamdb.js');

const client = new SingamClient('singam://localhost:7778');
await client.connect();

const db = client.database('xman');
const mutants = db.collection('mutants');

// 1. Create B-Tree & Unique Indexes
await mutants.createIndex('age', { isBTree: true });
await mutants.createIndex('email', { unique: true });

// 2. Insert Document
await mutants.insertOne({ name: 'Logan', power: 'Regeneration', age: 137, rank: 'Alpha' });

// 3. Fluent Chaining Query
const results = await mutants.find({ age: { $gte: 30 } })
  .sort({ age: -1 })
  .project('name', 'power', 'age')
  .limit(10)
  .toArray();

console.log(results);

// 4. Aggregations Pipeline
const stats = await mutants.aggregate({
  groupBy: 'rank',
  avg: 'age',
  count: true
});
console.log(stats);

client.close();
```

---

### 2. Python Native Driver

```python
from drivers.python.singamdb import SingamClient

client = SingamClient("singam://localhost:7778")
db = client["xman"]
mutants = db["mutants"]

# Insert
mutants.insert_one({"name": "Logan", "power": "Regeneration", "age": 137, "rank": "Alpha"})

# Query Cursor
for hero in mutants.find({"age": {"$gte": 30}}).sort("age", -1).limit(5):
    print(hero)

client.close()
```

---

### 3. HTTP REST API (curl / fetch)

```javascript
// Query with Sorting & Projection
const res = await fetch('http://localhost:7777/api/databases/xman/collections/mutants/query?sort=age&asc=false&project=name,power', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ age: { $gte: 30 } })
});
const docs = await res.json();
```

---

## Running All Benchmarks & Invariant Tests

```bash
./run-all-benchmarks.sh
```

---

## License

This project is licensed under the [MIT License](LICENSE).
