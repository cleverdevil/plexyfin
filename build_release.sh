#!/bin/bash
set -e

# Check for required parameter
if [ $# -lt 1 ]; then
  echo "Usage: ./build_release.sh <version> [--deploy]"
  echo "Examples:"
  echo "  ./build_release.sh 0.5.0.0       # Build version 0.5.0.0"
  echo "  ./build_release.sh 0.6.0.0       # Create and build version 0.6.0.0"
  echo "  ./build_release.sh latest        # Build the latest version in releases/"
  echo "  ./build_release.sh 0.6.0.0 --deploy # Build and update repository manifest"
  exit 1
fi

DEPLOY=false
if [ "$2" == "--deploy" ]; then
  DEPLOY=true
fi

VERSION=$1
PLUGIN_ID="Plexyfin"
PLUGIN_NAME="Jellyfin.Plugin.Plexyfin"
OUTPUT_DIR="dist"
RELEASES_DIR="releases"

# If "latest" is specified, find the latest version
if [ "$VERSION" == "latest" ]; then
  VERSION=$(ls -1 $RELEASES_DIR/*.json | sort -V | tail -1 | sed 's/.*\/\(.*\)\.json/\1/')
  echo "Latest version is $VERSION"
fi

RELEASE_FILE="$RELEASES_DIR/$VERSION.json"

# Check if the version file exists
if [ ! -f "$RELEASE_FILE" ]; then
  echo "Release file $RELEASE_FILE does not exist."
  read -p "Would you like to create a new release based on the latest version? (y/n) " CREATE_NEW
  
  if [ "$CREATE_NEW" == "y" ]; then
    # Find the latest version
    LATEST_VERSION=$(ls -1 $RELEASES_DIR/*.json 2>/dev/null | sort -V | tail -1 | sed 's/.*\/\(.*\)\.json/\1/')
    
    if [ -z "$LATEST_VERSION" ]; then
      echo "No existing versions found to base new version on."
      exit 1
    fi
    
    echo "Creating new version $VERSION based on $LATEST_VERSION"
    cp "$RELEASES_DIR/$LATEST_VERSION.json" "$RELEASE_FILE"
    
    # Update version in the new file
    sed -i '' "s/\"version\": \"$LATEST_VERSION\"/\"version\": \"$VERSION\"/" "$RELEASE_FILE"
    
    echo "Created $RELEASE_FILE. Please edit to update changelog and other details."
    exit 0
  else
    echo "Aborting."
    exit 1
  fi
fi

# Extract data from the release file
echo "Building from release file: $RELEASE_FILE"
PLUGIN_GUID=$(grep -o '"guid": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
PLUGIN_DESCRIPTION=$(grep -o '"description": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
PLUGIN_OVERVIEW=$(grep -o '"overview": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
PLUGIN_OWNER=$(grep -o '"owner": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
PLUGIN_CATEGORY=$(grep -o '"category": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
TARGET_ABI=$(grep -o '"targetAbi": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)
CHANGELOG=$(grep -o '"changelog": "[^"]*"' "$RELEASE_FILE" | cut -d'"' -f4)

# Update meta.json file
echo "Updating meta.json..."
cat > "$PLUGIN_NAME/meta.json" << EOF
{
  "name": "$PLUGIN_ID",
  "id": "$PLUGIN_GUID",
  "guid": "$PLUGIN_GUID",
  "version": "$VERSION",
  "overview": "$PLUGIN_OVERVIEW",
  "description": "$PLUGIN_DESCRIPTION",
  "owner": "$PLUGIN_OWNER",
  "category": "$PLUGIN_CATEGORY",
  "targetAbi": "$TARGET_ABI",
  "changelog": "$CHANGELOG",
  "imageUrl": "https://raw.githubusercontent.com/plexyfin/plexyfin/main/plexyfin.png"
}
EOF

# Update assembly version in .csproj file
echo "Updating version in .csproj file..."
sed -i '' "s/<AssemblyVersion>.*<\/AssemblyVersion>/<AssemblyVersion>$VERSION<\/AssemblyVersion>/" "$PLUGIN_NAME/$PLUGIN_NAME.csproj"
sed -i '' "s/<FileVersion>.*<\/FileVersion>/<FileVersion>$VERSION<\/FileVersion>/" "$PLUGIN_NAME/$PLUGIN_NAME.csproj"

# Clean output directory for this version
ZIP_NAME="${PLUGIN_ID}_${VERSION}.zip"
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}"

# Build the plugin
echo "Building plugin in Release mode..."
dotnet publish "$PLUGIN_NAME" \
  --configuration Release \
  --output "$OUTPUT_DIR/tmp" \
  --self-contained false \
  -p:DebugSymbols=false \
  -p:DebugType=none \
  -p:TreatWarningsAsErrors=false

# Copy only the plugin DLL (no dependencies)
echo "Copying files to output directory..."
cp "$OUTPUT_DIR/tmp/$PLUGIN_NAME.dll" "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/"

# Copy meta.json to output
cp "$PLUGIN_NAME/meta.json" "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/"

# Create ZIP file
echo "Creating ZIP file..."
cd "$OUTPUT_DIR"
zip -r "$ZIP_NAME" "${PLUGIN_ID}_${VERSION}"
cd ..

# Calculate checksum
if [ "$(uname)" == "Darwin" ]; then
  # macOS
  MD5_CHECKSUM=$(md5 -q "$OUTPUT_DIR/$ZIP_NAME")
  SHA512_CHECKSUM=$(shasum -a 512 "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/$PLUGIN_NAME.dll" | awk '{ print $1 }')
else
  # Linux
  MD5_CHECKSUM=$(md5sum "$OUTPUT_DIR/$ZIP_NAME" | awk '{print $1}')
  SHA512_CHECKSUM=$(sha512sum "$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}/$PLUGIN_NAME.dll" | awk '{ print $1 }')
fi

echo "MD5 checksum: $MD5_CHECKSUM"
echo "SHA512 checksum: $SHA512_CHECKSUM"

# Clean up temporary files
rm -rf "$OUTPUT_DIR/tmp"

# Copy ZIP to root for easy access
cp "$OUTPUT_DIR/$ZIP_NAME" .

echo "Build complete: $OUTPUT_DIR/$ZIP_NAME"
echo "Plugin zip is also available at: ./$ZIP_NAME"

# Update repository if deploy flag is set
if [ "$DEPLOY" = true ]; then
  echo "Updating repository manifest..."
  
  # Prepare paths
  REPO_DIR="metadata/stable"
  MANIFEST_FILE="$REPO_DIR/manifest.json"
  TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  
  # Check if manifest exists and parse it
  if [ -f "$MANIFEST_FILE" ]; then
    echo "Updating existing manifest..."
    
    # Check if jq is installed
    if ! command -v jq &> /dev/null; then
      echo "Warning: jq is not installed. This is recommended for proper JSON manipulation."
      echo "Please install jq or manually update the manifest."
      
      # Manual instructions
      echo ""
      echo "Add this version entry to $MANIFEST_FILE under the versions array:"
      cat << EOF
{
  "version": "$VERSION",
  "changelog": "$CHANGELOG",
  "targetAbi": "$TARGET_ABI",
  "sourceUrl": "https://github.com/cleverdevil/plexyfin/releases/download/v$VERSION/$ZIP_NAME",
  "checksum": "$MD5_CHECKSUM",
  "timestamp": "$TIMESTAMP"
}
EOF
    else
      # Use jq to add the new version to the manifest
      # Extract the current content
      MANIFEST_CONTENT=$(cat "$MANIFEST_FILE")
      
      # Create a temp file and update it with jq
      TMP_FILE=$(mktemp)
      
      # Complex jq command to insert new version at beginning of versions array
      # This preserves existing versions and keeps the latest one at the top
      echo "$MANIFEST_CONTENT" | jq ".[0].versions = [{
        \"version\": \"$VERSION\",
        \"changelog\": \"$CHANGELOG\",
        \"targetAbi\": \"$TARGET_ABI\",
        \"sourceUrl\": \"https://github.com/cleverdevil/plexyfin/releases/download/v$VERSION/$ZIP_NAME\",
        \"checksum\": \"$MD5_CHECKSUM\",
        \"timestamp\": \"$TIMESTAMP\"
      }] + .[0].versions" > "$TMP_FILE"
      
      # Replace the original file
      mv "$TMP_FILE" "$MANIFEST_FILE"
      echo "Updated manifest with version $VERSION"
    fi
  else
    echo "Creating new manifest..."
    
    # Create the manifest file with the initial version
    mkdir -p "$REPO_DIR"
    cat > "$MANIFEST_FILE" << EOF
[
  {
    "guid": "$PLUGIN_GUID",
    "name": "$PLUGIN_ID",
    "description": "$PLUGIN_DESCRIPTION",
    "overview": "$PLUGIN_OVERVIEW",
    "imageUrl": "https://raw.githubusercontent.com/cleverdevil/plexyfin/main/metadata/stable/banner.jpg",
    "owner": "cleverdevil",
    "category": "Metadata",
    "versions": [
      {
        "version": "$VERSION",
        "changelog": "$CHANGELOG",
        "targetAbi": "$TARGET_ABI",
        "sourceUrl": "https://github.com/cleverdevil/plexyfin/releases/download/v$VERSION/$ZIP_NAME",
        "checksum": "$MD5_CHECKSUM",
        "timestamp": "$TIMESTAMP"
      }
    ]
  }
]
EOF
    echo "Created new manifest with version $VERSION"
  fi
  
  # Create Git tag for the version
  if git rev-parse "v$VERSION" >/dev/null 2>&1; then
    echo "Git tag v$VERSION already exists."
  else
    echo "Creating Git tag v$VERSION..."
    git tag -a "v$VERSION" -m "Release version $VERSION"
    echo "Git tag created. To push the tag to remote, run:"
    echo "git push origin v$VERSION"
  fi

  echo "Repository update complete."
  echo "Next steps:"
  echo "1. Commit changes: git add . && git commit -m \"Release version $VERSION\""
  echo "2. Push changes: git push"
  echo "3. Push tag: git push origin v$VERSION"
  echo "4. Create GitHub release at: https://github.com/cleverdevil/plexyfin/releases/new?tag=v$VERSION"
fi

echo "Build process completed successfully."