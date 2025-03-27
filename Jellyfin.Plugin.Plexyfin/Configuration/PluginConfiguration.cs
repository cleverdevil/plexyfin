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
    }
}