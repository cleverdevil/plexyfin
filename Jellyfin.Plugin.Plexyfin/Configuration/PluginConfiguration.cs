using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Plexyfin.Configuration
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            // Set default values
            PlexApiToken = string.Empty;
            SyncCollections = true;
            SyncArtwork = true; // Default to syncing artwork
            SyncItemArtwork = false; // Default to not syncing item artwork (opt-in feature)
            EnableScheduledSync = false;
            SyncIntervalHours = 24; // Default to daily sync
            SelectedLibraries = new List<string>(); // Initialize with empty list
            EnableDebugMode = false; // Default to no debug mode
            // MaxUrlPatternAttempts setting removed as it's no longer needed
        }

        /// <summary>
        /// Gets or sets the Plex Media Server URL.
        /// </summary>
        public string? PlexServerUrl { get; set; }

        /// <summary>
        /// Gets or sets the Plex API Token.
        /// </summary>
        public string PlexApiToken { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync collections from Plex.
        /// </summary>
        public bool SyncCollections { get; set; }

        
        /// <summary>
        /// Gets or sets a value indicating whether to sync collection artwork.
        /// </summary>
        public bool SyncArtwork { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to sync movie and TV show artwork.
        /// </summary>
        public bool SyncItemArtwork { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to automatically sync on a schedule.
        /// </summary>
        public bool EnableScheduledSync { get; set; }
        
        /// <summary>
        /// Gets or sets the schedule interval in hours.
        /// </summary>
        public int SyncIntervalHours { get; set; }
        
        /// <summary>
        /// Gets or sets a list of selected Plex library IDs to include in sync operations.
        /// If empty, all libraries will be considered.
        /// </summary>
        public List<string> SelectedLibraries { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether debug mode is enabled.
        /// When enabled, debug images will be saved and more verbose logging will occur.
        /// </summary>
        public bool EnableDebugMode { get; set; }
        
        // MaxUrlPatternAttempts setting removed as it's no longer needed
        
    }
}