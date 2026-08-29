# SingamDB Architecture Specification

SingamDB is engineered with a modern, high-performance database engine architecture designed for high IOPS, sub-millisecond query latency, and ACID-compliant storage durability.

---

## Architecture Diagram

```
+-----------------------------------------------------------------------+
|                             SingamDB CLI                              |
|                       (Interactive Shell / REPL)                      |
+-----------------------------------+-----------------------------------+
                                    |
+-----------------------------------+-----------------------------------+
|                           Network Layer                               |
|        (Binary Wire Protocol :7778 / High-Throughput HTTP REST :7777)  |
+-----------------------------------+-----------------------------------+
                                    |
+-----------------------------------+-----------------------------------+
|                        Volcano Query Engine                           |
|        (Cost Planner, Predicate Pushdown, Aggregations, Explains)      |
+-------------------+-------------------------------+-------------------+
                    |                               |
+-------------------+---------------+   +-----------+-------------------+
|              Indexing             |   |        Transaction & MVCC     |
|   (B+ Tree, Hash, Composite,      |   |   (Snapshot Isolation, Read/  |
|    Online Background Indexer)     |   |    Write Locks, Rollbacks)    |
+-------------------+---------------+   +-----------+-------------------+
                    |                               |
+-------------------+-------------------------------+-------------------+
|                           Storage Layer                               |
|   (4KB Slotted Binary Pages, LRU Buffer Pool, WAL Log, Checkpointer)  |
+-----------------------------------+-----------------------------------+
                                    |
                             Disk / NVMe SSD
```

---

## Core Subsystems

### 1. Slotted Page Binary Storage (`SingamDB.Storage`)
- Fixed 4096-byte (4KB) slotted pages.
- Header holds page ID, slot count, free space pointer, and checksum.
- Slotted architecture allows records to be packed from the end of the page backward, while slot offset pointers grow forward from the header.
- Zero fragmentation on variable-length document updates.

### 2. Buffer Pool Manager & LRU Cache
- Configurable frame pool pinning hot pages directly in RAM.
- Least-Recently-Used (LRU) eviction algorithm.
- Dirty page tracking with background thread flusher.

### 3. Write-Ahead Logging (WAL) & Checkpointing
- All mutations (INSERT, UPDATE, DELETE) are serialized sequentially to an append-only WAL log before modifying memory buffers.
- Automatic recovery on startup replaying uncheckpointed WAL segments.
- Checkpoint manager commits buffer pool dirty frames and safely truncates processed WAL files.

### 4. Indexing Subsystem (`SingamDB.Indexing`)
- **Primary Hash Index**: $O(1)$ point lookups by `_id`.
- **Secondary B+ Tree Index**: $O(\log N)$ point lookups and range scans ($gt, $gte, $lt, $lte).
- **Composite Multi-Field Indexes**: Composite keys for multi-attribute matching and sorting without table scans.
- **Online Background Index Builder**: Builds new indexes asynchronously without acquiring write locks on the live collection.

### 5. Volcano Iterator Query Engine (`SingamDB.Query`)
- Pull-based execution engine adhering to standard `Open()`, `Next()`, `Close()` iterator protocols.
- Pipeline execution minimizes heap allocation and avoids loading unnecessary documents into memory.
- Query optimizer automatically evaluates index presence and selects optimal execution paths.

### 6. Binary Wire Protocol (`SingamDB.Network`)
- Compact binary framing with 1-byte OpCode, 4-byte payload length, and raw BSON/JSON binary payloads.
- Delivers over 10x throughput over traditional JSON/HTTP parsing overhead.
- Supports streaming batch writes and multiplexed connections.
