#!/bin/bash

# Script to generate and update the Jellyfin plugin repository

# Configuration
PLUGIN_NAME="Plexyfin"
PLUGIN_GUID="eb5d7894-8eef-4b36-aa6f-5d124e828ce1"
PLUGIN_OWNER="cleverdevil"
PLUGIN_CATEGORY="Metadata"
REPO_URL="https://cleverdevil.github.io/plexyfin"
DOWNLOAD_URL="https://github.com/cleverdevil/plexyfin/releases/download"
TARGET_ABI="10.9.9.0"

# Paths
REPO_DIR="repository"
VERSIONS_DIR="$REPO_DIR/versions/plexyfin"
PLUGIN_DLL="dist/Plexyfin_0.4.0.0/Jellyfin.Plugin.Plexyfin.dll"

# Check if plugin DLL exists
if [ ! -f "$PLUGIN_DLL" ]; then
    echo "Error: Plugin DLL not found at $PLUGIN_DLL"
    echo "Please build the plugin first."
    exit 1
fi

# Set the version
VERSION="0.4.0.0"

# Generate SHA512 checksum
CHECKSUM=$(shasum -a 512 "$PLUGIN_DLL" | awk '{ print $1 }')

# Create version JSON file
cat > "$VERSIONS_DIR/$VERSION.json" << EOF
{
  "name": "$PLUGIN_NAME",
  "version": "$VERSION",
  "targetAbi": "$TARGET_ABI",
  "changelog": "See GitHub releases for changelog",
  "description": "Sync collections and artwork from Plex to Jellyfin",
  "overview": "A plugin that synchronizes collections and artwork from your Plex Media Server to Jellyfin",
  "owner": "$PLUGIN_OWNER",
  "category": "$PLUGIN_CATEGORY",
  "artifacts": [
    {
      "filename": "Jellyfin.Plugin.Plexyfin.dll",
      "url": "$DOWNLOAD_URL/v$VERSION/Jellyfin.Plugin.Plexyfin.dll",
      "checksum": "$CHECKSUM"
    }
  ]
}
EOF

echo "Created version file: $VERSIONS_DIR/$VERSION.json"

# Update manifest.json with the new version
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# Check if manifest exists
if [ ! -f "$REPO_DIR/manifest.json" ]; then
    # Create new manifest
    cat > "$REPO_DIR/manifest.json" << EOF
{
  "name": "$PLUGIN_NAME Plugin Repository",
  "description": "Repository for the $PLUGIN_NAME plugin that syncs collections from Plex to Jellyfin",
  "url": "$REPO_URL",
  "plugins": [
    {
      "guid": "$PLUGIN_GUID",
      "name": "$PLUGIN_NAME",
      "description": "Sync collections and artwork from Plex to Jellyfin",
      "overview": "A plugin that synchronizes collections and artwork from your Plex Media Server to Jellyfin",
      "owner": "$PLUGIN_OWNER",
      "category": "$PLUGIN_CATEGORY",
      "versions": [
        {
          "version": "$VERSION",
          "changelog": "See GitHub releases for changelog",
          "targetAbi": "$TARGET_ABI",
          "sourceUrl": "$REPO_URL/versions/plexyfin/$VERSION.json",
          "checksum": "$CHECKSUM",
          "timestamp": "$TIMESTAMP"
        }
      ]
    }
  ]
}
EOF
    echo "Created new manifest.json"
else
    # TODO: Update existing manifest with new version
    echo "Manifest exists. Manual update required for now."
    echo "Please add the following version entry to the manifest.json file:"
    cat << EOF
{
  "version": "$VERSION",
  "changelog": "See GitHub releases for changelog",
  "targetAbi": "$TARGET_ABI",
  "sourceUrl": "$REPO_URL/versions/plexyfin/$VERSION.json",
  "checksum": "$CHECKSUM",
  "timestamp": "$TIMESTAMP"
}
EOF
fi

echo "Repository files generated successfully."
echo "Don't forget to upload the DLL to: $DOWNLOAD_URL/Jellyfin.Plugin.Plexyfin_$VERSION.dll"
