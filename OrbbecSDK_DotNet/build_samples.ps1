# Stop on any error
$ErrorActionPreference = "Stop"

# ==================== 0. Check dotnet version ====================
Write-Host "======================================="
Write-Host " Checking .NET SDK version..."
Write-Host "======================================="

# Check if dotnet exists
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Error "dotnet is not installed or not in PATH. Please install .NET SDK 8.0 or later."
    exit 1
}

# Get dotnet version (e.g. 8.0.301)
$dotnetVersion = & dotnet --version
$majorVersion = [int]($dotnetVersion.Split('.')[0])

if ($majorVersion -lt 8) {
    Write-Error "Unsupported .NET SDK version detected: $dotnetVersion. This script requires .NET SDK 8.0 or later."
    exit 1
}

Write-Host "Detected .NET SDK version: $dotnetVersion "

# ==================== Basic configuration ====================
$SOLUTION_FILE = "OrbbecSharp.sln"
$CONFIGURATION = "Release"
$BASE_OUTPUT_DIR = Join-Path (Get-Location) "bin"
$TARGET_RUNTIME = "win-x64"

# ==================== Parse command line arguments ====================
if ($args.Length -ge 1) {
    $CONFIGURATION = $args[0]
}

if ($args.Length -ge 2) {
    $BASE_OUTPUT_DIR = (Resolve-Path $args[1]).Path
}

# ==================== 1. Clean existing bin directory ====================
Write-Host "======================================="
Write-Host " Cleaning up old output directory..."
Write-Host "======================================="

if (Test-Path $BASE_OUTPUT_DIR) {
    Write-Host "Removing existing directory: $BASE_OUTPUT_DIR"
    Remove-Item -Recurse -Force $BASE_OUTPUT_DIR
    Write-Host "Directory removed successfully."
}
else {
    Write-Host "No existing bin directory found, skipping deletion."
}

# ==================== 2. Execute publish operation ====================
$OUTPUT_DIR = Join-Path $BASE_OUTPUT_DIR "$CONFIGURATION\$TARGET_RUNTIME"

Write-Host ""
Write-Host "======================================="
Write-Host " Publishing Solution"
Write-Host "---------------------------------------"
Write-Host " Solution:       $SOLUTION_FILE"
Write-Host " Configuration:  $CONFIGURATION"
Write-Host " Target Runtime: $TARGET_RUNTIME"
Write-Host " Output Dir:     $OUTPUT_DIR"
Write-Host "======================================="

dotnet restore

# Clean solution first
dotnet clean $SOLUTION_FILE -c $CONFIGURATION | Out-Null

# Execute publish command
dotnet publish $SOLUTION_FILE `
    -c $CONFIGURATION `
    -r $TARGET_RUNTIME `
    -p:DebugType=none `
    -p:PublishSingleFile=true `
    --output $OUTPUT_DIR

# ==================== 3. Completion info ====================
Write-Host ""
Write-Host "Publish completed successfully."
Write-Host "Output files are located in:"
Write-Host "   $OUTPUT_DIR"
Write-Host "======================================="
