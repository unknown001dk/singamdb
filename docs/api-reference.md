# SingamDB API Reference

SingamDB provides dual access models:
1. High-Performance **HTTP REST API** (Port `7777`)
2. Low-Latency **Binary Wire Protocol** (Port `7778`)

---

## 1. HTTP REST API Reference

### Health & Server Information
- `GET /`
  - Returns engine banner, status, version, and enabled features.

### Database Operations
- `GET /api/v1/databases`
  - List all active databases.
- `POST /api/v1/databases/{db}`
  - Create a new database.
- `DELETE /api/v1/databases/{db}`
  - Drop a database and all its collections.

### Collection Operations
- `GET /api/v1/{db}/collections`
  - List collections in database.
- `POST /api/v1/{db}/{coll}`
  - Create collection.
- `DELETE /api/v1/{db}/{coll}`
  - Drop collection.
- `GET /api/v1/{db}/{coll}/stats`
  - Fetch detailed collection statistics, document count, page count, and index lists.

### Document Operations
- `POST /api/v1/{db}/{coll}/insert`
  - Body: JSON Document payload.
- `POST /api/v1/{db}/{coll}/batch-insert`
  - Body: Array of JSON Document payloads.
- `GET /api/v1/{db}/{coll}/find/{id}`
  - Fetch document by primary ID.
- `POST /api/v1/{db}/{coll}/query`
  - Body: Query filter object, e.g. `{"age": {"$gt": 25}}`.
- `PUT /api/v1/{db}/{coll}/update`
  - Body: Complete updated Document payload.
- `DELETE /api/v1/{db}/{coll}/delete/{id}`
  - Delete document by primary ID.

### Indexing & Query Analysis
- `POST /api/v1/{db}/{coll}/index`
  - Body: `{"field": "email", "type": "btree"}`
- `POST /api/v1/{db}/{coll}/composite-index`
  - Body: `{"fields": ["region", "status"]}`
- `POST /api/v1/{db}/{coll}/explain`
  - Body: Query filter object to analyze cost and query execution strategy.

---

## 2. Binary Wire Protocol Specification

Framing header: `[OpCode: 1 Byte] [PayloadLength: 4 Bytes BigEndian] [PayloadBytes: N Bytes]`

| OpCode | Name | Description |
| :--- | :--- | :--- |
| `0x01` | `PING` | Connection heartbeat; returns `PONG`. |
| `0x02` | `INSERT` | Single document insertion. |
| `0x03` | `FIND_ID` | Primary index $O(1)$ document lookup. |
| `0x04` | `QUERY` | Filter evaluation over collection and secondary indexes. |
| `0x05` | `UPDATE` | Document atomic update with MVCC version bump. |
| `0x06` | `DELETE` | Document deletion with tombstone marking. |
| `0x07` | `BATCH_INSERT` | Bulk high-throughput document streaming. |
| `0x08` | `CREATE_INDEX` | B+ Tree or Hash index creation. |
