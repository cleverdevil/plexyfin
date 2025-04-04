using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;

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

        private readonly SyncStatus? _syncStatus;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexWatchStateManager"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/>.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/>.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        public PlexWatchStateManager(
            ILogger logger,
            IHttpClientFactory httpClientFactory,
            IUserManager userManager,
            IUserDataManager userDataManager,
            SyncStatus? syncStatus = null)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _userDataManager = userDataManager;
            _syncStatus = syncStatus;
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
            Uri plexServerUrl,
            string plexToken,
            string direction)
        {
            // Validate parameters
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (plexServerUrl == null)
            {
                throw new ArgumentNullException(nameof(plexServerUrl));
            }

            if (string.IsNullOrEmpty(plexToken))
            {
                throw new ArgumentException("Plex token cannot be null or empty", nameof(plexToken));
            }

            if (string.IsNullOrEmpty(direction))
            {
                throw new ArgumentException("Sync direction cannot be null or empty", nameof(direction));
            }

            _logger.LogInformation("Starting watch state synchronization with direction: {Direction}", direction);

            // Get the default user
            var user = _userManager.Users.FirstOrDefault();
            if (user == null)
            {
                _logger.LogWarning("No users found in Jellyfin, cannot sync watch states");
                return;
            }

            var plexApiClient = new PlexApiClient(_httpClientFactory, _logger);
            var itemCount = 0;
            var syncedCount = 0;

            // Convert to a list so we can get an accurate count
            var itemsList = items.ToList();
            int totalItems = itemsList.Count;
            int remainingItems = totalItems;

            foreach (var item in itemsList)
            {
                try
                {
                    itemCount++;
                    remainingItems--;

                    // Update status every 5 items or when items % 5 == 0
                    if (_syncStatus != null && (itemCount <= 5 || itemCount % 5 == 0 || remainingItems <= 5))
                    {
                        _syncStatus.ProcessedItems = itemCount;
                        _syncStatus.RemainingItems = remainingItems;

                        string itemType = item is Movie ? "movie" : "episode";
                        string mediaItemTitle = item.Name;

                        if (item is Episode tvEpisode && tvEpisode.Series != null)
                        {
                            mediaItemTitle = $"{tvEpisode.Series.Name} - {mediaItemTitle}";
                        }

                        _syncStatus.Message = $"Processing {itemType} {itemCount} of {totalItems}: {mediaItemTitle}";
                        _syncStatus.Progress = 50 + (int)(50.0 * itemCount / totalItems);
                    }

                    // Log basic item details
                    _logger.LogInformation("Processing item {ItemCount}/{TotalItems}: '{ItemName}' ({ItemType})",
                        itemCount, totalItems, item.Name, item.GetType().Name);

                    // Log provider IDs for debugging at debug level
                    if (item.ProviderIds.Count > 0)
                    {
                        _logger.LogDebug("Item provider IDs: {ProviderIds}",
                            string.Join(", ", item.ProviderIds.Select(p => $"{p.Key}={p.Value}")));
                    }

                    // Try to find Plex item corresponding to this Jellyfin item
                    string? plexId = await FindPlexItemIdAsync(item, plexServerUrl, plexToken).ConfigureAwait(false);

                    // If no Plex ID found, skip this item
                    if (string.IsNullOrEmpty(plexId))
                    {
                        _logger.LogInformation("Could not find matching Plex item for '{ItemName}', skipping", item.Name);
                        continue;
                    }

                    syncedCount++;

                    // Get the Jellyfin watch state
                    var jellyfinUserData = _userDataManager.GetUserData(user, item);
                    bool jellyfinWatched = jellyfinUserData.Played;
                    double jellyfinPlaybackPosition = (double)jellyfinUserData.PlaybackPositionTicks / 10000000; // Convert ticks to seconds

                    // Log Jellyfin watch state
                    _logger.LogInformation("Jellyfin state for '{ItemName}': Watched={Watched}, Position={Position:F0}s, PlayCount={PlayCount}, LastPlayed={LastPlayed}",
                        item.Name,
                        jellyfinWatched,
                        jellyfinPlaybackPosition,
                        jellyfinUserData.PlayCount,
                        jellyfinUserData.LastPlayedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never");

                    // Get the Plex watch state
                    _logger.LogInformation("Requesting Plex watch state for ID {PlexId} ('{ItemName}')", plexId, item.Name);
                    var plexWatchState = await plexApiClient.GetItemWatchStateAsync(plexId, plexServerUrl, plexToken).ConfigureAwait(false);

                    if (plexWatchState == null)
                    {
                        _logger.LogWarning("Could not get watch state from Plex for item '{ItemName}' ({ItemId}). Item may not exist in Plex or Plex ID is incorrect.",
                            item.Name, item.Id);
                        continue;
                    }

                    // Log Plex watch state
                    _logger.LogInformation("Plex state for '{ItemName}' (ID {PlexId}): Watched={Watched}, Position={Position:F0}s",
                        item.Name,
                        plexId,
                        plexWatchState.Watched,
                        plexWatchState.PlaybackPosition);

                    // Process watch state based on the specified direction
                    if (direction == "Bidirectional")
                    {
                        // Bidirectional sync - special case handled differently than one-way syncs

                        // For watched state, if either is watched, both should be watched
                        bool shouldBeWatched = jellyfinWatched || plexWatchState.Watched;

                        _logger.LogInformation("Bidirectional evaluation for '{ItemName}': shouldBeWatched={ShouldBeWatched}",
                            item.Name, shouldBeWatched);

                        if (shouldBeWatched)
                        {
                            // Make sure both systems have the content marked as watched
                            if (!jellyfinWatched)
                            {
                                _logger.LogInformation("Bidirectional sync: Marking '{ItemName}' as watched in Jellyfin", item.Name);

                                // Create new user data based on current state
                                var updatedUserData = new UserItemData
                                {
                                    Key = jellyfinUserData.Key,
                                    Played = true, // Mark as watched
                                    PlaybackPositionTicks = 0, // Reset position when marking as watched
                                    LastPlayedDate = jellyfinUserData.LastPlayedDate ?? DateTime.UtcNow,
                                    PlayCount = Math.Max(1, jellyfinUserData.PlayCount),
                                    Likes = jellyfinUserData.Likes,
                                    Rating = jellyfinUserData.Rating,
                                    IsFavorite = jellyfinUserData.IsFavorite
                                };

                                _userDataManager.SaveUserData(user, item, updatedUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                            }

                            if (!plexWatchState.Watched)
                            {
                                _logger.LogInformation("Bidirectional sync: Marking '{ItemName}' as watched in Plex", item.Name);
                                await plexApiClient.UpdateItemWatchStateAsync(plexId, plexServerUrl, plexToken, true).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // Neither is fully watched, so handle in-progress items
                            // Use the furthest along playback position between the two systems
                            double maxPosition = Math.Max(jellyfinPlaybackPosition, plexWatchState.PlaybackPosition);

                            _logger.LogInformation("Bidirectional in-progress evaluation for '{ItemName}': maxPosition={MaxPos:F0}s (Jellyfin={JellyfinPos:F0}s, Plex={PlexPos:F0}s)",
                                item.Name, maxPosition, jellyfinPlaybackPosition, plexWatchState.PlaybackPosition);

                            if (maxPosition > 0)
                            {
                                // Only update if there's a meaningful difference (>10 seconds)
                                if (Math.Abs(jellyfinPlaybackPosition - maxPosition) > 10)
                                {
                                    _logger.LogInformation("Bidirectional sync: Updating '{ItemName}' position in Jellyfin to {Position:F0} seconds",
                                        item.Name, maxPosition);

                                    // Create new user data based on current state
                                    var updatedUserData = new UserItemData
                                    {
                                        Key = jellyfinUserData.Key,
                                        Played = jellyfinUserData.Played,
                                        PlaybackPositionTicks = (long)(maxPosition * 10000000),
                                        LastPlayedDate = jellyfinUserData.LastPlayedDate,
                                        PlayCount = jellyfinUserData.PlayCount,
                                        Likes = jellyfinUserData.Likes,
                                        Rating = jellyfinUserData.Rating,
                                        IsFavorite = jellyfinUserData.IsFavorite
                                    };

                                    _userDataManager.SaveUserData(user, item, updatedUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                                }

                                if (Math.Abs(plexWatchState.PlaybackPosition - maxPosition) > 10)
                                {
                                    _logger.LogInformation("Bidirectional sync: Updating '{ItemName}' position in Plex to {Position:F0} seconds",
                                        item.Name, maxPosition);

                                    await plexApiClient.UpdateItemWatchStateAsync(
                                        plexId, plexServerUrl, plexToken, false, maxPosition).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Both positions are 0, no position updates needed.");
                            }
                        }
                    }
                    else if (direction == "PlexToJellyfin")
                    {
                        // One-way sync from Plex to Jellyfin
                        if (plexWatchState.Watched != jellyfinWatched)
                        {
                            _logger.LogInformation("Plex → Jellyfin: Setting '{ItemName}' watched state in Jellyfin to {State}",
                                item.Name, plexWatchState.Watched ? "watched" : "unwatched");

                            // Create new user data based on current state
                            var updatedUserData = new UserItemData
                            {
                                Key = jellyfinUserData.Key,
                                Played = plexWatchState.Watched,
                                PlaybackPositionTicks = plexWatchState.Watched ? 0 : jellyfinUserData.PlaybackPositionTicks,
                                LastPlayedDate = plexWatchState.Watched ? (jellyfinUserData.LastPlayedDate ?? DateTime.UtcNow) : jellyfinUserData.LastPlayedDate,
                                PlayCount = plexWatchState.Watched ? Math.Max(1, jellyfinUserData.PlayCount) : jellyfinUserData.PlayCount,
                                Likes = jellyfinUserData.Likes,
                                Rating = jellyfinUserData.Rating,
                                IsFavorite = jellyfinUserData.IsFavorite
                            };

                            _userDataManager.SaveUserData(user, item, updatedUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                        }
                        else if (!plexWatchState.Watched && !jellyfinWatched &&
                                 plexWatchState.PlaybackPosition > 0 &&
                                 Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition) > 10)
                        {
                            // Item is in progress and positions are different enough to sync
                            _logger.LogInformation("Plex → Jellyfin: Updating '{ItemName}' position in Jellyfin to {Position:F0} seconds",
                                item.Name, plexWatchState.PlaybackPosition);

                            // Create new user data based on current state
                            var updatedUserData = new UserItemData
                            {
                                Key = jellyfinUserData.Key,
                                Played = jellyfinUserData.Played,
                                PlaybackPositionTicks = (long)(plexWatchState.PlaybackPosition * 10000000),
                                LastPlayedDate = jellyfinUserData.LastPlayedDate,
                                PlayCount = jellyfinUserData.PlayCount,
                                Likes = jellyfinUserData.Likes,
                                Rating = jellyfinUserData.Rating,
                                IsFavorite = jellyfinUserData.IsFavorite
                            };

                            _userDataManager.SaveUserData(user, item, updatedUserData, UserDataSaveReason.TogglePlayed, CancellationToken.None);
                        }
                    }
                    else if (direction == "JellyfinToPlex")
                    {
                        // One-way sync from Jellyfin to Plex
                        if (jellyfinWatched != plexWatchState.Watched)
                        {
                            _logger.LogInformation("Jellyfin → Plex: Setting '{ItemName}' watched state in Plex to {State}",
                                item.Name, jellyfinWatched ? "watched" : "unwatched");

                            await plexApiClient.UpdateItemWatchStateAsync(
                                plexId, plexServerUrl, plexToken, jellyfinWatched).ConfigureAwait(false);
                        }
                        else if (!jellyfinWatched && !plexWatchState.Watched &&
                                 jellyfinPlaybackPosition > 0 &&
                                 Math.Abs(jellyfinPlaybackPosition - plexWatchState.PlaybackPosition) > 10)
                        {
                            // Item is in progress and positions are different enough to sync
                            _logger.LogInformation("Jellyfin → Plex: Updating '{ItemName}' position in Plex to {Position:F0} seconds",
                                item.Name, jellyfinPlaybackPosition);

                            await plexApiClient.UpdateItemWatchStateAsync(
                                plexId, plexServerUrl, plexToken, false, jellyfinPlaybackPosition).ConfigureAwait(false);
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
                _logger.LogInformation("Watch state synchronization completed with direction '{Direction}' - processed {ItemCount} items",
                    direction, items.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing watch state sync");
            }
        }

        /// <summary>
        /// Previews watch state changes without actually applying them.
        /// </summary>
        /// <param name="items">The Jellyfin items to check.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <param name="direction">The direction of synchronization.</param>
        /// <returns>A list of watch state changes that would be made.</returns>
        public async Task<List<SyncWatchStateDetail>> PreviewWatchStateChangesAsync(
            IEnumerable<BaseItem> items,
            Uri plexServerUrl,
            string plexToken,
            string direction)
        {
            var changes = new List<SyncWatchStateDetail>();

            // Validate parameters
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (plexServerUrl == null)
            {
                throw new ArgumentNullException(nameof(plexServerUrl));
            }

            if (string.IsNullOrEmpty(plexToken))
            {
                throw new ArgumentException("Plex token cannot be null or empty", nameof(plexToken));
            }

            if (string.IsNullOrEmpty(direction))
            {
                throw new ArgumentException("Sync direction cannot be null or empty", nameof(direction));
            }

            _logger.LogInformation("Previewing watch state changes with direction: {Direction}", direction);

            // Get the default user
            var user = _userManager.Users.FirstOrDefault();
            if (user == null)
            {
                _logger.LogWarning("No users found in Jellyfin, cannot preview watch states");
                _logger.LogWarning("Watch state preview ABORTED - NO USERS FOUND in Jellyfin");
                return changes;
            }

            _logger.LogInformation("Starting watch state preview with direction '{Direction}' for user '{Username}' (ID: {UserId})",
                direction, user.Username, user.Id);

            var plexApiClient = new PlexApiClient(_httpClientFactory, _logger);
            var itemCount = 0;

            // Convert to a list so we can get an accurate count
            var itemsList = items.ToList();
            int totalItems = itemsList.Count;
            int remainingItems = totalItems;

            foreach (var item in itemsList)
            {
                try
                {
                    itemCount++;
                    remainingItems--;

                    // Update status every 5 items or when items % 5 == 0
                    if (_syncStatus != null && (itemCount <= 5 || itemCount % 5 == 0 || remainingItems <= 5))
                    {
                        _syncStatus.ProcessedItems = itemCount;
                        _syncStatus.RemainingItems = remainingItems;

                        string itemType = item is Movie ? "movie" : "episode";
                        string mediaItemTitle = item.Name;

                        if (item is Episode tvEpisode && tvEpisode.Series != null)
                        {
                            mediaItemTitle = $"{tvEpisode.Series.Name} - {mediaItemTitle}";
                        }

                        _syncStatus.Message = $"Analyzing {itemCount} of {totalItems}: {mediaItemTitle} ({itemType})";
                        _syncStatus.Progress = 50 + (int)(50.0 * itemCount / totalItems);
                    }

                    // Log basic item details
                    _logger.LogDebug("Processing item {ItemCount}/{TotalItems}: '{ItemName}' ({ItemType})",
                        itemCount, totalItems, item.Name, item.GetType().Name);

                    // Log provider IDs at debug level
                    if (item.ProviderIds.Count > 0)
                    {
                        _logger.LogDebug("Item provider IDs: {ProviderIds}",
                            string.Join(", ", item.ProviderIds.Select(p => $"{p.Key}={p.Value}")));
                    }

                    // Try to find Plex item corresponding to this Jellyfin item
                    string? plexId = await FindPlexItemIdAsync(item, plexServerUrl, plexToken).ConfigureAwait(false);

                    // If no Plex ID found, skip this item
                    if (string.IsNullOrEmpty(plexId))
                    {
                        _logger.LogDebug("Could not find matching Plex item for '{ItemName}', skipping", item.Name);
                        continue;
                    }

                    // Get the Jellyfin watch state
                    var jellyfinUserData = _userDataManager.GetUserData(user, item);
                    bool jellyfinWatched = jellyfinUserData.Played;
                    double jellyfinPlaybackPosition = (double)jellyfinUserData.PlaybackPositionTicks / 10000000; // Convert ticks to seconds

                    // Log Jellyfin watch state
                    _logger.LogInformation("Jellyfin state for '{ItemName}': Watched={Watched}, Position={Position:F0}s, PlayCount={PlayCount}, LastPlayed={LastPlayed}",
                        item.Name,
                        jellyfinWatched,
                        jellyfinPlaybackPosition,
                        jellyfinUserData.PlayCount,
                        jellyfinUserData.LastPlayedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never");

                    // Get the Plex watch state
                    _logger.LogInformation("Requesting Plex watch state for ID {PlexId} ('{ItemName}')", plexId, item.Name);
                    var plexWatchState = await plexApiClient.GetItemWatchStateAsync(plexId, plexServerUrl, plexToken).ConfigureAwait(false);

                    if (plexWatchState == null)
                    {
                        _logger.LogWarning("Could not get watch state from Plex for item '{ItemName}' ({ItemId}). Item may not exist in Plex or Plex ID is incorrect.",
                            item.Name, item.Id);
                        continue;
                    }

                    // Log Plex watch state
                    _logger.LogInformation("Plex state for '{ItemName}' (ID {PlexId}): Watched={Watched}, Position={Position:F0}s",
                        item.Name,
                        plexId,
                        plexWatchState.Watched,
                        plexWatchState.PlaybackPosition);

                    bool changeDetected = false;
                    // Include series and season info for TV episodes
                    string itemTitle = item.Name;
                    if (item is MediaBrowser.Controller.Entities.TV.Episode episode)
                    {
                        var series = episode.Series;
                        if (series != null)
                        {
                            itemTitle = $"{series.Name} - S{episode.ParentIndexNumber:D2}E{episode.IndexNumber:D2} - {episode.Name}";
                        }
                    }

                    var changeDetail = new SyncWatchStateDetail
                    {
                        Title = itemTitle
                    };

                    // Format current state to show status on both platforms
                    changeDetail.CurrentState = jellyfinWatched
                        ? "Jellyfin: Watched, "
                        : jellyfinPlaybackPosition > 0
                            ? $"Jellyfin: In Progress ({jellyfinPlaybackPosition:F0} seconds), "
                            : "Jellyfin: Unwatched, ";

                    changeDetail.CurrentState += plexWatchState.Watched
                        ? "Plex: Watched"
                        : plexWatchState.PlaybackPosition > 0
                            ? $"Plex: In Progress ({plexWatchState.PlaybackPosition:F0} seconds)"
                            : "Plex: Unwatched";

                    _logger.LogInformation("WATCH COMPARISON: '{ItemName}' - Jellyfin({JellyfinWatched}, {JellyfinPos:F0}s) vs Plex({PlexWatched}, {PlexPos:F0}s)",
                        item.Name,
                        jellyfinWatched,
                        jellyfinPlaybackPosition,
                        plexWatchState.Watched,
                        plexWatchState.PlaybackPosition);

                    // Analyze changes based on sync direction
                    if (direction == "Bidirectional")
                    {
                        // For bidirectional sync
                        bool shouldBeWatched = jellyfinWatched || plexWatchState.Watched;

                        _logger.LogInformation("Bidirectional evaluation for '{ItemName}': shouldBeWatched={ShouldBeWatched}",
                            item.Name, shouldBeWatched);

                        if (shouldBeWatched)
                        {
                            // If either is watched, both should be watched
                            List<string> updates = new List<string>();

                            if (!jellyfinWatched)
                            {
                                _logger.LogInformation("Bidirectional: '{ItemName}' should be marked as watched in Jellyfin", item.Name);
                                updates.Add("Mark as watched in Jellyfin");
                            }
                            else
                            {
                                _logger.LogInformation("Bidirectional: '{ItemName}' already watched in Jellyfin", item.Name);
                            }

                            if (!plexWatchState.Watched)
                            {
                                _logger.LogInformation("Bidirectional: '{ItemName}' should be marked as watched in Plex", item.Name);
                                updates.Add("Mark as watched in Plex");
                            }
                            else
                            {
                                _logger.LogInformation("Bidirectional: '{ItemName}' already watched in Plex", item.Name);
                            }

                            if (updates.Count > 0)
                            {
                                changeDetail.NewState = "Would " + string.Join(" and ", updates);
                                changeDetected = true;
                                _logger.LogInformation("CHANGE DETECTED (Bidirectional-Watched): {Changes}", changeDetail.NewState);
                            }
                            else
                            {
                                _logger.LogInformation("NO CHANGE NEEDED (Bidirectional-Watched): Item already watched in both systems");
                            }
                        }
                        else
                        {
                            // For in-progress items, use the furthest playback position
                            double maxPosition = Math.Max(jellyfinPlaybackPosition, plexWatchState.PlaybackPosition);

                            _logger.LogInformation("Bidirectional in-progress evaluation for '{ItemName}': maxPosition={MaxPos:F0}s (Jellyfin={JellyfinPos:F0}s, Plex={PlexPos:F0}s)",
                                item.Name, maxPosition, jellyfinPlaybackPosition, plexWatchState.PlaybackPosition);

                            if (maxPosition > 0)
                            {
                                List<string> updates = new List<string>();
                                bool jellyfinNeedsUpdate = Math.Abs(jellyfinPlaybackPosition - maxPosition) > 10;
                                bool plexNeedsUpdate = Math.Abs(plexWatchState.PlaybackPosition - maxPosition) > 10;

                                _logger.LogInformation("Position difference check for '{ItemName}': JellyfinNeedsUpdate={JellyfinNeeds} ({JellyfinDiff:F0}s diff), PlexNeedsUpdate={PlexNeeds} ({PlexDiff:F0}s diff)",
                                    item.Name,
                                    jellyfinNeedsUpdate,
                                    Math.Abs(jellyfinPlaybackPosition - maxPosition),
                                    plexNeedsUpdate,
                                    Math.Abs(plexWatchState.PlaybackPosition - maxPosition));

                                if (jellyfinNeedsUpdate)
                                {
                                    updates.Add($"Update Jellyfin position to {maxPosition:F0} seconds");
                                }

                                if (plexNeedsUpdate)
                                {
                                    updates.Add($"Update Plex position to {maxPosition:F0} seconds");
                                }

                                if (updates.Count > 0)
                                {
                                    changeDetail.NewState = "Would " + string.Join(" and ", updates);
                                    changeDetected = true;
                                    _logger.LogInformation("CHANGE DETECTED (Bidirectional-Position): {Changes}", changeDetail.NewState);
                                }
                                else
                                {
                                    _logger.LogInformation("NO CHANGE NEEDED (Bidirectional-Position): Positions within threshold");
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Both positions are 0, no position updates needed.");
                            }
                        }
                    }
                    else if (direction == "PlexToJellyfin")
                    {
                        // One-way sync from Plex to Jellyfin
                        if (plexWatchState.Watched != jellyfinWatched)
                        {
                            changeDetail.NewState = $"Would set Jellyfin watched state to {(plexWatchState.Watched ? "watched" : "unwatched")}";
                            changeDetected = true;
                            _logger.LogInformation("CHANGE DETECTED (PlexToJellyfin-Watched): Plex={PlexWatched}, Jellyfin={JellyfinWatched}",
                                plexWatchState.Watched, jellyfinWatched);
                        }
                        else
                        {
                            _logger.LogInformation("Watched states already match: Plex={PlexWatched}, Jellyfin={JellyfinWatched}",
                                plexWatchState.Watched, jellyfinWatched);

                            // Check for position changes if both are unwatched
                            if (!plexWatchState.Watched && !jellyfinWatched &&
                                    plexWatchState.PlaybackPosition > 0 &&
                                    Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition) > 10)
                            {
                                changeDetail.NewState = $"Would update Jellyfin position to {plexWatchState.PlaybackPosition:F0} seconds";
                                changeDetected = true;
                                _logger.LogInformation("CHANGE DETECTED (PlexToJellyfin-Position): Plex={PlexPos:F0}s, Jellyfin={JellyfinPos:F0}s, Diff={Diff:F0}s",
                                    plexWatchState.PlaybackPosition, jellyfinPlaybackPosition,
                                    Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition));
                            }
                            else if (!plexWatchState.Watched && !jellyfinWatched)
                            {
                                _logger.LogInformation("NO CHANGE NEEDED (PlexToJellyfin-Position): Positions within threshold or both 0: Plex={PlexPos:F0}s, Jellyfin={JellyfinPos:F0}s, Diff={Diff:F0}s",
                                    plexWatchState.PlaybackPosition, jellyfinPlaybackPosition,
                                    Math.Abs(plexWatchState.PlaybackPosition - jellyfinPlaybackPosition));
                            }
                        }
                    }
                    else if (direction == "JellyfinToPlex")
                    {
                        // One-way sync from Jellyfin to Plex
                        if (jellyfinWatched != plexWatchState.Watched)
                        {
                            changeDetail.NewState = $"Would set Plex watched state to {(jellyfinWatched ? "watched" : "unwatched")}";
                            changeDetected = true;
                            _logger.LogInformation("CHANGE DETECTED (JellyfinToPlex-Watched): Jellyfin={JellyfinWatched}, Plex={PlexWatched}",
                                jellyfinWatched, plexWatchState.Watched);
                        }
                        else
                        {
                            _logger.LogInformation("Watched states already match: Jellyfin={JellyfinWatched}, Plex={PlexWatched}",
                                jellyfinWatched, plexWatchState.Watched);

                            // Check for position changes if both are unwatched
                            if (!jellyfinWatched && !plexWatchState.Watched &&
                                    jellyfinPlaybackPosition > 0 &&
                                    Math.Abs(jellyfinPlaybackPosition - plexWatchState.PlaybackPosition) > 10)
                            {
                                changeDetail.NewState = $"Would update Plex position to {jellyfinPlaybackPosition:F0} seconds";
                                changeDetected = true;
                                _logger.LogInformation("CHANGE DETECTED (JellyfinToPlex-Position): Jellyfin={JellyfinPos:F0}s, Plex={PlexPos:F0}s, Diff={Diff:F0}s",
                                    jellyfinPlaybackPosition, plexWatchState.PlaybackPosition,
                                    Math.Abs(jellyfinPlaybackPosition - plexWatchState.PlaybackPosition));
                            }
                            else if (!jellyfinWatched && !plexWatchState.Watched)
                            {
                                _logger.LogInformation("NO CHANGE NEEDED (JellyfinToPlex-Position): Positions within threshold or both 0: Jellyfin={JellyfinPos:F0}s, Plex={PlexPos:F0}s, Diff={Diff:F0}s",
                                    jellyfinPlaybackPosition, plexWatchState.PlaybackPosition,
                                    Math.Abs(jellyfinPlaybackPosition - plexWatchState.PlaybackPosition));
                            }
                        }
                    }

                    if (changeDetected)
                    {
                        changes.Add(changeDetail);
                        _logger.LogInformation("Added change to results list: '{ItemName}' - {State}", item.Name, changeDetail.NewState);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error previewing watch state for item {ItemName} ({ItemId})", item.Name, item.Id);
                }
            }

            // Log the final results
            _logger.LogInformation("Watch state preview completed with direction '{Direction}' - processed {ItemCount} items and found {ChangeCount} potential changes",
                direction, items.Count(), changes.Count);

            if (changes.Count == 0)
            {
                _logger.LogWarning("NO WATCH STATE CHANGES FOUND. This could indicate a problem with Plex IDs, access permissions, or that all items are already in sync.");
            }

            return changes;
        }

        /// <summary>
        /// Finds the corresponding Plex item ID for a Jellyfin item.
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <returns>The Plex item ID if found, otherwise null.</returns>
        private async Task<string?> FindPlexItemIdAsync(BaseItem jellyfinItem, Uri plexServerUrl, string plexToken)
        {
            _logger.LogDebug("Finding Plex ID for '{ItemName}' ({ItemType})",
                jellyfinItem.Name, jellyfinItem.GetType().Name);

            try
            {
                // STRATEGY 1: Check for existing Plex ID in provider IDs
                foreach (var providerKey in new[] { "Plex", "PlexId", "Plex_Id" })
                {
                    if (jellyfinItem.ProviderIds.TryGetValue(providerKey, out var id) && !string.IsNullOrEmpty(id))
                    {
                        _logger.LogInformation("Found direct Plex ID {PlexId} for '{ItemName}'", id, jellyfinItem.Name);
                        return id;
                    }
                }

                var plexApiClient = new PlexApiClient(_httpClientFactory, _logger);

                // STRATEGY 2: Match by external IDs (IMDB, TVDB, TMDB)
                // This is our PRIMARY strategy for accurate matching
                bool hasImdb = jellyfinItem.ProviderIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId);
                bool hasTvdb = jellyfinItem.ProviderIds.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId);
                bool hasTmdb = jellyfinItem.ProviderIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId);

                // Only continue with external ID matching if we have at least one ID to match on
                if (hasImdb || hasTvdb || hasTmdb)
                {
                    // Execute the primary strategy - search Plex libraries for items with matching external IDs
                    string? plexId = await SearchPlexByExternalIdAsync(
                        jellyfinItem, imdbId, tvdbId, plexServerUrl, plexToken, plexApiClient).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(plexId))
                    {
                        _logger.LogInformation("Found Plex ID {PlexId} for '{ItemName}' using external ID match",
                            plexId, jellyfinItem.Name);
                        return plexId;
                    }
                }

                // STRATEGY 3: Title matching (FALLBACK ONLY)
                // Only use title matching as a last resort
                _logger.LogDebug("Using title matching as fallback for '{ItemName}'", jellyfinItem.Name);

                var encodedTitle = Uri.EscapeDataString(jellyfinItem.Name);
                var searchUrl = new Uri(plexServerUrl, $"/library/all?title={encodedTitle}&X-Plex-Token={plexToken}");

                using var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync(searchUrl).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var doc = System.Xml.Linq.XDocument.Parse(content);

                    // Look for any matching media item (Video, Movie, Episode, etc.)
                    var mediaElements = doc.Descendants().Where(e =>
                        e.Name.LocalName == "Video" ||
                        e.Name.LocalName == "Movie" ||
                        e.Name.LocalName == "Episode");

                    var mediaElementsList = mediaElements.ToList();

                    // Try to find exact title match first
                    var exactMatch = mediaElementsList.FirstOrDefault(e =>
                        e.Attribute("title")?.Value?.Equals(jellyfinItem.Name, StringComparison.OrdinalIgnoreCase) == true);

                    if (exactMatch != null)
                    {
                        var plexId = exactMatch.Attribute("ratingKey")?.Value;
                        if (!string.IsNullOrEmpty(plexId))
                        {
                            _logger.LogInformation("Found Plex ID {PlexId} for '{ItemName}' using title match",
                                plexId, jellyfinItem.Name);
                            return plexId;
                        }
                    }

                    // If no exact match, try for an approximate title match
                    if (mediaElementsList.Count > 0)
                    {
                        // Look for the closest title match by checking if title contains the item name or vice versa
                        var fuzzyMatch = mediaElementsList.FirstOrDefault(e => {
                            var title = e.Attribute("title")?.Value;
                            if (string.IsNullOrEmpty(title)) return false;

                            // Check if titles are very similar
                            return title.Contains(jellyfinItem.Name, StringComparison.OrdinalIgnoreCase) ||
                                   jellyfinItem.Name.Contains(title, StringComparison.OrdinalIgnoreCase);
                        });

                        if (fuzzyMatch != null)
                        {
                            var plexId = fuzzyMatch.Attribute("ratingKey")?.Value;
                            var title = fuzzyMatch.Attribute("title")?.Value;

                            if (!string.IsNullOrEmpty(plexId))
                            {
                                _logger.LogInformation("Found approximate title match for '{ItemName}' to '{PlexTitle}' (ID: {PlexId})",
                                    jellyfinItem.Name, title, plexId);
                                return plexId;
                            }
                        }
                    }
                }

                // If we get here, we couldn't find a match using any method
                _logger.LogInformation("Unable to find matching Plex item for '{ItemName}'", jellyfinItem.Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding Plex ID for '{ItemName}'", jellyfinItem.Name);
                return null;
            }
        }

        /// <summary>
        /// Determines the Plex item type based on the Jellyfin item type.
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item.</param>
        /// <returns>The Plex item type or null if not supported.</returns>
        private string? DetermineItemType(BaseItem jellyfinItem)
        {
            var itemType = jellyfinItem.GetType().Name;
            return itemType switch
            {
                "Movie" => "movie",
                "Series" => "show",
                "Season" => "season",
                "Episode" => "episode",
                _ => null
            };
        }

        /// <summary>
        /// Searches for a Plex item by external ID (IMDB or TVDB).
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item.</param>
        /// <param name="imdbId">The IMDB ID.</param>
        /// <param name="tvdbId">The TVDB ID.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <param name="plexApiClient">The Plex API client.</param>
        /// <returns>The Plex item ID if found, otherwise null.</returns>
        private async Task<string?> SearchPlexByExternalIdAsync(
            BaseItem jellyfinItem,
            string? imdbId,
            string? tvdbId,
            Uri plexServerUrl,
            string plexToken,
            PlexApiClient plexApiClient)
        {
            try
            {
                _logger.LogDebug("Searching for external ID match - IMDB: {ImdbId}, TVDB: {TvdbId}", imdbId, tvdbId);

                // Directly query all Plex libraries for a complete list of items with their metadata
                var librariesUrl = new Uri(plexServerUrl, $"/library/sections?X-Plex-Token={plexToken}");

                using var client = _httpClientFactory.CreateClient();
                var libResponse = await client.GetAsync(librariesUrl).ConfigureAwait(false);

                if (!libResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get Plex libraries: {Status}", libResponse.StatusCode);
                    return null;
                }

                var libContent = await libResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var libDoc = System.Xml.Linq.XDocument.Parse(libContent);

                // Extract library IDs
                var libraries = libDoc.Descendants()
                    .Where(e => e.Name.LocalName == "Directory" &&
                                (e.Attribute("type")?.Value == "movie" ||
                                 e.Attribute("type")?.Value == "show"))
                    .ToList();

                _logger.LogDebug("Found {Count} relevant Plex libraries", libraries.Count);

                foreach (var library in libraries)
                {
                    var libraryId = library.Attribute("key")?.Value;
                    var libraryTitle = library.Attribute("title")?.Value;

                    if (string.IsNullOrEmpty(libraryId)) continue;

                    _logger.LogDebug("Searching library: {Title}", libraryTitle);

                    // Query all items in this library and check for external ID matches
                    // Use the "includeGuids=1" parameter to get all external IDs directly
                    var allItemsUrl = new Uri(plexServerUrl, $"/library/sections/{libraryId}/all?includeGuids=1&X-Plex-Token={plexToken}");

                    var itemsResponse = await client.GetAsync(allItemsUrl).ConfigureAwait(false);

                    if (!itemsResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Failed to get items from library '{Title}': {Status}",
                            libraryTitle, itemsResponse.StatusCode);
                        continue;
                    }

                    var itemsContent = await itemsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var itemsDoc = System.Xml.Linq.XDocument.Parse(itemsContent);

                    // Find all media items (Video, Movie, Episode, etc.)
                    var mediaElements = itemsDoc.Descendants().Where(e =>
                        e.Name.LocalName == "Video" ||
                        e.Name.LocalName == "Movie" ||
                        e.Name.LocalName == "Episode").ToList();

                    _logger.LogDebug("Found {Count} media items in library '{Library}'",
                        mediaElements.Count, libraryTitle);

                    // Check each item for matching external IDs
                    foreach (var element in mediaElements)
                    {
                        var title = element.Attribute("title")?.Value;
                        var plexId = element.Attribute("ratingKey")?.Value;

                        if (string.IsNullOrEmpty(plexId)) continue;

                        // Look for Guid elements which contain the external IDs
                        var guidElements = element.Elements().Where(e => e.Name.LocalName == "Guid").ToList();

                        foreach (var guid in guidElements)
                        {
                            var guidId = guid.Attribute("id")?.Value;
                            if (string.IsNullOrEmpty(guidId)) continue;

                            // Check for IMDB match
                            if (!string.IsNullOrEmpty(imdbId) &&
                                (guidId.Contains(imdbId, StringComparison.OrdinalIgnoreCase) ||
                                 guidId.Contains($"imdb://{imdbId}", StringComparison.OrdinalIgnoreCase) ||
                                 guidId.EndsWith($"/{imdbId}", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogDebug("Found IMDB match: {ImdbId} matches guid {GuidId} for '{Title}'",
                                    imdbId, guidId, title);
                                return plexId;
                            }

                            // Check for TVDB match
                            if (!string.IsNullOrEmpty(tvdbId) &&
                                (guidId.Contains(tvdbId, StringComparison.OrdinalIgnoreCase) ||
                                 guidId.Contains($"tvdb://{tvdbId}", StringComparison.OrdinalIgnoreCase) ||
                                 guidId.EndsWith($"/{tvdbId}", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogDebug("Found TVDB match: {TvdbId} matches guid {GuidId} for '{Title}'",
                                    tvdbId, guidId, title);
                                return plexId;
                            }

                            // Check for TMDB match
                            if (jellyfinItem.ProviderIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId) &&
                                (guidId.Contains(tmdbId, StringComparison.OrdinalIgnoreCase) ||
                                 guidId.Contains($"tmdb://{tmdbId}", StringComparison.OrdinalIgnoreCase) ||
                                 guidId.EndsWith($"/{tmdbId}", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogDebug("Found TMDB match: {TmdbId} matches guid {GuidId} for '{Title}'",
                                    tmdbId, guidId, title);
                                return plexId;
                            }
                        }
                    }
                }

                _logger.LogDebug("No external ID match found in any Plex library");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in external ID search for '{ItemName}'", jellyfinItem.Name);
                return null;
            }
        }

        /// <summary>
        /// Searches for a Plex item by title and type.
        /// </summary>
        /// <param name="title">The item title.</param>
        /// <param name="itemType">The item type.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <param name="plexApiClient">The Plex API client.</param>
        /// <returns>The Plex item ID if found, otherwise null.</returns>
        private async Task<string?> SearchPlexByTitleAndTypeAsync(
            string title,
            string itemType,
            Uri plexServerUrl,
            string plexToken,
            PlexApiClient plexApiClient)
        {
            try
            {
                _logger.LogWarning("TITLE SEARCH: Searching for '{Title}' with type '{ItemType}'", title, itemType);

                // Construct a search query for the title
                string encodedTitle = Uri.EscapeDataString(title);
                var searchUrl = new Uri(plexServerUrl, $"/library/all?title={encodedTitle}&type={itemType}&X-Plex-Token={plexToken}");
                var redactedUrl = searchUrl.ToString().Replace(plexToken, "TOKEN-REDACTED");
                _logger.LogWarning("Title search URL: {Url}", redactedUrl);

                // Try searching with sections endpoint as well, which might return different results
                var sectionsSearchUrl = new Uri(plexServerUrl, $"/library/sections/all?title={encodedTitle}&type={itemType}&X-Plex-Token={plexToken}");
                var redactedSectionsUrl = sectionsSearchUrl.ToString().Replace(plexToken, "TOKEN-REDACTED");
                _logger.LogWarning("Alternative sections search URL: {Url}", redactedSectionsUrl);

                using var client = _httpClientFactory.CreateClient();

                // First try the primary search URL
                var response = await client.GetAsync(searchUrl);
                _logger.LogWarning("Title search response: Status={StatusCode}, Reason={ReasonPhrase}",
                    response.StatusCode, response.ReasonPhrase);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Log a sample of the response for debugging
                    var contentPreview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                    _logger.LogWarning("Title search response body (excerpt):\n{Content}", contentPreview);

                    var doc = System.Xml.Linq.XDocument.Parse(content);

                    // Log all tags in the document
                    var allTags = doc.Descendants().Select(e => e.Name.LocalName).Distinct().ToList();
                    _logger.LogWarning("XML tags in response: {Tags}", string.Join(", ", allTags));

                    // Also try to get the MediaContainer element and check if it has a size attribute
                    var mediaContainer = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "MediaContainer");
                    if (mediaContainer != null)
                    {
                        var sizeAttr = mediaContainer.Attribute("size")?.Value;
                        _logger.LogWarning("MediaContainer size attribute: {Size}", sizeAttr ?? "null");
                    }

                    // Look for any matching media item (Video, Movie, Episode, etc.)
                    var mediaElements = doc.Descendants().Where(e =>
                        e.Name.LocalName == "Video" ||
                        e.Name.LocalName == "Movie" ||
                        e.Name.LocalName == "Episode");

                    var mediaElementsList = mediaElements.ToList();
                    _logger.LogWarning("Found {Count} matching media elements in response", mediaElementsList.Count);

                    foreach (var element in mediaElementsList)
                    {
                        _logger.LogWarning("Media element: Type={Type}, Title='{Title}', RatingKey={RatingKey}",
                            element.Name.LocalName,
                            element.Attribute("title")?.Value,
                            element.Attribute("ratingKey")?.Value);

                        // Log all attributes for deeper inspection
                        var allAttributes = element.Attributes().Select(a => $"{a.Name.LocalName}='{a.Value}'");
                        _logger.LogWarning("All attributes: {Attributes}", string.Join(", ", allAttributes));
                    }

                    // Try to find exact title match first
                    var exactMatch = mediaElementsList.FirstOrDefault(e =>
                        e.Attribute("title")?.Value?.Equals(title, StringComparison.OrdinalIgnoreCase) == true);

                    if (exactMatch != null)
                    {
                        var plexId = exactMatch.Attribute("ratingKey")?.Value;
                        if (!string.IsNullOrEmpty(plexId))
                        {
                            _logger.LogWarning("EXACT MATCH FOUND: Plex ID={PlexId}, Title='{Title}'",
                                plexId, exactMatch.Attribute("title")?.Value);
                            return plexId;
                        }
                        else
                        {
                            _logger.LogWarning("Exact title match found but has no ratingKey attribute");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No exact title match found for '{Title}'", title);
                    }

                    // If no exact match, take the first result if available
                    var mediaElement = mediaElementsList.FirstOrDefault();
                    if (mediaElement != null)
                    {
                        var plexId = mediaElement.Attribute("ratingKey")?.Value;
                        if (!string.IsNullOrEmpty(plexId))
                        {
                            _logger.LogWarning("APPROXIMATE MATCH FOUND: Plex ID={PlexId}, Title='{Title}'",
                                plexId, mediaElement.Attribute("title")?.Value);
                            return plexId;
                        }
                        else
                        {
                            _logger.LogWarning("Approximate match found but has no ratingKey attribute");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No results found in Plex for title: '{Title}', type: {ItemType}", title, itemType);
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed title search request. Status: {StatusCode}, Content: {Content}",
                        response.StatusCode, errorContent);
                }

                // Try a broader search without the type parameter as a fallback
                _logger.LogWarning("FALLBACK: Trying broader search without type parameter");
                var broadSearchUrl = new Uri(plexServerUrl, $"/library/all?title={encodedTitle}&X-Plex-Token={plexToken}");
                var redactedBroadUrl = broadSearchUrl.ToString().Replace(plexToken, "TOKEN-REDACTED");
                _logger.LogWarning("Broad search URL: {Url}", redactedBroadUrl);

                var broadResponse = await client.GetAsync(broadSearchUrl);
                _logger.LogWarning("Broad search response: Status={StatusCode}, Reason={ReasonPhrase}",
                    broadResponse.StatusCode, broadResponse.ReasonPhrase);

                if (broadResponse.IsSuccessStatusCode)
                {
                    var broadContent = await broadResponse.Content.ReadAsStringAsync();
                    var broadPreview = broadContent.Length > 500 ? broadContent.Substring(0, 500) + "..." : broadContent;
                    _logger.LogWarning("Broad search response (excerpt):\n{Content}", broadPreview);

                    var broadDoc = System.Xml.Linq.XDocument.Parse(broadContent);
                    var broadMediaElements = broadDoc.Descendants().Where(e =>
                        e.Name.LocalName == "Video" ||
                        e.Name.LocalName == "Movie" ||
                        e.Name.LocalName == "Episode");

                    var broadResultsList = broadMediaElements.ToList();
                    _logger.LogWarning("Found {Count} results in broad search", broadResultsList.Count);

                    if (broadResultsList.Count > 0)
                    {
                        foreach (var element in broadResultsList.Take(5)) // Log first 5 results at most
                        {
                            _logger.LogWarning("Broad search result: Type={Type}, Title='{Title}', RatingKey={RatingKey}",
                                element.Name.LocalName,
                                element.Attribute("title")?.Value,
                                element.Attribute("ratingKey")?.Value);
                        }

                        // Try exact title match in broad search
                        var broadExactMatch = broadResultsList.FirstOrDefault(e =>
                            e.Attribute("title")?.Value?.Equals(title, StringComparison.OrdinalIgnoreCase) == true);

                        if (broadExactMatch != null)
                        {
                            var plexId = broadExactMatch.Attribute("ratingKey")?.Value;
                            if (!string.IsNullOrEmpty(plexId))
                            {
                                _logger.LogWarning("BROAD SEARCH EXACT MATCH: Plex ID={PlexId}, Title='{Title}', Type={Type}",
                                    plexId,
                                    broadExactMatch.Attribute("title")?.Value,
                                    broadExactMatch.Name.LocalName);
                                return plexId;
                            }
                        }
                    }
                }

                _logger.LogWarning("Title search complete - no matches found for '{Title}'", title);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION in title search for 'Wicked'");
                return null;
            }
        }
    }
}
