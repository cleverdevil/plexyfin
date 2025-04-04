# Plexyfin Plugin for Jellyfin

A plugin for Jellyfin that syncs collections and artwork from your Plex Media Server to Jellyfin.

## Features

- Sync collections from Plex to Jellyfin with their artwork
- Schedule automatic synchronization at configurable intervals
- Support for movies and TV shows in collections
- Direct filesystem access for reliable artwork synchronization
- Selective synchronization of specific Plex libraries
- Performance optimizations with configurable debug mode
- Error handling for improved reliability
- Watch state synchronization between Plex and Jellyfin
- Dry run mode to preview changes before applying them

## Installation

### Method 1: Easy Installation (Recommended)

1. In Jellyfin, go to Dashboard → Plugins → Repositories
2. Add a new repository with the following URL:
   `https://cleverdevil.github.io/plexyfin/manifest.json`
3. Go to the Catalog tab
4. Find Plexyfin in the list and click Install
5. Restart Jellyfin when prompted
6. Configure the plugin as described in the Usage section

### Method 2: Manual Installation

1. Download the latest release from the [Releases page](https://github.com/cleverdevil/plexyfin/releases)
2. Extract the zip file to your Jellyfin plugins directory
3. Restart Jellyfin
4. Enable the plugin in the Jellyfin dashboard

## Usage

1. Go to the Jellyfin dashboard
2. Navigate to Plugins
3. Click on Plexyfin
4. Configure your Plex server settings:
   - Plex Server URL (e.g., http://192.168.1.100:32400)
   - Plex API Token (from your Plex account)
   - Click "Test Connection" to verify and fetch available libraries
   - Select which Plex libraries should be included in synchronization
5. Configure sync options:
   - Enable collection sync
   - Enable artwork sync
   - Enable watch state sync with configurable direction
   - Set scheduled sync interval if desired
6. Click "Save"
7. Run a manual sync by clicking "Sync from Plex"

## Advanced Configuration

The plugin includes several advanced options that can be configured:

- **EnableDebugMode**: When enabled, saves debug copies of images and provides more verbose logging
- **MaxUrlPatternAttempts**: Controls how many URL patterns to try when fetching collection items (default: 3)
- **SyncWatchStateDirection**: Controls how watch states are synchronized:
  - **Bidirectional** (default): If content is marked as watched in either system, it will be marked as watched in both. For in-progress content, the furthest playback position is used.
  - **PlexToJellyfin**: One-way sync from Plex to Jellyfin
  - **JellyfinToPlex**: One-way sync from Jellyfin to Plex

## Environment Variables

- `JELLYFIN_DATA_PATH`: Override the default data path (/config/data) for Jellyfin installations with non-standard paths

## Development

### Prerequisites

- .NET 8.0 SDK (or .NET 6.0 SDK for older Jellyfin versions)
- Jellyfin instance for testing

### Building

```bash
# Using the build script
./build.sh

# Or manually
cd Jellyfin.Plugin.Plexyfin
dotnet build
```

### Installation

1. Build the plugin
2. Copy the resulting DLL from `Jellyfin.Plugin.Plexyfin/bin/Debug/net6.0/Jellyfin.Plugin.Plexyfin.dll` to your Jellyfin plugins directory
3. Restart Jellyfin
4. Go to the Jellyfin dashboard and enable the plugin

### Troubleshooting

If you experience build errors:
- Make sure you're using the latest .NET 6.0 SDK
- Use the provided build script which disables treating warnings as errors
- Check that you have the required dependencies installed

## License

MIT
