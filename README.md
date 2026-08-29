# SingamDB

<div align="center">

```
   ____  _                            ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
```

**Ultra-Fast, Embeddable & Networked Document Database Engine with ACID Durability**

[![Build & Test](https://img.shields.io/badge/build-passing-brightgreen.svg?style=flat-square)]()
[![.NET Version](https://img.shields.io/badge/.NET-8.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-orange.svg?style=flat-square)](docs/contributing-guide.md)

</div>

---

## 🚀 Key Architectural Highlights

- **4KB Binary Slotted Pages**: High-density record packing with slotted headers, zero-fragmentation updates, and checksum validations.
- **LRU Buffer Pool Frame Manager**: Dedicated memory-mapped buffer cache keeping hot frames pinned in RAM.
- **Write-Ahead Logging (WAL) & Fast Recovery**: Append-only log with crash recovery replay and automated checkpoint truncation.
- **Volcano Iterator Query Engine**: Pull-based query pipeline supporting predicate pushdown, filter evaluation, aggregations, and execution cost estimation.
- **B+ Tree & Composite Indexing**: Primary Hash $O(1)$ lookup, Secondary B+ Tree $O(\log N)$ range scans, and multi-column Composite indexes.
- **MVCC Snapshot Isolation**: Multi-version concurrency control providing non-blocking concurrent reads and isolated writes.
- **Binary Wire Protocol**: Low-latency TCP binary protocol delivering over 10x throughput over traditional REST.
- **Dual Server Interface**: Native Binary Wire Protocol (port `7778`) and HTTP RESTful API (port `7777`).

---

## 📁 Repository Architecture

```
SingamDB/
│
├── src/
│   ├── SingamDB.Core/          # Core models, MVCC, Lock Manager, Schema & Engine orchestration
│   ├── SingamDB.Storage/       # Slotted binary pages, LRU Buffer Pool, WAL & Checkpointing
│   ├── SingamDB.Query/         # Volcano Query Engine, Operators, Aggregations & Cost Planner
│   ├── SingamDB.Indexing/      # B+ Tree Index, Composite Index, Online Index Builder
│   ├── SingamDB.Network/       # Binary Wire Protocol Server & Client, Replication
│   └── SingamDB.CLI/           # Interactive Terminal REPL, Admin Tools & Benchmarks
│
├── tests/
│   ├── SingamDB.Core.Tests/    # Unit tests for Core models, MVCC snapshots, Transactions
│   ├── SingamDB.Query.Tests/   # Unit tests for Volcano query operators, filters, aggregations
│   └── SingamDB.IntegrationTests/ # End-to-end integration tests, WAL recovery, Wire Protocol
│
├── docs/
│   ├── getting-started.md      # Quickstart and introductory guide
│   ├── installation.md         # Multi-platform installation and Docker instructions
│   ├── architecture.md         # Comprehensive kernel architecture specification
│   ├── api-reference.md        # HTTP REST and Binary Wire Protocol specification
│   └── contributing-guide.md   # Developer guide, code style, and PR workflow
│
├── examples/
│   ├── basic-usage/            # Simple database init, collection creation & document queries
│   ├── crud-example/           # Complete CRUD lifecycle and transaction rollback sample
│   └── advanced-query/         # Multi-field composite indexing and aggregation pipelines
│
├── .github/
│   ├── ISSUE_TEMPLATE/
│   │   ├── bug_report.md
│   │   └── feature_request.md
│   │
│   ├── workflows/
│   │   ├── build.yml
│   │   └── tests.yml
│   │
│   └── pull_request_template.md
│
├── README.md
├── LICENSE
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
├── CHANGELOG.md
└── ROADMAP.md
```

---

## ⚡ Quick Start

### 1. One-Line Installation

**macOS / Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.sh | bash
```

**Windows (PowerShell):**
```powershell
iwr -useb https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1 | iex
```

### 2. Launch Interactive CLI

```bash
singam-cli
```

```text
singamdb> use store
singamdb> insert items {"title": "Mechanical Keyboard", "price": 129.99, "category": "electronics"}
singamdb> create-index items category --btree
singamdb> find items {"category": "electronics"}
singamdb> explain items {"category": "electronics"}
```

### 3. Run with Docker

```bash
docker run -d -p 7777:7777 -p 7778:7778 -v singam_data:/app/singam_data singamdb/server:latest
```

---

## 💻 C# Embedding Quickstart

```csharp
using SingamDB.Core;

// 1. Initialize engine
var engine = new DatabaseEngine("singam_data");
var db = engine.GetOrCreateDatabase("production");
var users = db.GetOrCreateCollection("users");

// 2. Index field with B+ Tree
users.CreateIndex("email", isBTree: true);

// 3. Insert document
var user = new Document(new Dictionary<string, object>
{
    { "email", "admin@singamdb.io" },
    { "role", "superuser" },
    { "loginCount", 10 }
});
users.Insert(user);

// 4. Query
var result = users.FindById(user.Id);
Console.WriteLine($"Logged in: {result?.GetValue("email")}");

// 5. Persist to Slotted Pages
db.Flush();
```

---

## 🧪 Running Tests & Verification

```bash
# Build entire solution
dotnet build SingamDB.sln

# Run all unit and integration test suites
dotnet test SingamDB.sln
```

---

## 📚 Documentation Links

- 🚀 [Getting Started](docs/getting-started.md)
- 💾 [Installation Guide](docs/installation.md)
- 📐 [Architecture Specification](docs/architecture.md)
- 📡 [API & Wire Protocol Reference](docs/api-reference.md)
- 🤝 [Contributing Guidelines](docs/contributing-guide.md)
- 🗺️ [Roadmap](ROADMAP.md)

---

## 📄 License

SingamDB is open-source software licensed under the [MIT License](LICENSE).
