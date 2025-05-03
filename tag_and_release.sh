#!/bin/bash
set -e

# Check for required parameter
if [ $# -lt 1 ]; then
  echo "Usage: ./tag_and_release.sh <version> [--push]"
  echo "Examples:"
  echo "  ./tag_and_release.sh 0.6.0.0       # Tag version 0.6.0.0 locally"
  echo "  ./tag_and_release.sh 0.6.0.0 --push # Tag and push to remote"
  exit 1
fi

VERSION=$1
PUSH=false

if [ "$2" == "--push" ]; then
  PUSH=true
fi

# Verify that the version exists in releases directory
RELEASES_DIR="releases"
RELEASE_FILE="$RELEASES_DIR/$VERSION.json"

if [ ! -f "$RELEASE_FILE" ]; then
  echo "Error: Release file $RELEASE_FILE does not exist."
  echo "Please create the version first with ./new_version.sh $VERSION"
  exit 1
fi

# Verify the version is built
OUTPUT_DIR="dist"
PLUGIN_ID="Plexyfin"
ZIP_FILE="$OUTPUT_DIR/${PLUGIN_ID}_${VERSION}.zip"

if [ ! -f "$ZIP_FILE" ]; then
  echo "Warning: Build output $ZIP_FILE not found."
  echo "It is recommended to build the version first with:"
  echo "./build_release.sh $VERSION"
  
  read -p "Continue anyway? (y/n) " CONTINUE
  if [ "$CONTINUE" != "y" ]; then
    exit 1
  fi
fi

# Make sure all changes are committed
if [ -n "$(git status --porcelain)" ]; then
  echo "Warning: There are uncommitted changes in the repository."
  git status --short
  
  read -p "Commit all changes with message 'Release version $VERSION'? (y/n) " COMMIT
  if [ "$COMMIT" == "y" ]; then
    git add .
    git commit -m "Release version $VERSION"
    echo "Changes committed."
  else
    echo "Aborting. Please commit your changes manually before tagging."
    exit 1
  fi
fi

# Create tag
if git rev-parse "v$VERSION" >/dev/null 2>&1; then
  echo "Git tag v$VERSION already exists."
  
  read -p "Delete existing tag and recreate? (y/n) " RETAG
  if [ "$RETAG" == "y" ]; then
    git tag -d "v$VERSION"
    echo "Deleted existing tag v$VERSION."
  else
    echo "Using existing tag."
  fi
fi

if ! git rev-parse "v$VERSION" >/dev/null 2>&1; then
  echo "Creating Git tag v$VERSION..."
  git tag -a "v$VERSION" -m "Release version $VERSION"
  echo "Git tag created."
fi

# Push to remote if requested
if [ "$PUSH" = true ]; then
  echo "Pushing commits to remote..."
  git push
  
  echo "Pushing tag to remote..."
  git push origin "v$VERSION"
  
  echo "Tag and commits pushed to remote."
  echo ""
  echo "You can now create a GitHub release at:"
  echo "https://github.com/cleverdevil/plexyfin/releases/new?tag=v$VERSION"
  echo ""
  echo "To prepare release files, run:"
  echo "./prepare_github_release.sh $VERSION"
else
  echo "Tag created locally but not pushed to remote."
  echo ""
  echo "To push changes and tag, run:"
  echo "git push && git push origin v$VERSION"
  echo ""
  echo "To prepare release files, run:"
  echo "./prepare_github_release.sh $VERSION"
fi