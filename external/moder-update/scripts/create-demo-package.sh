#!/bin/bash
# Usage: ./create-demo-package.sh <from_ver> <to_ver> <source_dir> <output_dir>
# Example: ./create-demo-package.sh 1.0.0 1.1.0 ./demo-app ./demo-packages
#
# This script creates a Moder.Update test package using the demo app's --create-package command.
# Prerequisites:
#   - .NET 10.0 SDK installed
#   - Moder.Update.Demo project built
#
# The script:
#   1. Creates output directory
#   2. Runs the demo with --create-package to generate the .zst package and catalog.json

set -e

if [ $# -ne 4 ]; then
    echo "Usage: $0 <from_ver> <to_ver> <source_dir> <output_dir>"
    echo "Example: $0 1.0.0 1.1.0 ./demo-app ./demo-packages"
    exit 1
fi

FROM_VER="$1"
TO_VER="$2"
SOURCE_DIR="$3"
OUTPUT_DIR="$4"

# Resolve paths
SOURCE_DIR="$(cd "$SOURCE_DIR" && pwd)"
OUTPUT_DIR="$(mkdir -p "$OUTPUT_DIR" && cd "$OUTPUT_DIR" && pwd)"

DEMO_DIR="$(cd "$(dirname "$0")/../src/Moder.Update.Demo" && pwd)"

echo "Creating update package: $FROM_VER -> $TO_VER"
echo "Source: $SOURCE_DIR"
echo "Output: $OUTPUT_DIR"

# Run the demo's --create-package command
dotnet run --project "$DEMO_DIR/Moder.Update.Demo.csproj" -- \
    --create-package "$FROM_VER" "$TO_VER" "$SOURCE_DIR" "$OUTPUT_DIR"

echo ""
echo "Package created successfully!"
echo "  Package: $OUTPUT_DIR/update-$FROM_VER-to-$TO_VER.zst"
echo "  Catalog: $OUTPUT_DIR/catalog.json"
echo ""
echo "To test the update:"
echo "  1. Copy demo app to a test directory"
echo "  2. Run: dotnet run --project $DEMO_DIR -- --apply"
echo "  3. The app will update and restart"
