using System.Collections.Generic;
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
            PlexServerUrl = string.Empty;
            PlexApiToken = string.Empty;
            SyncCollections = true;
            SyncPlaylists = false;
            DeleteBeforeSync = false; // Default to updating collections instead of deleting
            SyncArtwork = true; // Default to syncing artwork
            EnableScheduledSync = false;
            SyncIntervalHours = 24; // Default to daily sync
            SelectedLibraries = new List<string>(); // Initialize with empty list
            EnableDebugMode = false; // Default to no debug mode
            MaxUrlPatternAttempts = 3; // Default to 3 URL pattern attempts
            SyncWatchState = false; // Default to not syncing watch state
            SyncWatchStateDirection = "Bidirectional"; // Default to bidirectional sync
        }

        /// <summary>
        /// Gets or sets the Plex Media Server URL.
        /// </summary>
        public string PlexServerUrl { get; set; }

        /// <summary>
        /// Gets or sets the Plex API Token.
        /// </summary>
        public string PlexApiToken { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync collections from Plex.
        /// </summary>
        public bool SyncCollections { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync playlists from Plex.
        /// </summary>
        public bool SyncPlaylists { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to delete existing collections before syncing.
        /// </summary>
        public bool DeleteBeforeSync { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to sync collection artwork.
        /// </summary>
        public bool SyncArtwork { get; set; }
        
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
        
        /// <summary>
        /// Gets or sets the maximum number of URL patterns to try when fetching collection items.
        /// Lower values improve performance but may reduce compatibility with some Plex servers.
        /// </summary>
        public int MaxUrlPatternAttempts { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to sync watch state between Plex and Jellyfin.
        /// </summary>
        public bool SyncWatchState { get; set; }
        
        /// <summary>
        /// Gets or sets the direction for watch state synchronization.
        /// Valid values: "PlexToJellyfin", "JellyfinToPlex", "Bidirectional"
        /// </summary>
        public string SyncWatchStateDirection { get; set; }
    }
}