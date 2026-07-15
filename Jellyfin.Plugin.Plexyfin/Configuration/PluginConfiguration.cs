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
            SkipUnchangedArtwork = true; // Default to skipping re-download of artwork that Plex hasn't updated
            ForceReplaceAllArtwork = false; // Default off -- opt-in override to bypass the skip check above
            EnableScheduledSync = false;
            SyncBatchSize = 50; // Default number of items per processing batch
            MaxConcurrentApiRequests = 3; // Default cap on simultaneous Plex/Jellyfin requests
            BatchDelayMilliseconds = 750; // Default pause between batches
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
        /// Gets or sets a value indicating whether artwork downloads should be skipped when
        /// the local image file is already at least as new as the Plex item's "updatedAt"
        /// timestamp. This avoids unnecessary network and disk I/O on every sync run, while
        /// still picking up genuine changes -- including artwork refreshed by tools like
        /// Kometa (formerly Plex Meta Manager), which bump Plex's "updatedAt" value whenever
        /// they update a poster or backdrop.
        /// </summary>
        public bool SkipUnchangedArtwork { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore <see cref="SkipUnchangedArtwork"/>
        /// and re-download and replace every piece of artwork on the next sync(s), regardless
        /// of whether the local file already looks up to date. Use this to force a full refresh
        /// (for example, after changing artwork settings, or if you suspect local images are out
        /// of sync with Plex). Remember to turn this back off afterwards -- while it stays
        /// enabled, every sync run will re-download and replace all artwork every time, which
        /// is exactly the heavy behavior <see cref="SkipUnchangedArtwork"/> exists to avoid.
        /// </summary>
        public bool ForceReplaceAllArtwork { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether to automatically sync on a schedule.
        /// </summary>
        public bool EnableScheduledSync { get; set; }

        /// <summary>
        /// Gets or sets the number of items processed per batch during item-artwork sync.
        /// Items are worked through batch by batch, with a short pause between batches, rather
        /// than queuing an entire library at once. This does NOT control how many requests run
        /// at the same time -- see <see cref="MaxConcurrentApiRequests"/> for that.
        /// </summary>
        public int SyncBatchSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of items processed at the same time during
        /// item-artwork sync (movies/shows, and their seasons/episodes). This is the main
        /// control for how much simultaneous load Plexyfin puts on the Plex server (concurrent
        /// HTTP downloads) and on Jellyfin (concurrent image saves and database writes). Keep
        /// this low (2-5) unless you know your servers can comfortably handle more; high values
        /// can overwhelm Jellyfin's database and flood Plex with requests.
        /// </summary>
        public int MaxConcurrentApiRequests { get; set; }

        /// <summary>
        /// Gets or sets how long (in milliseconds) Plexyfin pauses between item-artwork batches
        /// (see <see cref="SyncBatchSize"/>). Set to 0 to disable the pause entirely and move
        /// straight to the next batch. Higher values are gentler on Plex/Jellyfin but make large
        /// libraries take longer to sync.
        /// </summary>
        public int BatchDelayMilliseconds { get; set; }
        
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