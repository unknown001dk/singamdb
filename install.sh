#!/usr/bin/env bash
# ==============================================================================
#  SingamDB Universal Online / Local Installer
#  Usage:
#    Online: curl -fsSL https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.sh | bash
#    Local:  ./install.sh
# ==============================================================================

set -e

# Visual colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BOLD='\033[1m'
NC='\033[0m' # No Color

echo -e "${CYAN}"
cat << 'EOF'
   ____  _                            ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
EOF
echo -e "${BOLD}High-Performance Storage Engine & Wire Protocol Database Server${NC}"
echo -e "${CYAN}===================================================================${NC}"

# Detect OS and Architecture
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"

case "$ARCH" in
    x86_64|amd64)
        TARGET_ARCH="x64"
        ;;
    arm64|aarch64)
        TARGET_ARCH="arm64"
        ;;
    *)
        echo -e "${YELLOW}[!] Warning: Unknown architecture $ARCH. Defaulting to x64.${NC}"
        TARGET_ARCH="x64"
        ;;
esac

case "$OS" in
    linux*)
        TARGET_OS="linux"
        ;;
    darwin*)
        TARGET_OS="osx"
        ;;
    msys*|cygwin*|mingw*)
        TARGET_OS="win"
        ;;
    *)
        TARGET_OS="linux"
        ;;
esac

RID="${TARGET_OS}-${TARGET_ARCH}"
INSTALL_DIR="$HOME/.singam/bin"
LIB_DIR="$HOME/.singam/lib"
GLOBAL_BIN="/usr/local/bin"

echo -e "Detected Platform: ${GREEN}${OS} (${TARGET_ARCH}) -> Runtime ID: ${RID}${NC}"
echo -e "Installing to:     ${CYAN}${INSTALL_DIR}${NC}"
echo ""

# Ensure installation directories exist
mkdir -p "$INSTALL_DIR"
mkdir -p "$LIB_DIR"

# Check if we are running from inside the source repository
IS_SOURCE_DIR=false
if [ -f "SingamDB.sln" ] && [ -d "SingamDB.Server" ]; then
    IS_SOURCE_DIR=true
fi

if [ "$IS_SOURCE_DIR" = true ]; then
    echo -e "${YELLOW}[1/3] Compiling SingamDB from local source...${NC}"
    
    # Check if dotnet is installed
    if ! command -v dotnet >/dev/null 2>&1; then
        echo -e "${RED}[ERROR] .NET SDK 8.0 or higher is required to build from source.${NC}"
        echo "Please install .NET from: https://dotnet.microsoft.com/download"
        exit 1
    fi

    # Build Server and CLI
    dotnet publish SingamDB.Server/SingamDB.Server.csproj -c Release -o "$LIB_DIR/server" --nologo -v q
    dotnet publish src/SingamDB.CLI/SingamDB.CLI.csproj -c Release -o "$LIB_DIR/cli" --nologo -v q

    # Create launcher wrappers
    cat << 'EOF' > "$INSTALL_DIR/singam-server"
#!/usr/bin/env bash
SINGAM_LIB_DIR="$HOME/.singam/lib/server"
exec dotnet "$SINGAM_LIB_DIR/SingamDB.Server.dll" "$@"
EOF

    cat << 'EOF' > "$INSTALL_DIR/singam"
#!/usr/bin/env bash
SINGAM_LIB_DIR="$HOME/.singam/lib/cli"
exec dotnet "$SINGAM_LIB_DIR/SingamDB.CLI.dll" "$@"
EOF

    if [ "$TARGET_OS" = "win" ]; then
        cat << 'EOF' > "$INSTALL_DIR/singam-server.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\server\SingamDB.Server.dll" %*
EOF
        cat << 'EOF' > "$INSTALL_DIR/singam.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.CLI.dll" %*
EOF
        cat << 'EOF' > "$INSTALL_DIR/singam-cli.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.CLI.dll" %*
EOF
    fi

