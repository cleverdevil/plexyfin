# Plexyfin v0.6.0.0 Release Notes

## New Features

### Multi-Version Media Support
This release introduces comprehensive support for handling multiple versions of the same media content, addressing two key scenarios:

1. **Cross-Library Duplicates**: When the same movie exists in different libraries (e.g., "Movies HD" and "Movies 4K"), each version now maintains its own artwork and metadata.

2. **Same-Library Versions**: Multiple versions within the same library (Director's Cut, Theatrical Release, etc.) are now properly matched and handled without conflicts.

### Filesystem-Based Matching
- Implements intelligent file path matching to disambiguate between multiple versions
- Automatically handles different mount points between containerized Plex and Jellyfin servers
- Requires no configuration - works out of the box by comparing relative paths

## Improvements

### Enhanced Matching Logic
- Upgraded the Jellyfin item index to support multiple items per external ID
- When multiple matches are found, uses file path comparison to determine the correct match
- Falls back gracefully to existing behavior when file paths are unavailable

### Artwork Sync Optimization
- Prevents duplicate artwork processing when multiple Plex versions map to the same Jellyfin item
- Tracks processed items during sync to avoid redundant operations
- Improved logging for multi-version scenarios

## Technical Details

### New Components
- `PathMatcher.cs`: Utility class for intelligent path comparison
- Enhanced `PlexItem` model with `FilePath` property
- Modified `JellyfinItemIndex` to support multi-match scenarios

### Compatibility
- Maintains full backward compatibility with existing installations
- No configuration changes required
- Works with all existing Plex and Jellyfin server configurations

## Installation Notes

This version can be installed as a direct upgrade from any previous version. No migration or configuration changes are required.

## Bug Fixes

- Resolved issues where multiple versions of the same content would cause artwork sync conflicts
- Fixed incorrect matching when external IDs (IMDb, TMDb, TVDb) were shared across different versions

## Known Limitations

- Jellyfin's limitation: All versions of a movie in the same library share the same artwork (this is a Jellyfin platform limitation, not a Plexyfin issue)
- When multiple Plex versions exist for a single Jellyfin item, the first encountered version's artwork is used

## Contributors

Thanks to all contributors who helped identify and test the multi-version media scenarios that led to this enhancement.