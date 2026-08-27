#!/usr/bin/env bash
set -e

echo "Building SingamDB in Release Mode..."
dotnet build SingamDB.sln -c Release

echo ""
echo "=========================================================================="
echo " 1. RUNNING IN-MEMORY INDEX SCALING BENCHMARK (10K, 100K, 1M RECORDS)"
echo "=========================================================================="
dotnet run --project SingamDB.Benchmark -c Release --no-build

echo ""
echo "=========================================================================="
echo " 2. RUNNING SYSTEMS, CHAOS & INVARIANT VERIFICATION SUITE"
echo "=========================================================================="
dotnet run --project SingamDB.HttpBenchmark -c Release --no-build

echo ""
echo "[OK] All SingamDB benchmark suites completed successfully!"
