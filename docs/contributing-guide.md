# Contributing Guide for SingamDB

Thank you for contributing to SingamDB! We welcome contributions ranging from documentation fixes to major kernel enhancements.

---

## 1. Development Environment Setup

1. **Prerequisites**:
   - .NET 8.0 SDK (`dotnet --version` >= 8.0.100)
   - Git
   - Docker (optional, for container validation)

2. **Fork and Clone**:
   ```bash
   git clone https://github.com/unknown001dk/singamdb.git
   cd singamdb
   ```

3. **Build All Projects**:
   ```bash
   dotnet build SingamDB.sln
   ```

4. **Run the Test Suite**:
   ```bash
   dotnet test SingamDB.sln
   ```

---

## 2. Project Architecture & Standards

- `src/SingamDB.Core`: Core domain models, transactions, MVCC, lock manager, schema.
- `src/SingamDB.Storage`: Slotted binary page serializer, buffer pool, WAL, checkpoints.
- `src/SingamDB.Indexing`: B+ Tree, Composite index, online indexer.
- `src/SingamDB.Query`: Volcano query engine, operators, aggregations, planner.
- `src/SingamDB.Network`: Binary wire protocol server & client, replication.
- `src/SingamDB.CLI`: Interactive REPL and database tooling.

### Code Style Guidelines
- Use modern C# 12 / .NET 8 idioms (file-scoped namespaces, pattern matching, primary constructors where appropriate).
- Avoid heap allocations in hot I/O and query execution paths.
- Ensure all public APIs are documented with XML documentation comments.
- Keep tests isolated and deterministic.

---

## 3. Submitting Pull Requests

1. Create a feature branch: `git checkout -b feature/my-enhancement`
2. Write unit and/or integration tests for your changes.
3. Verify that `dotnet test SingamDB.sln` passes with 0 failures.
4. Push your branch and open a Pull Request against `main` using our PR template.
