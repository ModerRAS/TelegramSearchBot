#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="${ROOT_DIR}/nupkg"

echo "Building solution..."
dotnet build "${ROOT_DIR}/src/Moder.Update.sln" --configuration Release

echo "Packing Moder.Update..."
dotnet pack "${ROOT_DIR}/src/Moder.Update/Moder.Update.csproj" --configuration Release --output "${OUTPUT_DIR}" --no-build

echo ""
echo "Package created in: ${OUTPUT_DIR}"
ls -la "${OUTPUT_DIR}"/*.nupkg 2>/dev/null || echo "No .nupkg files found!"
