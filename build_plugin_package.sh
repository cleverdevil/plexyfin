#!/bin/bash

# Exit on error
set -e

# Get version from meta.json
VERSION=$(grep -o '"version": "[^"]*"' Jellyfin.Plugin.Plexyfin/meta.json | cut -d'"' -f4)
echo "Building package for version $VERSION"

# Check if we already have the DLL
if [ ! -f "Jellyfin.Plugin.Plexyfin/bin/Release/net8.0/Jellyfin.Plugin.Plexyfin.dll" ]; then
    echo "Building Plexyfin plugin..."
    dotnet build Jellyfin.Plugin.Plexyfin -c Release
fi

# Create package directory
PACKAGE_DIR="package"
rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR"

# Copy DLL to package directory
cp Jellyfin.Plugin.Plexyfin/bin/Release/net8.0/Jellyfin.Plugin.Plexyfin.dll "$PACKAGE_DIR/"

# Copy meta.json to package directory
cp Jellyfin.Plugin.Plexyfin/meta.json "$PACKAGE_DIR/"

# Create ZIP
ZIP_NAME="plexyfin_${VERSION}.zip"
rm -f "$ZIP_NAME"
cd "$PACKAGE_DIR"
zip -r "../$ZIP_NAME" ./*
cd ..

# Calculate MD5 checksum
if [ "$(uname)" == "Darwin" ]; then
    # macOS
    CHECKSUM=$(md5 -q "$ZIP_NAME")
else
    # Linux
    CHECKSUM=$(md5sum "$ZIP_NAME" | awk '{print $1}')
fi

echo "Package created: $ZIP_NAME"
echo "MD5 checksum: $CHECKSUM"

# Cleanup
rm -rf "$PACKAGE_DIR"

echo "Done!"