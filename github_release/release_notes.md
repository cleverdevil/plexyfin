# Plexyfin 0.5.3.0

## Changelog
- Fixed scheduled sync not working when collection sync is disabled (issue #6)
- Added notification to restart Jellyfin when scheduled sync settings change
- Improved scheduled sync configuration UI with helpful instructions
- Use external IDs for more reliable Plex-Jellyfin matching (issue #7)
- Removed unused Jellyfin Base URL configuration option
- Fixed scheduled sync note visibility in dark themes

## Installation
Download the ZIP file and install it in Jellyfin via Dashboard > Plugins > Upload.

## Manual Installation
If you prefer to install manually, copy the DLL file to your Jellyfin plugins directory.

## Important Notes
- After changing scheduled sync settings, you must restart Jellyfin for the changes to take effect
- External ID matching now provides more reliable Plex-Jellyfin content matching using TMDb and IMDb identifiers