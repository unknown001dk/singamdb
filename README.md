# SingamDB

<p align="center">
  <img src="https://img.shields.io/badge/Language-C%23%2012%20%2F%20.NET%208-blue" alt="C# .NET 8">
  <img src="https://img.shields.io/badge/Architecture-Slotted%20Pages%20%2B%20WAL%20%2B%20MVCC-success" alt="Architecture">
  <img src="https://img.shields.io/badge/Performance-79%2C590%20RPS-orange" alt="Performance">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
</p>

**SingamDB** is a lightweight, high-performance database engine built from scratch in **C# / .NET 8**. It features **Binary Slotted 4KB Page Block Storage**, **B-Tree Range Indexes**, **CRC32 Checksummed Write-Ahead Logging (WAL)**, **LRU Buffer Pool Caching**, **Snapshot Isolation (MVCC)** with first-committer-wins write conflict detection, and an **interactive CLI REPL shell**.

---

## Architecture

```text
+-------------------------------------------------------------------------+
|                           SingamDB Server                               |
|                            (Port: 7777)                                 |
|                                                                         |
|   HTTP REST API & Quiet Kestrel Execution Layer                         |
|            |                                                            |
|            v                                                            |
|   Query Parser & Cost-Based Optimizer (EXPLAIN)                         |
|            |                                                            |
|            |-- Primary Hash Index (O(1) ID Lookups)                     |
|            |-- Secondary Hash Indexes                                   |
|            \-- B-Tree Range Indexes ($gt, $gte, $lt, $lte, $between)    |
|            |                                                            |
|            v                                                            |
|   ACID Transaction Manager (MVCC Snapshot Isolation)                    |
|   [First-Committer-Wins Conflict Detection]                             |
|            |                                                            |
|            v                                                            |
|   LRU Buffer Pool Manager (Frame Caching & Pin Count)                   |
|            |                                                            |
|            v                                                            |
|   4KB Slotted Pages & Checksummed WAL Engine (CRC32)                    |
+------------^------------------^------------------^----------------------+
             |                  |                  |
             | (HTTP Protocol)  | (HTTP Protocol)  | (HTTP Protocol)
             |                  |                  |
   +---------+--------+   +-----+----------+   +---+-------------------+
   | Interactive CLI  |   | Node.js/React  |   | Python / C# Apps      |
   |  (singam-cli)    |   | Express API    |   | External Drivers      |
   +------------------+   +----------------+   +-----------------------+
```

---

## Production Segmented Storage Architecture

SingamDB stores database files in a partitioned directory layout:

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

## Benchmark & Verification Results

### 1. In-Memory Index Scaling Benchmark

| Dataset Size | Full Scan (No Index) | Hash / B-Tree Index | Speedup |
| :--- | :--- | :--- | :--- |
| **10,000 docs** | 1.50 ms | 0.0020 ms | **763.9x faster** |
| **100,000 docs** | 12.49 ms | 0.0016 ms | **7,583.5x faster** |
| **1,000,000 docs** | 101.62 ms | 0.0030 ms | **34,180.9x faster** |

### 2. High-Concurrency Client Saturation Curve

| Concurrent Clients | Throughput | Median (P50) Latency | Tail (P99) Latency |
| :--- | :--- | :--- | :--- |
| 1 client | 6,193 req/s | 0.144 ms | 0.318 ms |
| 8 clients | 49,004 req/s | 0.151 ms | 0.258 ms |
| 32 clients | 72,898 req/s | 0.374 ms | 2.416 ms |
| **64 clients** | **79,590 req/s** | **0.669 ms** | **3.018 ms** |
| 128 clients | 76,247 req/s | 1.358 ms | 4.686 ms |
| 256 clients | 63,863 req/s | 3.146 ms | 10.622 ms |

### 3. 64-Client Adversarial Crash & Invariant Verification

- **Workload**: 64 concurrent clients performing 40% Read, 30% Insert, 20% Update, 10% Delete under active MVCC transactions.
- **Chaos Injection**: Hard process crash followed by 72-byte torn WAL tail injection.
- **Restored**: **100% committed transactions restored**, **100% uncommitted ghost records discarded**, zero index-pointer corruption.

---

## Quick Start

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 2. Start the Server Daemon
```bash
./start-server.sh
```
*The server starts listening on `http://localhost:7777`.*

### 3. Launch the Interactive CLI Shell
```bash
./singam-cli.sh
```

---

## Interactive Shell Commands

```text
USE <dbName>                      - Switch or create database
COLL <collName>                   - Switch or create collection
INSERT {"name": "Durai Singam"}   - Insert document
FIND {"rank": "DCP"}              - Query documents
GET <docId>                       - Get document by ID
UPDATE <id> {"rank": "IGP"}       - Update document
DELETE <id>                       - Delete document
INDEX <fieldName>                 - Create index on field
EXPLAIN FIND {"rank": "DCP"}      - Query execution plan
STATS                             - Collection statistics
SHOW DBS                          - List all databases
SHOW COLLECTIONS                  - List all collections
CLEAR                             - Clear screen
EXIT                              - Quit shell
```

---

## Client Connection Examples

### Node.js / Express
```javascript
const res = await fetch('http://localhost:7777/api/databases/singam/collections/cops/documents', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ name: 'Durai Singam', rank: 'DCP', city: 'Thoothukudi' })
});
const doc = await res.json();
console.log('Inserted:', doc._id);
```

### Python
```python
import requests

url = "http://localhost:7777/api/databases/singam/collections/cops/query"
query = {"age": {"$between": [30, 45]}}
response = requests.post(url, json=query)
print("Matching cops:", response.json())
```

---

## Running All Benchmarks

```bash
./run-all-benchmarks.sh
```

---

## License

This project is licensed under the [MIT License](LICENSE).
