# Jellyfin Plugin Repository Setup

This document explains how to set up and maintain the Jellyfin plugin repository for Plexyfin.

## Repository Structure

```
repository/
├── manifest.json
└── versions/
    └── plexyfin/
        ├── 1.0.0.json
        ├── 1.0.1.json
        └── ...
```

## Setting Up the Repository

1. Build the plugin using the build script or `dotnet build`
2. Run the `generate_repository.sh` script to create or update repository files:
   ```bash
   ./generate_repository.sh
   ```
3. Host the repository files on a web server or GitHub Pages
4. Make sure the plugin DLL is available at the URL specified in the version JSON file

## Hosting Options

### GitHub Pages

1. Push the repository directory to a GitHub repository
2. Enable GitHub Pages in the repository settings
3. Set the source to the branch and directory containing the repository files

### Web Server

1. Upload the repository directory to your web server
2. Make sure the files are accessible via HTTP/HTTPS

## Updating the Repository

When releasing a new version:

1. Build the new version of the plugin
2. Run the `generate_repository.sh` script
3. Upload the new DLL to the download location
4. Update the hosted repository files

## Testing

To test the repository:

1. Add the repository URL to your Jellyfin instance
2. Check if the plugin appears in the catalog
3. Try installing the plugin

## Troubleshooting

- Make sure all URLs in the manifest and version files are correct
- Verify that the checksums match the actual DLL files
- Check that the targetAbi is compatible with the Jellyfin version
- Ensure all files are accessible via HTTP/HTTPS
