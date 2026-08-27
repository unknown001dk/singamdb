# Contributing to SingamDB

Thank you for your interest in contributing to **SingamDB**!

## Development Setup

1. **Prerequisites**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **Clone the Repository**:
   ```bash
   git clone https://github.com/unknown001dk/singamdb.git
   cd singamdb
   ```
3. **Build the Solution**:
   ```bash
   dotnet build SingamDB.sln -c Release
   ```
4. **Run Verification & Benchmarks**:
   ```bash
   ./run-all-benchmarks.sh
   ```

## Pull Request Guidelines

- Ensure code builds with 0 errors and 0 warnings.
- Run the full chaos and benchmark test suite before submitting PRs.
- Write clear commit messages describing changes and rationale.
