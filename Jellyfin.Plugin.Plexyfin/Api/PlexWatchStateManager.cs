using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plexyfin.Api
{
    /// <summary>
    /// Manages watch state synchronization between Plex and Jellyfin.
    /// </summary>
    public class PlexWatchStateManager
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly PlexApiClient _plexApiClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexWatchStateManager"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/>.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/>.</param>
        public PlexWatchStateManager(
            ILogger logger,
            IHttpClientFactory httpClientFactory,
            IUserManager userManager,
            IUserDataManager userDataManager)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _userDataManager = userDataManager;
            _plexApiClient = new PlexApiClient(httpClientFactory, logger);
        }

        /// <summary>
        /// Synchronizes watch state between Plex and Jellyfin based on the configured direction.
        /// </summary>
        /// <param name="jellyfinItems">List of Jellyfin items to synchronize.</param>
        /// <param name="plexServerUrl">Plex server URL.</param>
        /// <param name="plexApiToken">Plex API token.</param>
        /// <param name="syncDirection">Direction of synchronization.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SyncWatchStateAsync(
            IEnumerable<BaseItem> jellyfinItems,
            string plexServerUrl,
            string plexApiToken,
            string syncDirection)
        {
            try
            {
                _logger.LogInformation("Starting watch state synchronization in {Direction} mode", syncDirection);

                // Get the default Jellyfin user
                var jellyfinUser = _userManager.Users.FirstOrDefault();
                if (jellyfinUser == null)
                {
                    _logger.LogWarning("No Jellyfin users found for watch state sync");
                    return;
                }

                // Initialize counters for reporting
                int updatedInJellyfin = 0;
                int updatedInPlex = 0;
                int errors = 0;

                foreach (var item in jellyfinItems)
                {
                    try
                    {
                        // Get the Jellyfin item's external ID that matches Plex's rating key
                        var plexId = item.GetProviderId("Plex");
                        if (string.IsNullOrEmpty(plexId))
                        {
                            // Skip items without Plex IDs
                            continue;
                        }

                        // Get the current watch state in Jellyfin
                        var jellyfinUserData = _userDataManager.GetUserData(jellyfinUser, item);
                        bool jellyfinWatched = jellyfinUserData.Played;
                        double jellyfinPlaybackPosition = jellyfinUserData.PlaybackPositionTicks / 10000000.0; // Convert ticks to seconds

                        if (syncDirection == "PlexToJellyfin" || syncDirection == "Bidirectional")
                        {
                            // Get watch state from Plex
                            var plexWatchState = await _plexApiClient.GetItemWatchStateAsync(plexServerUrl, plexApiToken, plexId);
                            
                            if (plexWatchState != null)
                            {
                                // Update Jellyfin watch state if it differs from Plex
                                if (plexWatchState.Watched != jellyfinWatched)
                                {
                                    if (plexWatchState.Watched)
                                    {
                                        _userDataManager.MarkPlayed(item, jellyfinUser, DateTime.UtcNow);
                                    }
                                    else
                                    {
                                        _userDataManager.MarkUnplayed(item, jellyfinUser);
                                    }
                                    
                                    updatedInJellyfin++;
                                    _logger.LogInformation("Updated watch state for {ItemName} in Jellyfin to {WatchState}", 
                                        item.Name, plexWatchState.Watched ? "watched" : "unwatched");
                                }
                                
                                // Update playback position if needed
                                if (plexWatchState.PlaybackPosition > 0 && 
                                    Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition) > 10) // 10 second threshold
                                {
                                    _userDataManager.SaveUserData(
                                        jellyfinUser.Id,
                                        item,
                                        new UserItemData
                                        {
                                            PlaybackPositionTicks = Convert.ToInt64(plexWatchState.PlaybackPosition * 10000000),
                                            Played = jellyfinUserData.Played,
                                            PlayCount = jellyfinUserData.PlayCount,
                                            IsFavorite = jellyfinUserData.IsFavorite,
                                            Rating = jellyfinUserData.Rating,
                                            LastPlayedDate = jellyfinUserData.LastPlayedDate
                                        },
                                        UserDataSaveReason.TogglePlayed);
                                        
                                    _logger.LogInformation("Updated playback position for {ItemName} in Jellyfin to {Position} seconds", 
                                        item.Name, plexWatchState.PlaybackPosition);
                                }
                            }
                        }

                        if (syncDirection == "JellyfinToPlex" || syncDirection == "Bidirectional")
                        {
                            // Update Plex watch state from Jellyfin
                            bool success = await _plexApiClient.UpdateItemWatchStateAsync(
                                plexServerUrl, 
                                plexApiToken, 
                                plexId, 
                                jellyfinWatched, 
                                jellyfinPlaybackPosition);
                                
                            if (success)
                            {
                                updatedInPlex++;
                                _logger.LogInformation("Updated watch state for {ItemName} in Plex to {WatchState}", 
                                    item.Name, jellyfinWatched ? "watched" : "unwatched");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing watch state for item {ItemName}", item.Name);
                        errors++;
                    }
                }

                _logger.LogInformation("Watch state sync completed. Updated {JellyfinCount} items in Jellyfin, {PlexCount} items in Plex. Errors: {ErrorCount}", 
                    updatedInJellyfin, updatedInPlex, errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during watch state synchronization");
            }
        }
    }

    /// <summary>
    /// Represents the watch state of a Plex item.
    /// </summary>
    public class PlexWatchState
    {
        /// <summary>
        /// Gets or sets a value indicating whether the item has been watched.
        /// </summary>
        public bool Watched { get; set; }
        
        /// <summary>
        /// Gets or sets the playback position in seconds.
        /// </summary>
        public double PlaybackPosition { get; set; }
    }
}
