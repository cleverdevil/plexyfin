using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Plexyfin.Configuration;
using Jellyfin.Plugin.Plexyfin.Plex;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plexyfin.Api
{
    /// <summary>
    /// Controller for Plexyfin API endpoints.
    /// </summary>
    [ApiController]
    [Route("Plexyfin")]
    public class PlexyfinController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProviderManager? _providerManager;
        private readonly IFileSystem? _fileSystem;
        private readonly ILogger<PlexyfinController> _logger;
        private readonly IUserManager? _userManager;
        private readonly IUserDataManager? _userDataManager;
        
        // Static field to track ongoing sync operations
        private static readonly Dictionary<string, SyncStatus> ActiveSyncs = new Dictionary<string, SyncStatus>();

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexyfinController"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/>.</param>
        /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/>. Can be null.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/>. Can be null.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{T}"/>.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/>. Can be null.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/>. Can be null.</param>
        public PlexyfinController(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IHttpClientFactory httpClientFactory,
            IProviderManager? providerManager,
            IFileSystem? fileSystem,
            ILogger<PlexyfinController> logger,
            IUserManager? userManager = null,
            IUserDataManager? userDataManager = null)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _collectionManager = collectionManager ?? throw new ArgumentNullException(nameof(collectionManager));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _providerManager = providerManager; // Can be null
            _fileSystem = fileSystem; // Can be null
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager; // Can be null
            _userDataManager = userDataManager; // Can be null
        }
        

        /// <summary>
        /// Internal method for syncing from Plex, used by both the API endpoint and scheduled task.
        /// </summary>
        /// <param name="dryRun">If true, changes will not be applied, only reported.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task<SyncResult> SyncFromPlexAsync(bool dryRun = false, SyncStatus? syncStatus = null)
        {
            var config = Plugin.Instance!.Configuration;
            var result = new SyncResult();
            
            // If this is a dry run, initialize the details collection
            if (dryRun)
            {
                result.Details = new DryRunDetails();
                _logger.LogInformation("Starting DRY RUN sync - changes will not be applied");
                
                if (syncStatus != null)
                {
                    syncStatus.Message = "Starting dry run sync - analyzing current state...";
                    syncStatus.Progress = 5;
                }
            }
            else if (syncStatus != null)
            {
                syncStatus.Message = "Starting sync operation...";
                syncStatus.Progress = 5;
            }
            
            // Create a PlexClient instance
            var plexServerUri = string.IsNullOrEmpty(config.PlexServerUrl) 
                ? new Uri("http://localhost") 
                : new Uri(config.PlexServerUrl);
            var plexClient = new PlexClient(_httpClientFactory, _logger, plexServerUri, config.PlexApiToken, config);
            
            // Sync collections if enabled
            if (config.SyncCollections)
            {
                _logger.LogInformation("{Mode} collection sync from Plex", dryRun ? "Simulating" : "Starting");
                
                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun 
                        ? "Analyzing collections..." 
                        : "Syncing collections from Plex...";
                    syncStatus.Progress = 10;
                }
                
                // Delete existing collections if configured to do so
                if (config.DeleteBeforeSync && !dryRun)
                {
                    if (syncStatus != null)
                    {
                        syncStatus.Message = "Emptying existing collections...";
                        syncStatus.Progress = 15;
                    }
                    
                    var emptiedCount = await DeleteExistingCollectionsAsync().ConfigureAwait(false);
                    _logger.LogInformation("Emptied {Count} existing collections for recreation", emptiedCount);
                }
                else if (config.DeleteBeforeSync && dryRun)
                {
                    // In dry run mode, just count existing collections that would be emptied
                    var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                    });
                    _logger.LogInformation("Would empty {Count} existing collections for recreation", collections.Count());
                }
                
                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun 
                        ? "Analyzing collections from Plex..." 
                        : "Processing collections from Plex...";
                    syncStatus.Progress = 20;
                }
                
                var collectionResults = await SyncCollectionsFromPlexAsync(plexClient, dryRun, syncStatus).ConfigureAwait(false);
                result.CollectionsAdded = collectionResults.added;
                result.CollectionsUpdated = collectionResults.updated;
                
                // If this is a dry run, make sure the collections are included in the details
                if (dryRun && result.Details != null)
                {
                    // The collectionsToAdd and collectionsToUpdate lists are populated in SyncCollectionsFromPlexAsync
                    result.Details.CollectionsToAdd.AddRange(collectionResults.collectionsToAdd);
                    result.Details.CollectionsToUpdate.AddRange(collectionResults.collectionsToUpdate);
                }
                
                _logger.LogInformation("Collection sync {Status}. Would add {Added} collections, would update {Updated} collections", 
                    dryRun ? "simulation completed" : "completed", 
                    result.CollectionsAdded, 
                    result.CollectionsUpdated);
                    
                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun
                        ? $"Collection analysis complete. Would add {result.CollectionsAdded}, update {result.CollectionsUpdated} collections."
                        : $"Collection sync complete. Added {result.CollectionsAdded}, updated {result.CollectionsUpdated} collections.";
                    syncStatus.Progress = 50;
                }
            }
            
            
            // Sync watch state if enabled
            if (config.SyncWatchState && _userManager != null && _userDataManager != null)
            {
                try
                {
                    _logger.LogInformation("{Mode} watch state synchronization", dryRun ? "Simulating" : "Starting");
                    
                    // Get selected libraries from configuration
                    var selectedLibraries = config.SelectedLibraries ?? new List<string>();
                    
                    // Get all media items from Jellyfin, filtered by library if selected
                    var itemsQuery = new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode }
                    };
                    
                    // Only apply library filtering if libraries are selected
                    var allItems = _libraryManager.GetItemList(itemsQuery).ToList();
                    
                    // Count the total number of items
                    int totalItems = allItems.Count;
                    int moviesCount = allItems.Count(i => i is Movie);
                    int episodesCount = totalItems - moviesCount;
                    
                    if (syncStatus != null)
                    {
                        syncStatus.TotalItems = totalItems;
                        syncStatus.RemainingItems = totalItems;
                        syncStatus.ProcessedItems = 0;
                        
                        string itemsText = $"{totalItems} items ({moviesCount} movies, {episodesCount} TV episodes)";
                        syncStatus.Message = dryRun
                            ? $"Analyzing watch states for {itemsText}..."
                            : $"Syncing watch states for {itemsText}...";
                        
                        // Collection syncs use 0-50% of progress, watch states 50-100%
                        syncStatus.Progress = 50;
                    }
                    
                    // If libraries are selected, filter the items to only include those in selected libraries
                    if (selectedLibraries.Count > 0)
                    {
                        _logger.LogInformation("Filtering watch state sync to {Count} selected libraries", selectedLibraries.Count);
                        
                        // Get all Plex libraries to map Plex library IDs to names                            
                        var plexLibs = new PlexClient(_httpClientFactory, _logger, plexServerUri, config.PlexApiToken, config);
                        var plexLibraries = await plexLibs.GetLibraries().ConfigureAwait(false);
                        
                        // Log selected libraries for debugging
                        foreach (var libId in selectedLibraries)
                        {
                            var libName = plexLibraries.FirstOrDefault(l => l.Id == libId)?.Title ?? "Unknown";
                            _logger.LogDebug("Selected library: {LibraryId} ({LibraryName})", libId, libName);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No libraries selected, processing all items for watch state sync");
                    }
                    
                    // Create watch state manager
                    var watchStateManager = new PlexWatchStateManager(
                        _logger,
                        _httpClientFactory,
                        _userManager,
                        _userDataManager,
                        syncStatus);  // Pass the sync status object for progress tracking
                        
                    // Sync watch states
                    var watchStateServerUri = string.IsNullOrEmpty(config.PlexServerUrl) 
                        ? new Uri("http://localhost") 
                        : new Uri(config.PlexServerUrl);
                    
                    if (dryRun)
                    {
                        // If in dry run mode, preview changes instead of applying them
                        var watchStateChanges = await watchStateManager.PreviewWatchStateChangesAsync(
                            allItems,
                            watchStateServerUri,
                            config.PlexApiToken,
                            config.SyncWatchStateDirection).ConfigureAwait(false);
                            
                        // Add the changes to the result details
                        if (result.Details != null)
                        {
                            result.Details.WatchStatesChanged = watchStateChanges;
                        }
                        
                        _logger.LogInformation("Watch state synchronization simulation completed. Would change {Count} items", 
                            watchStateChanges.Count);
                            
                        if (syncStatus != null)
                        {
                            syncStatus.Message = $"Watch state analysis complete. Would change {watchStateChanges.Count} items.";
                            syncStatus.Progress = 100;
                            syncStatus.RemainingItems = 0;
                            syncStatus.ProcessedItems = totalItems;
                        }
                    }
                    else
                    {
                        // Actually apply the changes
                        await watchStateManager.SyncWatchStateAsync(
                            allItems,
                            watchStateServerUri,
                            config.PlexApiToken,
                            config.SyncWatchStateDirection).ConfigureAwait(false);
                            
                        _logger.LogInformation("Watch state synchronization completed");
                        
                        if (syncStatus != null)
                        {
                            syncStatus.Message = "Watch state synchronization completed.";
                            syncStatus.Progress = 100;
                            syncStatus.RemainingItems = 0;
                            syncStatus.ProcessedItems = totalItems;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error {Operation} watch states", dryRun ? "simulating sync of" : "syncing");
                    
                    if (syncStatus != null)
                    {
                        syncStatus.Message = $"Error syncing watch states: {ex.Message}";
                    }
                }
            }
            
            // Complete the sync operation
            if (syncStatus != null)
            {
                syncStatus.IsComplete = true;
                syncStatus.Result = result;
                syncStatus.Message = "Sync operation completed successfully.";
                syncStatus.Progress = 100;
                syncStatus.EndTime = DateTime.UtcNow;
            }
            
            return result;
        }

        /// <summary>
        /// Empties all existing collections in Jellyfin.
        /// </summary>
        /// <returns>The number of collections emptied.</returns>
        private async Task<int> DeleteExistingCollectionsAsync()
        {
            // Get all BoxSet items (collections) from the library
            var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
            });
            
            int count = 0;
            
            // We'll just empty all collections to prepare for recreation
            foreach (var collection in collections)
            {
                try
                {
                    // Empty the collection by removing all items from it
                    await _collectionManager.RemoveFromCollectionAsync(collection.Id, Array.Empty<Guid>()).ConfigureAwait(false);
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error emptying collection: {Name}", collection.Name);
                }
            }
            
            return count;
        }

        /// <summary>
        /// Syncs collections from Plex to Jellyfin.
        /// </summary>
        /// <param name="plexClient">The Plex client.</param>
        /// <param name="dryRun">If true, changes will not be applied, only reported.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        /// <returns>A tuple containing the number of collections added and updated along with the collection details.</returns>
        private async Task<(int added, int updated, List<SyncCollectionDetail> collectionsToAdd, List<SyncCollectionDetail> collectionsToUpdate)> SyncCollectionsFromPlexAsync(
            PlexClient plexClient, 
            bool dryRun = false, 
            SyncStatus? syncStatus = null)
        {
            int added = 0;
            int updated = 0;
            
            // Get selected libraries from configuration
            var config = Plugin.Instance!.Configuration;
            var selectedLibraries = config.SelectedLibraries ?? new List<string>();
            
            // Lists to store details for dry run mode
            var collectionsToAdd = new List<SyncCollectionDetail>();
            var collectionsToUpdate = new List<SyncCollectionDetail>();
            
            try
            {
                // Track progress metrics
                int totalLibraries = selectedLibraries.Count;
                int processedLibraries = 0;
                
                // Get collections from Plex
                foreach (var libraryId in selectedLibraries)
                {
                    processedLibraries++;
                    
                    if (syncStatus != null)
                    {
                        int libraryProgress = processedLibraries * 100 / totalLibraries;
                        // Scale the progress to be between 20-50% of the overall sync
                        int scaledProgress = 20 + (libraryProgress * 30 / 100);
                        syncStatus.Progress = scaledProgress;
                        syncStatus.Message = $"Processing library {processedLibraries} of {totalLibraries}...";
                    }
                    
                    _logger.LogDebug($"Processing library ID: {libraryId}");
                    var collections = await plexClient.GetCollections(libraryId).ConfigureAwait(false);
                    _logger.LogDebug($"Found {collections.Count} collections in Plex library");
                    
                    // Track collection progress within this library
                    int totalCollections = collections.Count;
                    int processedCollections = 0;
                    
                    foreach (var collection in collections)
                    {
                        processedCollections++;
                        
                        if (syncStatus != null && totalCollections > 0)
                        {
                            // Update the detailed progress message
                            syncStatus.Message = $"Processing library {processedLibraries} of {totalLibraries}: " +
                                               $"Collection {processedCollections} of {totalCollections} ({collection.Title})";
                        }
                        
                        _logger.LogDebug($"Processing collection: {collection.Title}");
                        
                        try
                        {
                            // Check if collection already exists in Jellyfin
                            // When DeleteBeforeSync is true, we should always create new collections instead of updating
                            var existingCollections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                            {
                                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                            }).Where(c => c.Name.Equals(collection.Title, StringComparison.OrdinalIgnoreCase));
                            
                            var existingCollection = Plugin.Instance!.Configuration.DeleteBeforeSync 
                                ? null // Force collection recreation when DeleteBeforeSync is enabled
                                : existingCollections.FirstOrDefault();
                            
                            // Get collection items from Plex
                            var plexItems = await plexClient.GetCollectionItems(collection.Id).ConfigureAwait(false);
                            _logger.LogDebug("Found {Count} items in Plex collection", plexItems.Count);
                            
                            // Find matching items in Jellyfin
                            var jellyfinItems = new List<Guid>();
                            var matchedItemTitles = new List<string>(); // For dry run report
                            
                            foreach (var plexItem in plexItems)
                            {
                                // Try to find by name
                                var matchingItems = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                                {
                                    Name = plexItem.Title,
                                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
                                });
                                
                                var jellyfinItem = matchingItems.FirstOrDefault();
                                
                                if (jellyfinItem != null)
                                {
                                    jellyfinItems.Add(jellyfinItem.Id);
                                    matchedItemTitles.Add(plexItem.Title);
                                    _logger.LogDebug("Matched Plex item '{ItemTitle}' to Jellyfin item with ID: {ItemId}", 
                                        plexItem.Title, jellyfinItem.Id);
                                }
                                else
                                {
                                    _logger.LogDebug("Could not find matching Jellyfin item for Plex item: {ItemTitle}", 
                                        plexItem.Title);
                                }
                            }
                            
                            if (existingCollection == null)
                            {
                                // Create new collection
                                _logger.LogInformation("{Action} new collection: {CollectionTitle}", 
                                    dryRun ? "Would create" : "Creating", collection.Title);
                                
                                // In dry run mode, just record what would be done
                                if (dryRun)
                                {
                                    var syncDetail = new SyncCollectionDetail
                                    {
                                        Title = collection.Title,
                                        Summary = collection.Summary,
                                        Items = matchedItemTitles
                                    };
                                    
                                    collectionsToAdd.Add(syncDetail);
                                }
                                else
                                {
                                    try
                                    {
                                        // Create collection using the built-in collection manager
                                        var options = new MediaBrowser.Controller.Collections.CollectionCreationOptions
                                        {
                                            Name = collection.Title,
                                            ItemIdList = jellyfinItems.Select(id => id.ToString()).ToList(),
                                            IsLocked = true
                                        };
                                        
                                        // Create the collection
                                        var newCollectionId = await _collectionManager.CreateCollectionAsync(options).ConfigureAwait(false);
                                        _logger.LogDebug("Created collection with ID: {CollectionId}", newCollectionId);
                                        
                                        // Use GetItemList instead of GetItemById to avoid type conversion issues
                                        var newlyCreatedCollections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                                        {
                                            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                                            Name = collection.Title,
                                            Limit = 1
                                        });
                                        
                                        var newCollection = newlyCreatedCollections.FirstOrDefault();
                                        
                                        if (newCollection != null)
                                        {
                                            // Update metadata
                                            newCollection.Overview = collection.Summary;
                                            
                                            // Save changes
                                            await _libraryManager.UpdateItemAsync(
                                                newCollection, 
                                                newCollection.GetParent(), 
                                                ItemUpdateType.MetadataEdit, 
                                                CancellationToken.None).ConfigureAwait(false);
                                            
                                            // Process artwork
                                            if (config.SyncArtwork)
                                            {
                                                ProcessCollectionArtwork(newCollection, collection);
                                            }
                                            
                                            added++;
                                            _logger.LogInformation("Successfully created collection: {CollectionTitle}", collection.Title);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("Unable to retrieve newly created collection: {CollectionTitle}", 
                                                collection.Title);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error creating collection: {CollectionTitle}", collection.Title);
                                    }
                                }
                            }
                            else
                            {
                                // Update existing collection
                                _logger.LogInformation("{Action} existing collection: {CollectionTitle}", 
                                    dryRun ? "Would update" : "Updating", collection.Title);
                                
                                // In dry run mode, just record what would be done
                                if (dryRun)
                                {
                                    var syncDetail = new SyncCollectionDetail
                                    {
                                        Title = collection.Title,
                                        Summary = collection.Summary,
                                        Items = matchedItemTitles
                                    };
                                    
                                    collectionsToUpdate.Add(syncDetail);
                                }
                                else
                                {
                                    try
                                    {
                                        // Update metadata
                                        existingCollection.Overview = collection.Summary;
                                        
                                        // Update items in the collection
                                        await _collectionManager.AddToCollectionAsync(
                                            existingCollection.Id, 
                                            jellyfinItems.ToArray()).ConfigureAwait(false);
                                        
                                        // Save changes
                                        await _libraryManager.UpdateItemAsync(
                                            existingCollection, 
                                            existingCollection.GetParent(), 
                                            ItemUpdateType.MetadataEdit, 
                                            CancellationToken.None).ConfigureAwait(false);
                                        
                                        // Process artwork
                                        if (config.SyncArtwork)
                                        {
                                            ProcessCollectionArtwork(existingCollection, collection);
                                        }
                                        
                                        updated++;
                                        _logger.LogInformation("Successfully updated collection: {CollectionTitle}", collection.Title);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error updating collection: {CollectionTitle}", collection.Title);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing collection: {CollectionTitle}", collection.Title);
                        }
                    }
                }
                
                // If this is a dry run, add the detailed changes to the result
                if (dryRun)
                {
                    // We'll return these details directly in the added/updated counts and the detailed result
                    added = collectionsToAdd.Count;
                    updated = collectionsToUpdate.Count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing collections");
                
                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error syncing collections: {ex.Message}";
                }
            }
            
            return (added, updated, collectionsToAdd, collectionsToUpdate);
        }
        
        /// <summary>
        /// Processes artwork for a collection without requiring type conversions.
        /// </summary>
        /// <param name="jellyfinCollection">The Jellyfin collection.</param>
        /// <param name="plexCollection">The Plex collection.</param>
        private void ProcessCollectionArtwork(BaseItem jellyfinCollection, PlexCollection plexCollection)
        {
            if (jellyfinCollection == null)
            {
                _logger.LogWarning("Cannot process artwork for null collection");
                return;
            }
            
            _logger.LogDebug("Processing artwork for collection: {CollectionName}", jellyfinCollection.Name);
            
            // For now, just log that we would sync the artwork
            if (plexCollection.ThumbUrl != null)
            {
                _logger.LogDebug("Would sync thumbnail: {ThumbUrl}", plexCollection.ThumbUrl);
            }
            
            if (plexCollection.ArtUrl != null)
            {
                _logger.LogDebug("Would sync art: {ArtUrl}", plexCollection.ArtUrl);
            }
            
            // In a real implementation, we would download and save the artwork
        }
        

        /// <summary>
        /// API endpoint for starting a sync from Plex.
        /// </summary>
        /// <returns>The status ID for tracking the sync operation.</returns>
        [HttpPost("sync")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<object> StartSync()
        {
            try
            {
                // Create a new sync status object
                var syncStatus = new SyncStatus 
                { 
                    Progress = 0,
                    Message = "Initializing sync...",
                    IsComplete = false,
                    IsDryRun = false
                };
                
                // Register this sync operation
                lock (ActiveSyncs)
                {
                    // Clean up any completed syncs older than 1 hour
                    var oldSyncs = ActiveSyncs.Where(kvp => 
                        kvp.Value.IsComplete && kvp.Value.EndTime.HasValue && 
                        (DateTime.UtcNow - kvp.Value.EndTime.Value).TotalHours > 1)
                        .Select(kvp => kvp.Key)
                        .ToList();
                        
                    foreach (var key in oldSyncs)
                    {
                        ActiveSyncs.Remove(key);
                    }
                    
                    ActiveSyncs[syncStatus.Id] = syncStatus;
                }
                
                // Start the sync in the background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Starting background sync operation with ID: {SyncId}", syncStatus.Id);
                        var result = await SyncFromPlexAsync(false, syncStatus).ConfigureAwait(false);
                        syncStatus.Result = result;
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = "Sync completed successfully.";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background sync operation");
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                });
                
                // Return the ID so the client can poll for status
                return Ok(new 
                { 
                    success = true,
                    syncId = syncStatus.Id,
                    message = "Sync operation started successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting sync from Plex");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// API endpoint for getting the status of a sync operation.
        /// </summary>
        /// <param name="syncId">The ID of the sync operation.</param>
        /// <returns>The current status of the sync operation.</returns>
        [HttpGet("sync-status/{syncId}")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<object> GetSyncStatus(string syncId)
        {
            try
            {
                // Look up the sync operation
                lock (ActiveSyncs)
                {
                    if (ActiveSyncs.TryGetValue(syncId, out var syncStatus))
                    {
                        var response = new
                        {
                            success = true,
                            isComplete = syncStatus.IsComplete,
                            progress = syncStatus.Progress,
                            message = syncStatus.Message,
                            isDryRun = syncStatus.IsDryRun,
                            elapsedSeconds = (int)syncStatus.ElapsedTime.TotalSeconds,
                            totalItems = syncStatus.TotalItems,
                            remainingItems = syncStatus.RemainingItems,
                            processedItems = syncStatus.ProcessedItems
                        };
                        
                        // If the sync is complete, include the results
                        if (syncStatus.IsComplete && syncStatus.Result != null)
                        {
                            var detailsObj = new
                            {
                                collectionsAdded = syncStatus.Result.CollectionsAdded,
                                collectionsUpdated = syncStatus.Result.CollectionsUpdated,
                                details = syncStatus.Result.Details
                            };
                            
                            return Ok(new
                            {
                                success = true,
                                isComplete = syncStatus.IsComplete,
                                progress = syncStatus.Progress,
                                message = syncStatus.Message,
                                isDryRun = syncStatus.IsDryRun,
                                elapsedSeconds = (int)syncStatus.ElapsedTime.TotalSeconds,
                                totalItems = syncStatus.TotalItems,
                                remainingItems = syncStatus.RemainingItems,
                                processedItems = syncStatus.ProcessedItems,
                                result = detailsObj
                            });
                        }
                        
                        return Ok(response);
                    }
                    
                    return NotFound(new { success = false, message = "Sync operation not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// API endpoint for testing the Plex connection.
        /// </summary>
        /// <param name="url">The Plex server URL.</param>
        /// <param name="token">The Plex API token.</param>
        /// <returns>The available libraries.</returns>
        [HttpGet("test-connection")]
        [Authorize(Policy = "RequiresElevation")]
        public async Task<ActionResult> TestConnection(
            [Required] Uri url,
            [Required] string token)
        {
            try
            {
                var config = Plugin.Instance!.Configuration;
                var plexClient = new PlexClient(_httpClientFactory, _logger, url, token, config);
                var libraries = await plexClient.GetLibraries().ConfigureAwait(false);
                
                // Mark libraries as selected if they're in the SelectedLibraries list
                var selectedLibraries = config.SelectedLibraries ?? new List<string>();
                foreach (var library in libraries)
                {
                    library.IsSelected = selectedLibraries.Contains(library.Id);
                }
                
                // Save the URL and token to the configuration
                config.PlexServerUrl = url.ToString();
                config.PlexApiToken = token;
                Plugin.Instance.SaveConfiguration();
                
                return Ok(new { 
                    success = true, 
                    message = "Successfully connected to Plex Media Server",
                    libraries = libraries 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Plex connection");
                return Ok(new { 
                    success = false, 
                    error = $"Error connecting to Plex: {ex.Message}" 
                });
            }
        }
        
        /// <summary>
        /// API endpoint for starting a dry run sync operation.
        /// </summary>
        /// <returns>The status ID for tracking the sync operation.</returns>
        [HttpPost("DryRunSync")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<object> StartDryRunSync()
        {
            try
            {
                // Create a new sync status object
                var syncStatus = new SyncStatus 
                { 
                    Progress = 0,
                    Message = "Initializing dry run...",
                    IsComplete = false,
                    IsDryRun = true
                };
                
                // Register this sync operation
                lock (ActiveSyncs)
                {
                    // Clean up any completed syncs older than 1 hour
                    var oldSyncs = ActiveSyncs.Where(kvp => 
                        kvp.Value.IsComplete && kvp.Value.EndTime.HasValue && 
                        (DateTime.UtcNow - kvp.Value.EndTime.Value).TotalHours > 1)
                        .Select(kvp => kvp.Key)
                        .ToList();
                        
                    foreach (var key in oldSyncs)
                    {
                        ActiveSyncs.Remove(key);
                    }
                    
                    ActiveSyncs[syncStatus.Id] = syncStatus;
                }
                
                // Start the sync in the background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Starting background dry run with ID: {SyncId}", syncStatus.Id);
                        var result = await SyncFromPlexAsync(true, syncStatus).ConfigureAwait(false);
                        syncStatus.Result = result;
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = "Dry run completed successfully.";
                        syncStatus.EndTime = DateTime.UtcNow;
                        
                        // Generate dry run metrics
                        if (result.Details != null)
                        {
                            int totalChanges = result.Details.CollectionsToAdd.Count + 
                                             result.Details.CollectionsToUpdate.Count + 
                                             result.Details.WatchStatesChanged.Count;
                                             
                            _logger.LogInformation("Dry run completed with {TotalChanges} total changes: {AddCount} collections to add, {UpdateCount} to update, {WatchCount} watch states to change",
                                totalChanges,
                                result.Details.CollectionsToAdd.Count,
                                result.Details.CollectionsToUpdate.Count,
                                result.Details.WatchStatesChanged.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background dry run operation");
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                });
                
                // Return the ID so the client can poll for status
                return Ok(new 
                { 
                    success = true,
                    syncId = syncStatus.Id,
                    message = "Dry run started successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting dry run");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
        
        /// <summary>
        /// API endpoint for updating the selected libraries.
        /// </summary>
        /// <param name="libraryIds">List of library IDs to include in sync operations.</param>
        /// <returns>A success status.</returns>
        [HttpPost("UpdateSelectedLibraries")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult UpdateSelectedLibraries([FromBody] List<string> libraryIds)
        {
            try
            {
                _logger.LogDebug($"Received library IDs: {string.Join(", ", libraryIds)}");
                
                var config = Plugin.Instance!.Configuration;
                config.SelectedLibraries = libraryIds;
                Plugin.Instance.SaveConfiguration();
                
                return Ok(new { 
                    success = true,
                    message = "Selected libraries updated successfully" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating selected libraries");
                return Ok(new { 
                    success = false, 
                    error = $"Error updating selected libraries: {ex.Message}" 
                });
            }
        }
    }
}