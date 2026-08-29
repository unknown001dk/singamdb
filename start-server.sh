#!/bin/bash
cd "$(dirname "$0")"
echo "Starting SingamDB Server on port 7777..."
dotnet run --project src/SingamDB.Server
