# Plexyfin Plugin for Jellyfin

A plugin for Jellyfin that adds various features inspired by Plex functionality.

## Features

- Create a collection with all movies that start with the letter 'A'

## Installation

1. Download the latest release from the Releases page
2. Extract the zip file to your Jellyfin plugins directory
3. Restart Jellyfin
4. Enable the plugin in the Jellyfin dashboard

## Usage

1. Go to the Jellyfin dashboard
2. Navigate to Plugins
3. Click on Plexyfin
4. Enter a collection name (defaults to "Test Collection")
5. Click "Create Collection"

## Development

### Prerequisites

- .NET 6.0 SDK
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