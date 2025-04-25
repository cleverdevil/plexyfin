# Release Process for Plexyfin

This document outlines the simplified manual process for creating new releases of the Plexyfin plugin.

## Version Numbering

Plexyfin follows the Jellyfin plugin versioning scheme of `Major.Minor.Build.Revision`:

- **Major**: Significant changes that may require user intervention
- **Minor**: New features with backward compatibility
- **Build**: Bug fixes and minor improvements
- **Revision**: Small patches and hotfixes

Current version: **0.5.0.0**

## Creating a New Release

1. Update version numbers in the following files:
   - `Jellyfin.Plugin.Plexyfin.csproj`: Update AssemblyVersion and FileVersion elements
   - `Jellyfin.Plugin.Plexyfin/meta.json`: Update version field
   - Any other version references in the code

2. Commit and push these changes:
   ```bash
   git add Jellyfin.Plugin.Plexyfin.csproj Jellyfin.Plugin.Plexyfin/meta.json
   git commit -m "Bump version to X.Y.Z.0"
   git push
   ```

3. Create and push a tag:
   ```bash
   git tag -a vX.Y.Z.0 -m "Release vX.Y.Z.0"
   git push origin vX.Y.Z.0
   ```

4. Build the plugin package:
   ```bash
   ./build_plugin_package.sh
   ```
   This will:
   - Build the plugin
   - Create a ZIP file with the plugin DLL and meta.json
   - Calculate and display the MD5 checksum of the ZIP

5. Update the repository manifest:
   - Edit `metadata/stable/manifest.json` with the new version info
   - Update the sourceUrl to point to the ZIP file that will be uploaded in the release
   - Update the checksum with the MD5 hash of the ZIP shown by the build script
   - Update the timestamp and changelog
   - Commit and push these changes:
     ```bash
     git add metadata/stable/manifest.json
     git commit -m "Update manifest for version X.Y.Z.0"
     git push
     ```

6. Create a new release on GitHub:
   - Go to https://github.com/cleverdevil/plexyfin/releases
   - Click "Draft a new release"
   - Select the tag you created earlier
   - Title: "Plexyfin vX.Y.Z.0"
   - Add release notes describing changes
   - Upload the ZIP package as a release asset
   - Publish the release

## Testing a Release

After publishing a release:

1. Verify that the manifest file is updated on GitHub
2. Add the repository to Jellyfin and check if the plugin appears in the catalog:
   ```
   https://raw.githubusercontent.com/cleverdevil/plexyfin/main/metadata/stable/manifest.json
   ```
3. Install the plugin and test its functionality

## Troubleshooting

If the release process fails:

1. Make sure the ZIP file is properly uploaded to the GitHub release
2. Verify that the version numbers and checksums are consistent
3. Check that the sourceUrl in the manifest correctly points to the ZIP file on GitHub
4. Test the repository URL in Jellyfin directly
