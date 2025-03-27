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
            CollectionName = "Test Collection";
            PlexServerUrl = string.Empty;
            PlexApiToken = string.Empty;
            SyncCollections = true;
            SyncPlaylists = false;
            DeleteBeforeSync = true; // Default to deleting collections before sync
            SyncArtwork = true; // Default to syncing artwork
            EnableScheduledSync = false;
            SyncIntervalHours = 24; // Default to daily sync
        }

        /// <summary>
        /// Gets or sets the name of the collection to be created.
        /// </summary>
        public string CollectionName { get; set; }

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
    }
}