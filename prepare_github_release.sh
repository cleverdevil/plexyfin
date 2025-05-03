#!/bin/bash
set -e

# Check for required parameter
if [ $# -lt 1 ]; then
  echo "Usage: ./prepare_github_release.sh <version>"
  echo "Example: ./prepare_github_release.sh 0.5.0.0"
  exit 1
fi

VERSION=$1
RELEASES_DIR="releases"
RELEASE_FILE="$RELEASES_DIR/$VERSION.json"
OUTPUT_DIR="dist"
PLUGIN_ID="Plexyfin"
PLUGIN_NAME="Jellyfin.Plugin.Plexyfin"
GITHUB_RELEASE_DIR="github_release"

# Check if the version file exists
if [ ! -f "$RELEASE_FILE" ]; then
  echo "Error: Release file $RELEASE_FILE does not exist."
  exit 1
fi

# Check if the build exists
ZIP_FILE="${PLUGIN_ID}_${VERSION}.zip"
DLL_FILE="$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/$PLUGIN_NAME.dll"

if [ ! -f "$OUTPUT_DIR/$ZIP_FILE" ] || [ ! -f "$DLL_FILE" ]; then
  echo "Error: Build files not found. Please run ./build_release.sh $VERSION first."
  exit 1
fi

# Extract changelog from the release file
CHANGELOG=$(grep -o '"changelog": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4 | sed 's/\\n/\n/g')

# Prepare GitHub release files
mkdir -p "$GITHUB_RELEASE_DIR"

# Copy the DLL and ZIP for GitHub release
cp "$DLL_FILE" "$GITHUB_RELEASE_DIR/"
cp "$OUTPUT_DIR/$ZIP_FILE" "$GITHUB_RELEASE_DIR/"

# Generate release notes
cat > "$GITHUB_RELEASE_DIR/release_notes.md" << EOF
# Plexyfin $VERSION

## Changelog
$CHANGELOG

## Installation
Download the ZIP file and install it in Jellyfin via Dashboard > Plugins > Upload.

## Manual Installation
If you prefer to install manually, copy the DLL file to your Jellyfin plugins directory.
EOF

echo "GitHub release files prepared in: $GITHUB_RELEASE_DIR/"
echo ""
echo "To create a GitHub release:"
echo "1. Go to: https://github.com/cleverdevil/plexyfin/releases/new?tag=v$VERSION"
echo "2. Set the title to: Plexyfin $VERSION"
echo "3. Copy the content from $GITHUB_RELEASE_DIR/release_notes.md"
echo "4. Upload the files from the $GITHUB_RELEASE_DIR directory"
echo "5. Click 'Publish release'"