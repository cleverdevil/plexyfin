<p align="center">
<img alt="Plugin Banner" src="https://raw.githubusercontent.com/cleverdevil/plexyfin/main/metadata/stable/banner.jpg"/>
<br/>

# Plexyfin Plugin for Jellyfin

A plugin for Jellyfin that syncs collections and artwork from your Plex Media Server to Jellyfin.

## Features

- Sync collections from Plex to Jellyfin with their artwork
- Sync media artwork (Primary/Poster and Backdrop/Fanart) from Plex to Jellyfin
- Sync TV Show Season artwork from Plex to Jellyfin
- Schedule automatic synchronization at configurable intervals
- Support for movies and TV shows in collections
- Selective synchronization of specific Plex libraries
- Dry run mode to preview changes before applying them

## ⚠️ Important Warning

**This plugin DELETES AND REPLACES artwork in Jellyfin with artwork from Plex.**

When the artwork sync feature is enabled:
- All existing poster images for media items will be completely removed before new ones are added
- All existing backdrop/fanart images for media items will be completely removed before new ones are added
- All existing poster and backdrop images for TV show seasons will be completely removed before new ones are added
- Custom artwork you've manually set in Jellyfin will be permanently lost

**It is STRONGLY recommended to create a backup of your Jellyfin metadata directory before performing a sync, especially the first time.**

## Installation

### Method 1: Easy Installation (Recommended)

1. In Jellyfin, go to Dashboard → Plugins → Repositories
2. Add a new repository with the following URL:
   `https://raw.githubusercontent.com/cleverdevil/plexyfin/main/metadata/stable/manifest.json`
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
   - Enable artwork sync (use with caution - see warning above about data loss)
   - The artwork sync includes:
     - Movie posters and backdrops
     - TV Series posters and backdrops
     - TV Show Season posters and backdrops
   - Set scheduled sync interval if desired
6. Click "Save"
7. Make sure you have a backup of your Jellyfin data before proceeding
8. Run a manual sync by clicking "Sync Now"

## Environment Variables

- `JELLYFIN_DATA_PATH`: Override the default data path (/config/data) for Jellyfin installations with non-standard paths

## Development

### Prerequisites

- .NET 8.0 SDK (or .NET 6.0 SDK for older Jellyfin versions)
- Jellyfin instance for testing

### Building

```bash
# Create a new version
./new_version.sh 0.7.0.0

# Build a specific version
./build_release.sh 0.7.0.0

# Build the latest version
./build_release.sh latest

# Build and update repository manifest
./build_release.sh 0.7.0.0 --deploy

# Tag and push a release
./tag_and_release.sh 0.7.0.0 --push

# Prepare GitHub release files
./prepare_github_release.sh 0.7.0.0
```

See [BUILD.md](BUILD.md) for detailed information about the build and release process.

### Installation

1. Build the plugin using `./build_release.sh latest`
2. Copy the resulting DLL from `dist/Plexyfin_X.Y.Z.Z/Jellyfin.Plugin.Plexyfin.dll` to your Jellyfin plugins directory
3. Restart Jellyfin
4. Go to the Jellyfin dashboard and enable the plugin

### Troubleshooting

If you experience build errors:
- Make sure you're using the latest .NET SDK
- Use `build_release.sh` which disables treating warnings as errors
- Check that you have the required dependencies installed

## License

MIT
