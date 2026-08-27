#!/usr/bin/env bash
set -e

echo "Uninstalling SingamDB from your system..."

# Remove local user directory
rm -rf "$HOME/.singam"

# Remove global binaries if present
if [ -w "/usr/local/bin" ]; then
    rm -f "/usr/local/bin/singam-server"
    rm -f "/usr/local/bin/singam"
    rm -f "/usr/local/bin/singam-cli"
    rm -rf "/usr/local/lib/singamdb"
else
    sudo rm -f "/usr/local/bin/singam-server" 2>/dev/null || true
    sudo rm -f "/usr/local/bin/singam" 2>/dev/null || true
    sudo rm -f "/usr/local/bin/singam-cli" 2>/dev/null || true
    sudo rm -rf "/usr/local/lib/singamdb" 2>/dev/null || true
fi

echo "SingamDB uninstalled successfully."
