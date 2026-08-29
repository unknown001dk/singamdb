# Changelog

All notable changes to **SingamDB** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.0.0] - 2026-08-29

### Added
- **Modular Multi-Project Architecture**: Refactored core engine into `SingamDB.Core`, `SingamDB.Storage`, `SingamDB.Indexing`, `SingamDB.Query`, `SingamDB.Network`, and `SingamDB.CLI`.
- **4KB Binary Slotted Pages**: Zero-fragmentation record packing format with slotted headers and checksums.
- **LRU Buffer Pool Manager**: High-performance frame cache reducing physical disk I/O.
- **Volcano Iterator Query Engine**: Pull-based query pipeline with predicate pushdown and cost-based explain plans.
- **Composite Indexing**: Multi-column composite B+ tree index support for accelerated range queries.
- **Binary Wire Protocol**: Native TCP binary framing protocol delivering over 10x throughput.
- **MVCC Snapshot Isolation**: Lock-free multi-version concurrency control with non-blocking reads.
- **Write-Ahead Logging (WAL) & Auto-Recovery**: ACID crash resilience with checkpoint truncation.

### Changed
- Migrated codebase to modern .NET 8.0 SDK.
- Optimized query executor to bypass reflection and use direct dictionary access.

---

## [2.0.0] - 2026-04-15

### Added
- Secondary B+ Tree indexing for range queries (`$gt`, `$lt`, `$gte`, `$lte`).
- High-concurrency HTTP REST server with auto-flushing daemon.
- Interactive CLI with REPL mode and syntax highlighting.

---

## [1.0.0] - 2026-01-10

### Initial Release
- Basic document storage engine with primary hash indexing.
- Read/write locks with `ReaderWriterLockSlim`.
- JSON disk serialization and file persistence.
