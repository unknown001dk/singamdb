# Multi-stage build for SingamDB Server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY SingamDB.sln ./
COPY SingamDB.Core/SingamDB.Core.csproj SingamDB.Core/
COPY SingamDB.Server/SingamDB.Server.csproj SingamDB.Server/
COPY SingamDB.Cli/SingamDB.Cli.csproj SingamDB.Cli/
COPY SingamDB.Benchmark/SingamDB.Benchmark.csproj SingamDB.Benchmark/
COPY SingamDB.HttpBenchmark/SingamDB.HttpBenchmark.csproj SingamDB.HttpBenchmark/

# Restore dependencies
RUN dotnet restore

# Copy all source code and build Release
COPY . .
RUN dotnet publish SingamDB.Server/SingamDB.Server.csproj -c Release -o /app/publish

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
