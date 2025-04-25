# Release Process for Plexyfin

This document outlines the process for creating new releases of the Plexyfin plugin.

## Version Numbering

Plexyfin follows the Jellyfin plugin versioning scheme of `Major.Minor.Build.Revision`:

- **Major**: Significant changes that may require user intervention
- **Minor**: New features with backward compatibility
- **Build**: Bug fixes and minor improvements
- **Revision**: Small patches and hotfixes

Current version: **0.5.0.0**

## Creating a New Release

1. Update version number in:
   - `Jellyfin.Plugin.Plexyfin.csproj` (AssemblyVersion and FileVersion elements)
   - `Jellyfin.Plugin.Plexyfin/meta.json` (version field)
   - Any other version references in the code

2. Create and push a tag:
   ```bash
   git tag -a v0.5.0.0 -m "Release v0.5.0.0"
   git push origin v0.5.0.0
   ```

3. Create a new release on GitHub:
   - Go to https://github.com/cleverdevil/plexyfin/releases
   - Click "Draft a new release"
   - Select the tag you just created
   - Title: "Plexyfin v0.5.0.0"
   - Add release notes describing changes
   - Publish the release
   - Upload the built DLL as a release asset

4. Update the repository manifest:
   - Update `metadata/stable/manifest.json` with the new version info
   - Update the checksum with the SHA512 hash of the DLL
   - Update the timestamp and changelog
   - Commit and push the changes to main

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

1. Make sure the DLL is properly uploaded to the GitHub release
2. Verify that the version numbers and checksums are consistent
3. Check the manifest.json format using a JSON validator
4. Test the repository URL in Jellyfin directly
