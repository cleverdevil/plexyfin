using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plexyfin.Api
{
    /// <summary>
    /// Manager for synchronizing watch states between Plex and Jellyfin.
    /// </summary>
    public class PlexWatchStateManager
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        
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
        }
        
        /// <summary>
        /// Synchronizes watch states between Plex and Jellyfin.
        /// </summary>
        /// <param name="items">The Jellyfin items to synchronize.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <param name="direction">The direction of synchronization.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SyncWatchStateAsync(
            IEnumerable<BaseItem> items,
            string plexServerUrl,
            string plexToken,
            string direction)
        {
            _logger.LogInformation("Starting watch state synchronization with direction: {Direction}", direction);
            
            // Get the default user
            var user = _userManager.Users.FirstOrDefault();
            if (user == null)
            {
                _logger.LogWarning("No users found in Jellyfin, cannot sync watch states");
                return;
            }
            
            var plexApiClient = new PlexApiClient(_httpClientFactory, _logger);
            
            foreach (var item in items)
            {
                try
                {
                    // Get the Plex ID from the Jellyfin item
                    var plexId = item.ProviderIds.TryGetValue("Plex", out var id) ? id : null;
                    
                    if (string.IsNullOrEmpty(plexId))
                    {
                        // Skip items without a Plex ID
                        continue;
                    }
                    
                    // Get the Jellyfin watch state
                    var jellyfinUserData = _userDataManager.GetUserData(user, item);
                    bool jellyfinWatched = jellyfinUserData.Played;
                    double jellyfinPlaybackPosition = (double)jellyfinUserData.PlaybackPositionTicks / 10000000; // Convert ticks to seconds
                    
                    // Get the Plex watch state
                    var plexWatchState = await plexApiClient.GetItemWatchStateAsync(plexId, plexServerUrl, plexToken);
                    
                    if (plexWatchState == null)
                    {
                        _logger.LogWarning("Could not get watch state from Plex for item {ItemName} ({ItemId})", item.Name, item.Id);
                        continue;
                    }
                    
                    if (direction == "PlexToJellyfin" || direction == "Bidirectional")
                    {
                        // Update Jellyfin watch state from Plex
                        if (plexWatchState.Watched && !jellyfinWatched)
                        {
                            _logger.LogInformation("Marking item as watched in Jellyfin: {ItemName}", item.Name);
                            _userDataManager.MarkPlayed(item, user, DateTime.UtcNow, true);
                        }
                        else if (!plexWatchState.Watched && jellyfinWatched)
                        {
                            if (direction == "PlexToJellyfin")
                            {
                                // Only mark as unwatched if we're doing one-way sync from Plex to Jellyfin
                                _logger.LogInformation("Marking item as unwatched in Jellyfin: {ItemName}", item.Name);
                                _userDataManager.MarkUnplayed(item, user, DateTime.UtcNow);
                            }
                        }
                        else if (!plexWatchState.Watched && !jellyfinWatched && plexWatchState.PlaybackPosition > 0)
                        {
                            // Update playback position if the item is in progress
                            if (Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition) > 10) // Only update if difference is more than 10 seconds
                            {
                                _logger.LogInformation("Updating playback position in Jellyfin for {ItemName}: {Position} seconds", 
                                    item.Name, plexWatchState.PlaybackPosition);
                                    
                                jellyfinUserData.PlaybackPositionTicks = (long)(plexWatchState.PlaybackPosition * 10000000);
                                _userDataManager.SaveUserData(user.Id, item, jellyfinUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                            }
                        }
                    }
                    
                    if (direction == "JellyfinToPlex" || direction == "Bidirectional")
                    {
                        // Update Plex watch state from Jellyfin
                        if (jellyfinWatched && !plexWatchState.Watched)
                        {
                            _logger.LogInformation("Marking item as watched in Plex: {ItemName}", item.Name);
                            await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, true);
                        }
                        else if (!jellyfinWatched && plexWatchState.Watched)
                        {
                            if (direction == "JellyfinToPlex")
                            {
                                // Only mark as unwatched if we're doing one-way sync from Jellyfin to Plex
                                _logger.LogInformation("Marking item as unwatched in Plex: {ItemName}", item.Name);
                                await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, false);
                            }
                        }
                        else if (!jellyfinWatched && !plexWatchState.Watched && jellyfinPlaybackPosition > 0)
                        {
                            // Update playback position if the item is in progress
                            if (Math.Abs(jellyfinPlaybackPosition - plexWatchState.PlaybackPosition) > 10) // Only update if difference is more than 10 seconds
                            {
                                _logger.LogInformation("Updating playback position in Plex for {ItemName}: {Position} seconds", 
                                    item.Name, jellyfinPlaybackPosition);
                                    
                                await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, false, jellyfinPlaybackPosition);
                            }
                        }
                    }
                    
                    if (direction == "Bidirectional")
                    {
                        // For bidirectional sync, if either is watched, both should be watched
                        bool shouldBeWatched = jellyfinWatched || plexWatchState.Watched;
                        
                        if (shouldBeWatched)
                        {
                            // Make sure both are marked as watched
                            if (!jellyfinWatched)
                            {
                                _logger.LogInformation("Bidirectional sync: Marking item as watched in Jellyfin: {ItemName}", item.Name);
                                _userDataManager.MarkPlayed(item, user, DateTime.UtcNow, true);
                            }
                            
                            if (!plexWatchState.Watched)
                            {
                                _logger.LogInformation("Bidirectional sync: Marking item as watched in Plex: {ItemName}", item.Name);
                                await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, true);
                            }
                        }
                        else
                        {
                            // For in-progress items, use the furthest playback position
                            double maxPosition = Math.Max(jellyfinPlaybackPosition, plexWatchState.PlaybackPosition);
                            
                            if (maxPosition > 0)
                            {
                                // Update Jellyfin if needed
                                if (Math.Abs(jellyfinPlaybackPosition - maxPosition) > 10)
                                {
                                    _logger.LogInformation("Bidirectional sync: Updating playback position in Jellyfin for {ItemName}: {Position} seconds", 
                                        item.Name, maxPosition);
                                        
                                    jellyfinUserData.PlaybackPositionTicks = (long)(maxPosition * 10000000);
                                    _userDataManager.SaveUserData(user.Id, item, jellyfinUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                                }
                                
                                // Update Plex if needed
                                if (Math.Abs(plexWatchState.PlaybackPosition - maxPosition) > 10)
                                {
                                    _logger.LogInformation("Bidirectional sync: Updating playback position in Plex for {ItemName}: {Position} seconds", 
                                        item.Name, maxPosition);
                                        
                                    await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, false, maxPosition);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing watch state for item {ItemName} ({ItemId})", item.Name, item.Id);
                }
            }
            
            try
            {
                _logger.LogInformation("Watch state synchronization completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing watch state sync");
            }
        }
    }
}
