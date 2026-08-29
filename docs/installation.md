# Installation Guide

SingamDB can be installed across all major operating systems, run via official Docker containers, or compiled directly from source using the .NET 8 SDK.

---

## 1. System Requirements

- **Runtime**: [.NET 8.0 SDK / Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- **Supported Operating Systems**:
  - Linux (Ubuntu 20.04+, Debian 11+, RHEL 8+, Alpine 3.18+)
  - macOS (Apple Silicon M1/M2/M3/M4 & Intel x64, macOS 12+)
  - Windows (Windows 10/11, Windows Server 2019+)
- **Architecture**: x86_64 (amd64) and ARM64 (aarch64)

---

## 2. One-Line Automated Installers

### macOS & Linux
```bash
curl -fsSL https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.sh | bash
```

### Windows (PowerShell)
```powershell
iwr -useb https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1 | iex
```

The installer will:
1. Detect your OS and CPU architecture.
2. Compile and package the native self-contained binaries (`singam-server` and `singam-cli`).
3. Symlink executable binaries into your system PATH (`/usr/local/bin` or `~/.local/bin` on Unix, `%USERPROFILE%\AppData\Local\SingamDB\bin` on Windows).

---

## 3. Docker Container Deployment

### Run Container Directly
```bash
docker run -d \
  --name singamdb \
  -p 7777:7777 \
  -p 7778:7778 \
  -v singam_data:/app/singam_data \
  --restart unless-stopped \
  singamdb/server:latest
```

### Docker Compose
```yaml
version: '3.8'
services:
  singamdb:
    build: .
    ports:
      - "7777:7777"
      - "7778:7778"
    volumes:
      - singam_data:/app/singam_data
    restart: unless-stopped

volumes:
  singam_data:
```
Run with:
```bash
docker-compose up -d
```

---

## 4. Building from Source

```bash
# Clone the repository
git clone https://github.com/unknown001dk/singamdb.git
cd singamdb

# Build the entire solution
dotnet build SingamDB.sln -c Release

# Run tests to verify
dotnet test SingamDB.sln

# Publish the CLI tool
dotnet publish src/SingamDB.CLI/SingamDB.CLI.csproj -c Release -o ./publish/cli
```
