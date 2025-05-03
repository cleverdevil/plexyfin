# Build and Release Process

This document explains the build and release process for the Plexyfin plugin.

## Overview

The build system uses a "releases" directory as the single source of truth for versioning. Each version has its own JSON file in this directory, which contains all the metadata for that release.

## Directory Structure

- `releases/`: Contains JSON files for each version (e.g., `0.5.0.0.json`, `0.6.0.0.json`)
- `dist/`: Output directory for builds
- `metadata/stable/`: Repository metadata for the Jellyfin plugin catalog

## Build Scripts

The following scripts handle the build and release process:

- `new_version.sh` - Creates new versions
- `build_release.sh` - Builds and packages a version
- `tag_and_release.sh` - Tags and pushes releases to Git
- `prepare_github_release.sh` - Prepares files for GitHub releases

### Creating a New Version

To create a new version:

```bash
./new_version.sh <new_version>
```

For example:
```bash
./new_version.sh 0.6.0.0
```

This will:
1. Create a new JSON file in the `releases` directory based on the latest version
2. Update the version number in the file
3. Open the file in an editor so you can update the changelog and other details

### Building a Version

To build a specific version:

```bash
./build_release.sh <version>
```

For example:
```bash
./build_release.sh 0.5.0.0
```

This will:
1. Read the version metadata from the corresponding file in the `releases` directory
2. Update `meta.json` and the `.csproj` file with the correct version
3. Build the plugin
4. Create a ZIP file for distribution
5. Calculate checksums

### Building and Updating the Repository

To build a version and update the repository manifest:

```bash
./build_release.sh <version> --deploy
```

This will perform the build steps and also:
1. Update the `metadata/stable/manifest.json` file with the new version
2. Preserve all existing versions in the manifest
3. Create a Git tag for the version (but not push it to remote)

### Tagging and Pushing a Release

To create a Git tag for a version and optionally push it to the remote repository:

```bash
# Tag only
./tag_and_release.sh <version>

# Tag and push to remote
./tag_and_release.sh <version> --push
```

This will:
1. Check if the version exists and has been built
2. Commit any uncommitted changes if desired
3. Create a Git tag for the version
4. Optionally push the tag and commits to the remote repository

### Preparing a GitHub Release

After building and tagging a version, prepare files for a GitHub release:

```bash
./prepare_github_release.sh <version>
```

This will:
1. Copy the necessary files to a `github_release` directory
2. Generate release notes based on the changelog
3. Provide instructions for creating a GitHub release

## Using the Latest Version

To build the latest version available in the `releases` directory:

```bash
./build_release.sh latest
```

## Workflow for a New Release

1. Create a new version: `./new_version.sh 0.6.0.0`
2. Edit the changelog and other details in the generated file
3. Build and test locally: `./build_release.sh 0.6.0.0`
4. When ready to release, update the repository: `./build_release.sh 0.6.0.0 --deploy`
5. Tag and push the release: `./tag_and_release.sh 0.6.0.0 --push`
6. Prepare for GitHub release: `./prepare_github_release.sh 0.6.0.0`
7. Create a GitHub release using the prepared files

## Tips and Best Practices

- Always use the scripts to manage versions to ensure consistency
- The `releases` directory serves as the version history of the plugin
- When modifying the build scripts, ensure they continue to use the `releases` directory as the source of truth
- For automated CI/CD pipelines, use `./build_release.sh latest --deploy`

