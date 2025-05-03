#!/bin/bash
set -e

# Check for required parameter
if [ $# -lt 1 ]; then
  echo "Usage: ./new_version.sh <new_version>"
  echo "Example: ./new_version.sh 0.6.0.0"
  exit 1
fi

NEW_VERSION=$1
RELEASES_DIR="releases"

# Find the latest version
# Find the latest version from the releases directory
LATEST_VERSION=$(ls -1 $RELEASES_DIR/*.json 2>/dev/null | grep -v "template.json" | sort -V | tail -1 | sed 's/.*\/\(.*\)\.json/\1/')

NEW_RELEASE_FILE="$RELEASES_DIR/$NEW_VERSION.json"

# Check if the new version already exists
if [ -f "$NEW_RELEASE_FILE" ]; then
  echo "Error: Version $NEW_VERSION already exists at $NEW_RELEASE_FILE"
  exit 1
fi

# Use template.json if available, otherwise use the latest version
if [ -f "$RELEASES_DIR/template.json" ]; then
  echo "Creating new version $NEW_VERSION based on template"
  cp "$RELEASES_DIR/template.json" "$NEW_RELEASE_FILE"
  
  # Update version in the new file
  sed -i '' "s/\"version\": \"VERSION_NUMBER\"/\"version\": \"$NEW_VERSION\"/" "$NEW_RELEASE_FILE"
elif [ ! -z "$LATEST_VERSION" ]; then
  echo "Creating new version $NEW_VERSION based on $LATEST_VERSION"
  cp "$RELEASES_DIR/$LATEST_VERSION.json" "$NEW_RELEASE_FILE"
  
  # Update version in the new file
  sed -i '' "s/\"version\": \"$LATEST_VERSION\"/\"version\": \"$NEW_VERSION\"/" "$NEW_RELEASE_FILE"
else
  echo "Error: No template.json or existing versions found to base new version on."
  exit 1
fi

# Ask if the user wants to edit the changelog
if [ -t 0 ]; then  # Only if this is an interactive session
  echo ""
  echo "Do you want to update the changelog now? (y/n)"
  read -r EDIT_CHANGELOG
  
  if [ "$EDIT_CHANGELOG" == "y" ] || [ "$EDIT_CHANGELOG" == "Y" ]; then
    echo ""
    echo "Enter the changelog entries (one per line). Enter an empty line to finish:"
    CHANGELOG=""
    while true; do
      read -r LINE
      if [ -z "$LINE" ]; then
        break
      fi
      if [ -z "$CHANGELOG" ]; then
        CHANGELOG="- $LINE"
      else
        CHANGELOG="$CHANGELOG\\n- $LINE"
      fi
    done
    
    # Update changelog in the file
    if [ ! -z "$CHANGELOG" ]; then
      # Escape special characters for sed
      ESCAPED_CHANGELOG=$(echo "$CHANGELOG" | sed 's/[\/&]/\\&/g')
      sed -i '' "s/\"changelog\": \".*\"/\"changelog\": \"$ESCAPED_CHANGELOG\"/" "$NEW_RELEASE_FILE"
      echo "Changelog updated."
    fi
  fi
else
  echo "Please edit $NEW_RELEASE_FILE manually to update the changelog and other details."
fi

echo "Created version $NEW_VERSION"
echo ""
echo "To build this version, run:"
echo "./build_release.sh $NEW_VERSION"
echo ""
echo "To build and update the repository manifest, run:"
echo "./build_release.sh $NEW_VERSION --deploy"