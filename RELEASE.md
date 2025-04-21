# Release Process for Plexyfin

This document outlines the process for creating new releases of the Plexyfin plugin.

## Version Numbering

Plexyfin follows the Jellyfin plugin versioning scheme of `Major.Minor.Build.Revision`:

- **Major**: Significant changes that may require user intervention
- **Minor**: New features with backward compatibility
- **Build**: Bug fixes and minor improvements
- **Revision**: Small patches and hotfixes

Current version: **0.4.0.0**

## Creating a New Release

1. Update version number in:
   - `Jellyfin.Plugin.Plexyfin.csproj` (AssemblyVersion and Version elements)
   - Any other version references in the code

2. Create and push a tag:
   ```bash
   git tag -a v0.4.0.0 -m "Release v0.4.0.0"
   git push origin v0.4.0.0
   ```

3. Create a new release on GitHub:
   - Go to https://github.com/cleverdevil/plexyfin/releases
   - Click "Draft a new release"
   - Select the tag you just created
   - Title: "Plexyfin v0.4.0.0"
   - Add release notes describing changes
   - Publish the release

4. The GitHub Actions workflow will:
   - Build the plugin
   - Upload the DLL to the release
   - Update the repository files
   - Deploy to GitHub Pages

## Testing a Release

After publishing a release:

1. Wait for the GitHub Actions workflow to complete
2. Verify that the repository files are updated on GitHub Pages
3. Add the repository to Jellyfin and check if the plugin appears in the catalog
4. Install the plugin and test its functionality

## Troubleshooting

If the release process fails:

1. Check the GitHub Actions logs for errors
2. Verify that the version numbers are consistent
3. Make sure the GitHub Pages branch (gh-pages) is properly configured
4. Test the repository URL in Jellyfin
