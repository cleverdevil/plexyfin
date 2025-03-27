#!/bin/bash
set -e

# Configuration
VERSION="0.0.0.1"
PLUGIN_ID="Plexyfin"
PLUGIN_NAME="Jellyfin.Plugin.Plexyfin"
OUTPUT_DIR="dist"
ZIP_NAME="${PLUGIN_ID}_${VERSION}.zip"

# Clean output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}"

# Build the plugin in publish mode (self-contained)
echo "Building plugin in publish mode..."
dotnet publish "$PLUGIN_NAME" \
  --configuration Release \
  --output "$OUTPUT_DIR/tmp" \
  --self-contained false \
  -p:DebugSymbols=false \
  -p:DebugType=none

# Copy only the plugin DLL (no dependencies)
echo "Copying files to output directory..."
cp "$OUTPUT_DIR/tmp/$PLUGIN_NAME.dll" "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/"

# Create meta.json
echo "Creating meta.json..."
cat > "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/meta.json" << EOF
{
  "id": "b9f0c474-e9a8-4292-ae41-eb3c1542f4cd",
  "name": "${PLUGIN_ID}",
  "description": "A Jellyfin plugin for creating collections in a Plex-like style",
  "overview": "Create and manage collections with Plex-inspired features",
  "version": "${VERSION}",
  "owner": "plexyfin",
  "category": "General",
  "targetAbi": "10.9.9.0",
  "framework": "net8.0",
  "status": "Active",
  "autoUpdate": true,
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%S.0000000Z")",
  "changelog": "Initial version"
}
EOF

# Create ZIP file
echo "Creating ZIP file..."
cd "$OUTPUT_DIR"
zip -r "$ZIP_NAME" "${PLUGIN_ID}_${VERSION}"
cd ..

# Clean up temporary files
rm -rf "$OUTPUT_DIR/tmp"

echo "Build complete: $OUTPUT_DIR/$ZIP_NAME"
echo "You can now install this plugin in Jellyfin by uploading the ZIP file."