else
    echo -e "${YELLOW}[1/3] Downloading latest SingamDB binaries from online release...${NC}"
    
    # Release URL pattern
    LATEST_RELEASE="v3.0.0"
    TAR_URL="https://github.com/unknown001dk/singamdb/releases/download/${LATEST_RELEASE}/singamdb-${RID}.tar.gz"
    
    TEMP_DIR="$(mktemp -d)"
    
    # Attempt download or fallback to git clone & build if release asset is not yet populated
    if curl -fsSL "$TAR_URL" -o "$TEMP_DIR/singamdb.tar.gz" 2>/dev/null; then
        echo "Extracting release bundle..."
        tar -xzf "$TEMP_DIR/singamdb.tar.gz" -C "$LIB_DIR/"
    else
        echo -e "${YELLOW}Prebuilt tarball not yet published online. Cloning and building source directly...${NC}"
        if ! command -v git >/dev/null 2>&1; then
            echo -e "${RED}[ERROR] Git is required to install online.${NC}"
            exit 1
        fi
        
        TEMP_BUILD_DIR="$HOME/.singam/tmp_build"
        rm -rf "$TEMP_BUILD_DIR"
        mkdir -p "$TEMP_BUILD_DIR"
        
        echo "Cloning repository..."
        git clone --depth 1 https://github.com/unknown001dk/singamdb.git "$TEMP_BUILD_DIR"
        
        echo "Building SingamDB Server and CLI..."
        dotnet publish "$TEMP_BUILD_DIR/SingamDB.Server/SingamDB.Server.csproj" -c Release -o "$LIB_DIR/server" --nologo -v q
        dotnet publish "$TEMP_BUILD_DIR/src/SingamDB.CLI/SingamDB.CLI.csproj" -c Release -o "$LIB_DIR/cli" --nologo -v q
        
        cat << 'EOF' > "$INSTALL_DIR/singam-server"
#!/usr/bin/env bash
SINGAM_LIB_DIR="$HOME/.singam/lib/server"
exec dotnet "$SINGAM_LIB_DIR/SingamDB.Server.dll" "$@"
EOF

        cat << 'EOF' > "$INSTALL_DIR/singam"
#!/usr/bin/env bash
SINGAM_LIB_DIR="$HOME/.singam/lib/cli"
exec dotnet "$SINGAM_LIB_DIR/SingamDB.CLI.dll" "$@"
EOF
        
        # On Windows, also create .cmd wrappers
        if [ "$TARGET_OS" = "win" ]; then
            cat << 'EOF' > "$INSTALL_DIR/singam-server.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\server\SingamDB.Server.dll" %*
EOF
            cat << 'EOF' > "$INSTALL_DIR/singam.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.CLI.dll" %*
EOF
            cat << 'EOF' > "$INSTALL_DIR/singam-cli.cmd"
@echo off
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.CLI.dll" %*
EOF
        fi

        rm -rf "$TEMP_BUILD_DIR"
    fi
    
    rm -rf "$TEMP_DIR"
fi

# Make binaries executable
chmod +x "$INSTALL_DIR/singam-server"
chmod +x "$INSTALL_DIR/singam"
ln -sf "$INSTALL_DIR/singam" "$INSTALL_DIR/singam-cli"

echo -e "${YELLOW}[2/3] Setting up environment and PATH...${NC}"

# Add to PATH in Shell profile if not already present
PROFILE_FILES=("$HOME/.bashrc" "$HOME/.zshrc" "$HOME/.bash_profile" "$HOME/.profile")
PATH_ENTRY='export PATH="$HOME/.singam/bin:$PATH"'

UPDATED_PROFILE=false
for PROF in "${PROFILE_FILES[@]}"; do
    if [ -f "$PROF" ]; then
        if ! grep -q "$HOME/.singam/bin" "$PROF"; then
            echo "" >> "$PROF"
            echo "# SingamDB Database Path" >> "$PROF"
            echo "$PATH_ENTRY" >> "$PROF"
            UPDATED_PROFILE=true
            echo -e "  Added PATH to ${CYAN}${PROF}${NC}"
        fi
    fi
done

# Try creating symlink in /usr/local/bin if writable or with sudo
echo -e "${YELLOW}[3/3] Creating global system symlinks...${NC}"
if [ -w "$GLOBAL_BIN" ]; then
    ln -sf "$INSTALL_DIR/singam-server" "$GLOBAL_BIN/singam-server" 2>/dev/null || true
    ln -sf "$INSTALL_DIR/singam" "$GLOBAL_BIN/singam" 2>/dev/null || true
    ln -sf "$INSTALL_DIR/singam" "$GLOBAL_BIN/singam-cli" 2>/dev/null || true
    echo -e "  Created symlinks in ${CYAN}${GLOBAL_BIN}${NC}"
fi

echo ""
echo -e "${GREEN}===================================================================${NC}"
echo -e "${GREEN}${BOLD} [SUCCESS] SingamDB installed successfully!${NC}"
echo -e "${GREEN}===================================================================${NC}"
echo ""
echo -e "To start using SingamDB immediately:"
echo ""
echo -e "  1. Reload your shell:   ${BOLD}source ~/.zshrc${NC} (or ${BOLD}source ~/.bashrc${NC})"
echo -e "  2. Start DB Server:     ${BOLD}singam-server${NC}   (Listening on :7777 HTTP & :7778 Wire Protocol)"
echo -e "  3. Open Interactive CLI:${BOLD}singam${NC}"
echo ""
