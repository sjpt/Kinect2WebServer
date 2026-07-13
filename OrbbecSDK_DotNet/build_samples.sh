#!/bin/bash

set -e

# ==================== 0. Check dotnet version ====================
echo "======================================="
echo " Checking .NET SDK version..."
echo "======================================="

# Check if dotnet exists
if ! command -v dotnet >/dev/null 2>&1; then
    echo "Error: dotnet is not installed or not in PATH."
    echo "Please install .NET SDK 8.0 before running this script."
    exit 1
fi

# Get dotnet SDK version (e.g. 8.0.301)
DOTNET_VERSION=$(dotnet --version)
DOTNET_MAJOR=$(echo "$DOTNET_VERSION" | cut -d. -f1)

if [ "$DOTNET_MAJOR" -lt 8 ]; then
    echo "Error: Unsupported .NET SDK version detected: $DOTNET_VERSION"
    echo "This script requires .NET SDK 8.0 or later."
    exit 1
fi

echo "Detected .NET SDK version: $DOTNET_VERSION "

SOLUTION_FILE="OrbbecSharp.sln"
CONFIGURATION="Release"
BASE_OUTPUT_DIR="$(pwd)/bin"

# Parse command line arguments
if [ $# -ge 1 ]; then
    CONFIGURATION=$1
fi

if [ $# -ge 2 ]; then
    BASE_OUTPUT_DIR=$(realpath "$2")
fi

# ==================== 1. Detect current system architecture ====================
echo "======================================="
echo " Detecting system architecture..."
echo "======================================="

# Get current system architecture
CURRENT_ARCH=$(uname -m)
TARGET_RUNTIME=""

# Map system architecture to .NET runtime identifier
case "$CURRENT_ARCH" in
    x86_64)
        TARGET_RUNTIME="linux-x64"
        ;;
    aarch64)
        TARGET_RUNTIME="linux-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture '$CURRENT_ARCH'"
        echo "         Only linux-x64 (x86_64) and linux-arm64 (aarch64) are supported."
        exit 1
        ;;
esac

echo "Detected system architecture: $CURRENT_ARCH -> Target runtime: $TARGET_RUNTIME"

# ==================== 2. Remove existing bin directory (exit on failure) ====================
echo ""
echo "======================================="
echo " Cleaning up old output directory..."
echo "======================================="

if [ -d "$BASE_OUTPUT_DIR" ]; then
    echo "Removing existing directory: $BASE_OUTPUT_DIR"
    # Delete directory with rm -rf, exit with error if failed
    rm -rf "$BASE_OUTPUT_DIR" || {
        echo "Error: Failed to delete directory $BASE_OUTPUT_DIR"
        exit 1
    }
    echo "Directory removed successfully."
else
    echo "No existing bin directory found, skipping deletion."
fi

# ==================== 3. Execute publish operation ====================
OUTPUT_DIR="$BASE_OUTPUT_DIR/$CONFIGURATION/$TARGET_RUNTIME"

echo ""
echo "======================================="
echo " Publishing Solution"
echo "---------------------------------------"
echo " Solution:      $SOLUTION_FILE"
echo " Configuration: $CONFIGURATION"
echo " Target Runtime: $TARGET_RUNTIME"
echo " Output Dir:    $OUTPUT_DIR"
echo "======================================="

dotnet restore

# Clean solution first
dotnet clean "$SOLUTION_FILE" -c "$CONFIGURATION" > /dev/null

# Execute publish command with specified parameters
dotnet publish "$SOLUTION_FILE" \
    -c "$CONFIGURATION" \
    -r "$TARGET_RUNTIME" \
    -p:DebugType=none \
    -p:PublishSingleFile=true \
    --output "$OUTPUT_DIR"

# ==================== 4. Output completion information ====================
echo ""
echo "Publish completed successfully."
echo "Output files are located in:"
echo "   $OUTPUT_DIR"
echo "======================================="
