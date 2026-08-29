# Multi-stage build for SingamDB Server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY SingamDB.sln ./
COPY src/SingamDB.Core/SingamDB.Core.csproj src/SingamDB.Core/
COPY src/SingamDB.Storage/SingamDB.Storage.csproj src/SingamDB.Storage/
COPY src/SingamDB.Indexing/SingamDB.Indexing.csproj src/SingamDB.Indexing/
COPY src/SingamDB.Query/SingamDB.Query.csproj src/SingamDB.Query/
COPY src/SingamDB.Network/SingamDB.Network.csproj src/SingamDB.Network/
COPY src/SingamDB.CLI/SingamDB.CLI.csproj src/SingamDB.CLI/
COPY src/SingamDB.Server/SingamDB.Server.csproj src/SingamDB.Server/
COPY benchmarks/SingamDB.Benchmark/SingamDB.Benchmark.csproj benchmarks/SingamDB.Benchmark/
COPY benchmarks/SingamDB.HttpBenchmark/SingamDB.HttpBenchmark.csproj benchmarks/SingamDB.HttpBenchmark/

# Restore dependencies
RUN dotnet restore

# Copy all source code and build Release
COPY . .
RUN dotnet publish src/SingamDB.Server/SingamDB.Server.csproj -c Release -o /app/publish

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose HTTP (7777) and Native Wire Protocol (7778)
EXPOSE 7777
EXPOSE 7778

# Persistent volume for database storage
VOLUME /app/singam_data

ENTRYPOINT ["dotnet", "SingamDB.Server.dll"]
