# SingamDB Engineering Roadmap

This document outlines the strategic engineering roadmap and upcoming architectural milestones for SingamDB.

---

## 🎯 Short-Term (Q3 2026 - Q4 2026)

- [x] **Slotted Binary Page Storage Format (4KB pages)**
- [x] **LRU Buffer Pool Frame Manager**
- [x] **WAL Crash Recovery & Checkpointing**
- [x] **Volcano Query Execution Engine**
- [x] **Composite Indexing & Cost-Based Explain Plan**
- [x] **Native Binary Wire Protocol Server & Client**
- [ ] **Multi-Collection JOIN Operator (Hash Join / Nested Loop Join in Volcano Engine)**
- [ ] **Dynamic Bloom Filters for SSTable/Page Probing**

---

## 🚀 Medium-Term (Q1 2027 - Q2 2027)

- [ ] **Distributed Consensus with Raft Engine**
  - Leader election, log replication, and split-brain resilience.
- [ ] **Native Vector Embeddings & HNSW Indexing**
  - $L_2$ distance and Cosine similarity for AI/RAG workloads.
- [ ] **Columnar Parquet Export & Storage Compression**
  - SIMD-accelerated bit-packing and Zstandard dictionary compression.
- [ ] **GraphQL Engine Subsystem**
  - Native GraphQL schema generator over collections.

---

## 🌐 Long-Term (2027+)

- [ ] **WebAssembly (WASM) Stored Procedures & Triggers**
- [ ] **Geo-Distributed Active-Active Multi-Region Clustering**
- [ ] **Hardware Acceleration (AVX-512 & GPU Index Scanning)**
