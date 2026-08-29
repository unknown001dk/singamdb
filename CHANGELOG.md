# Changelog

All notable changes to **SingamDB** are documented in this file based on the repository commit history.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.0.0] - 2026-08-27

### Added
- **Volcano Iterator Query Engine**: Pull-based query pipeline supporting filtering, projections, and sorting.
- **Composite Indexing**: Multi-column index creation and composite index range scans.
- **Aggregations Pipeline**: Native `$sum`, `$avg`, `$min`, `$max`, `$count`, and `$groupBy` operations.
- **Durable Checkpointing**: `CheckpointManager` committing dirty buffer pool frames and WAL log truncation.
- **Binary Wire Protocol**: Low-latency binary TCP wire protocol server (port `7778`) and client framing (`WireProtocolServer`, `WireProtocolClient`).
- **Concurrency & Locking**: Fine-grained `LockManager` with deadlock detection, `OnlineIndexBuilder`, `ReplicationEngine`, and `VacuumEngine` garbage collection.
- **Slotted Binary Page Storage**: 4KB slotted binary pages with CRC checksums and partitioned disk storage directories.
- **Distribution & Cross-Platform Support**: Universal Linux/macOS `install.sh`, Windows PowerShell `install.ps1`, Docker container images, and official Node.js / Python drivers.

---

## [2.5.0] - 2026-08-27

### Added
- **Secondary B+ Tree Indexing**: $O(\log N)$ point lookups and range scans (`$gt`, `$gte`, `$lt`, `$lte`, `$between`).
- **Slotted Pages & Buffer Pool**: 4KB binary slotted page format with LRU buffer pool cache.
- **MVCC & Transactions**: Multi-version concurrency control with snapshot isolation and transaction rollback.
- **Benchmark Suites**: In-memory index scaling benchmark (10K, 100K, 1M records) and HTTP concurrency verification suite.
- **HTTP REST Server & CLI**: High-throughput ASP.NET Core REST API server and interactive terminal CLI tool.
