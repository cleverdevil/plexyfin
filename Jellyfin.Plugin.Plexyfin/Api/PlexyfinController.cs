using MediaBrowser.Controller.Persistence;
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
    /// Provides fast lookup of Jellyfin items by external IDs.
    /// </summary>
    internal class JellyfinItemIndex
    {
        private readonly Dictionary<string, BaseItem> _imdbIndex = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BaseItem> _tmdbIndex = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BaseItem> _tvdbIndex = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BaseItem> _titleIndex = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;

        public JellyfinItemIndex(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Builds the index from a list of Jellyfin items.
        /// </summary>
        public void BuildIndex(IReadOnlyList<BaseItem> items)
        {
            _logger.LogInformation("Building Jellyfin item index for {0} items", items.Count);
            
            foreach (var item in items)
            {
                // Index by title (for fallback matching)
                if (!string.IsNullOrEmpty(item.Name))
                {
                    _titleIndex[item.Name] = item;
                }
                
                // Index by external IDs
                if (item.ProviderIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId))
                {
                    _imdbIndex[imdbId] = item;
                }
                
                if (item.ProviderIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId))
                {
                    _tmdbIndex[tmdbId] = item;
                }
                
                if (item.ProviderIds.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId))
                {
                    _tvdbIndex[tvdbId] = item;
                }
            }
            
            _logger.LogInformation("Index built: {0} IMDb, {1} TMDb, {2} TVDb, {3} titles indexed", 
                _imdbIndex.Count, _tmdbIndex.Count, _tvdbIndex.Count, _titleIndex.Count);
        }

        /// <summary>
        /// Finds a Jellyfin item matching the given Plex item.
        /// </summary>
        public BaseItem? FindMatch(PlexItem plexItem)
        {
            // Try IMDb ID first (most reliable for movies)
            if (!string.IsNullOrEmpty(plexItem.ImdbId) && _imdbIndex.TryGetValue(plexItem.ImdbId, out var imdbMatch))
            {
                _logger.LogDebug("Matched '{0}' by IMDb ID: {1}", plexItem.Title, plexItem.ImdbId);
                return imdbMatch;
            }
            
            // Try TMDb ID (good for movies and TV shows)
            if (!string.IsNullOrEmpty(plexItem.TmdbId) && _tmdbIndex.TryGetValue(plexItem.TmdbId, out var tmdbMatch))
            {
                _logger.LogDebug("Matched '{0}' by TMDb ID: {1}", plexItem.Title, plexItem.TmdbId);
                return tmdbMatch;
            }
            
            // Try TVDb ID (primarily for TV shows)
            if (!string.IsNullOrEmpty(plexItem.TvdbId) && _tvdbIndex.TryGetValue(plexItem.TvdbId, out var tvdbMatch))
            {
                _logger.LogDebug("Matched '{0}' by TVDb ID: {1}", plexItem.Title, plexItem.TvdbId);
                return tvdbMatch;
            }
            
            // Fall back to title matching
            if (!string.IsNullOrEmpty(plexItem.Title) && _titleIndex.TryGetValue(plexItem.Title, out var titleMatch))
            {
                _logger.LogDebug("Matched '{0}' by title", plexItem.Title);
                return titleMatch;
            }
            
            _logger.LogDebug("No match found for '{0}'", plexItem.Title);
            return null;
        }
    }

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
        private readonly IItemRepository _itemRepository;

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
            IItemRepository itemRepository = null,
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
            _itemRepository = itemRepository;
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
                _logger.LogDryRun("changes will not be applied");

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
                : new Uri(config.PlexServerUrl ?? "http://localhost");
            var plexClient = new PlexClient(_httpClientFactory, _logger, plexServerUri, config.PlexApiToken, config);
            
            // Build the Jellyfin item index for efficient matching
            _logger.LogInformation("Building Jellyfin item index for efficient matching...");
            var jellyfinIndex = new JellyfinItemIndex(_logger);
            
            // Get all movies and TV series from Jellyfin
            var allJellyfinItems = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
            });
            
            jellyfinIndex.BuildIndex(allJellyfinItems);
            _logger.LogInformation("Jellyfin item index ready");

            // Sync library item artwork if enabled (this happens first to ensure all items have artwork before collections)
            if (config.SyncItemArtwork)
            {
                _logger.LogSyncMode(dryRun ? "Simulating item artwork" : "Starting item artwork");

                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun
                        ? "Analyzing item artwork..."
                        : "Syncing item artwork from Plex...";
                    syncStatus.Progress = 5;
                }

                if (!dryRun)
                {
                    // Process all libraries
                    var libraryArtworkResult = await SyncItemArtworkFromPlexAsync(plexClient, jellyfinIndex, dryRun, syncStatus).ConfigureAwait(false);
                    result.ItemArtworkUpdated = libraryArtworkResult;

                    _logger.LogItemArtworkSyncCompleted(result.ItemArtworkUpdated);

                    if (syncStatus != null)
                    {
                        syncStatus.Message = $"Item artwork sync complete. Updated {result.ItemArtworkUpdated} items.";
                        syncStatus.Progress = 30;
                    }
                }
                else
                {
                    // For dry run, estimate the number of items that would be updated
                    var libraryArtworkEstimate = await EstimateItemArtworkUpdatesAsync(plexClient, syncStatus).ConfigureAwait(false);
                    result.ItemArtworkUpdated = libraryArtworkEstimate;

                    _logger.LogItemArtworkSimulationCompleted(result.ItemArtworkUpdated);

                    if (syncStatus != null)
                    {
                        syncStatus.Message = $"Item artwork analysis complete. Would update {result.ItemArtworkUpdated} items.";
                        syncStatus.Progress = 30;
                    }
                }
            }

            // Sync collections if enabled
            if (config.SyncCollections)
            {
                _logger.LogCollectionSync(dryRun ? "Simulating" : "Starting");

                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun
                        ? "Analyzing collections..."
                        : "Syncing collections from Plex...";
                    syncStatus.Progress = 40;
                }

                // We no longer have the delete-all-collections-before-sync option.
                // Collections are now deleted and recreated individually during the sync process.

                if (syncStatus != null)
                {
                    syncStatus.Message = dryRun
                        ? "Analyzing collections from Plex..."
                        : "Processing collections from Plex...";
                    syncStatus.Progress = 50;
                }

                var collectionResults = await SyncCollectionsFromPlexAsync(plexClient, jellyfinIndex, dryRun, syncStatus).ConfigureAwait(false);
                result.CollectionsAdded = collectionResults.added;
                result.CollectionsUpdated = collectionResults.updated;

                // If this is a dry run, make sure the collections are included in the details
                if (dryRun && result.Details != null)
                {
                    // The collectionsToAdd and collectionsToUpdate lists are populated in SyncCollectionsFromPlexAsync
                    result.Details.CollectionsToAdd.AddRange(collectionResults.collectionsToAdd);
                    result.Details.CollectionsToUpdate.AddRange(collectionResults.collectionsToUpdate);

                    // For dry run, estimate the item artwork count based on the total items found
                    if (config.SyncItemArtwork)
                    {
                        // Set an estimated number for items with artwork updates
                        // This is approximate since we don't actually download artwork in dry run mode
                        int itemArtworkEstimate = 0;

                        // Estimate based on matched items (which would have artwork processed)
                        foreach (var collection in collectionResults.collectionsToAdd)
                        {
                            itemArtworkEstimate += collection.Items.Count;
                        }

                        foreach (var collection in collectionResults.collectionsToUpdate)
                        {
                            itemArtworkEstimate += collection.Items.Count;
                        }

                        result.ItemArtworkUpdated = itemArtworkEstimate;
                    }
                }

                if (config.SyncItemArtwork) {
                    _logger.LogCollectionArtworkStatus(
                        dryRun ? "simulation completed" : "completed",
                        result.CollectionsAdded,
                        result.CollectionsUpdated,
                        result.ItemArtworkUpdated);
                } else {
                    _logger.LogCollectionStatus(
                        dryRun ? "simulation completed" : "completed",
                        result.CollectionsAdded,
                        result.CollectionsUpdated);
                }

                if (syncStatus != null)
                {
                    string itemsMessage = config.SyncItemArtwork ?
                        (dryRun ? $", would update artwork for {result.ItemArtworkUpdated} items" : $", updated artwork for {result.ItemArtworkUpdated} items")
                        : "";

                    syncStatus.Message = dryRun
                        ? $"Collection analysis complete. Would add {result.CollectionsAdded}, update {result.CollectionsUpdated} collections{itemsMessage}."
                        : $"Collection sync complete. Added {result.CollectionsAdded}, updated {result.CollectionsUpdated} collections{itemsMessage}.";
                    syncStatus.Progress = 70;
                }
            }


            // Watch state synchronization removed

            // Complete the sync operation
            if (syncStatus != null)
            {
                string completionMessage = "Sync operation completed successfully.";

                // Add summary of what was synced
                if (config.SyncCollections || config.SyncItemArtwork)
                {
                    List<string> syncedItems = new List<string>();

                    if (config.SyncCollections)
                    {
                        var collectionMessage = $"{(dryRun ? "would add" : "added")} {result.CollectionsAdded} collections, " +
                                             $"{(dryRun ? "would update" : "updated")} {result.CollectionsUpdated} collections";

                        // Add deletion information if there were deletions
                        if (result.CollectionsDeleted > 0)
                        {
                            collectionMessage += $", {(dryRun ? "would delete" : "deleted")} {result.CollectionsDeleted} collections";
                        }

                        syncedItems.Add(collectionMessage);
                    }

                    if (config.SyncItemArtwork)
                    {
                        syncedItems.Add($"{(dryRun ? "would update" : "updated")} artwork for {result.ItemArtworkUpdated} items");
                    }

                    completionMessage = $"Sync operation completed successfully. {string.Join(" and ", syncedItems)}.";
                }

                syncStatus.IsComplete = true;
                syncStatus.Result = result;
                syncStatus.Message = completionMessage;
                syncStatus.Progress = 100;
                syncStatus.EndTime = DateTime.UtcNow;
            }

            return result;
        }

        /// <summary>
        /// Deletes a collection from Jellyfin based on a Plex collection.
        /// </summary>
        /// <param name="collectionTitle">The title of the Plex collection to delete from Jellyfin.</param>
        /// <param name="dryRun">If true, changes will not be applied, only reported.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        /// <returns>A boolean indicating whether the deletion was successful.</returns>
        // private async Task<bool> DeleteCollectionFromPlexAsync(
        //     string collectionTitle,
        //     bool dryRun = false,
        //     SyncStatus? syncStatus = null)
        // {
        //     // This is a stub implementation for the collection deletion feature
        //     // TODO: Implement actual collection deletion logic
        //
        //     if (dryRun)
        //     {
        //         _logger.LogInformation("Dry run: Would delete collection '{CollectionTitle}'", collectionTitle);
        //         return true;
        //     }
        //     else
        //     {
        //         _logger.LogInformation("Collection deletion is not yet implemented for '{CollectionTitle}'", collectionTitle);
        //         return false;
        //     }
        // }
        //
        //
        //
        public async Task<bool> DeleteCollectionFromPlexAsync(string collectionName, bool dryRun, SyncStatus? syncStatus = null)
        {
            try
            {
                var collection = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Name = collectionName
                }).FirstOrDefault();

                if (collection == null)
                {
                    _logger.LogWarning($"Collection '{collectionName}' not found.");
                    return false;
                }

                if (dryRun)
                {
                    _logger.LogInformation($"[Dry Run] Would delete collection: {collectionName}");
                    return true;
                }

                _logger.LogInformation($"Deleting collection: {collectionName} (ID: {collection.Id})");

                if (_libraryManager is ILibraryManager concreteLibraryManager)
                {
                    var deleteOptions = new DeleteOptions
                    {
                        DeleteFileLocation = false,
                        DeleteFromExternalProvider = false
                    };

                    concreteLibraryManager.DeleteItem(collection, deleteOptions);
                    return true;
                }

                _logger.LogError("Failed to cast ILibraryManager to concrete LibraryManager. Deletion not possible.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete collection '{collectionName}'.");
                return false;
            }
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
            JellyfinItemIndex jellyfinIndex,
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

                    _logger.LogProcessingLibrary(libraryId);
                    var collections = await plexClient.GetCollections(libraryId).ConfigureAwait(false);
                    _logger.LogFoundCollections(libraryId, collections.Count);

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

                        _logger.LogProcessingCollection(collection.Title);

                        try
                        {
                            // Check if collection already exists in Jellyfin
                            var existingCollections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                            {
                                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                            }).Where(c => c.Name.Equals(collection.Title, StringComparison.OrdinalIgnoreCase));

                            var existingCollection = existingCollections.FirstOrDefault();

                            // Get collection items from Plex
                            var plexItems = await plexClient.GetCollectionItems(collection.Id).ConfigureAwait(false);
                            _logger.LogCollectionItems(plexItems.Count);

                            // Find matching items in Jellyfin
                            var jellyfinItems = new List<Guid>();
                            var matchedItemTitles = new List<string>(); // For dry run report

                            foreach (var plexItem in plexItems)
                            {
                                // Use the efficient index lookup
                                var jellyfinItem = jellyfinIndex.FindMatch(plexItem);

                                if (jellyfinItem != null)
                                {
                                    jellyfinItems.Add(jellyfinItem.Id);
                                    matchedItemTitles.Add(plexItem.Title);
                                    _logger.LogMatchedItem(plexItem.Title, jellyfinItem.Id);

                                    // Note: We no longer process item artwork here as we now have a dedicated method
                                    // that processes all items, including ones not in collections
                                }
                                else
                                {
                                    _logger.LogNoMatchItem(plexItem.Title);
                                }
                            }

                            // If the collection exists, in dry run mode, record it as an update
                            // In actual sync mode, delete it and recreate it
                            if (existingCollection != null)
                            {
                                if (dryRun)
                                {
                                    // For dry run, record as an update

                                    var syncDetail = new SyncCollectionDetail
                                    {
                                        Title = collection.Title,
                                        SortTitle = collection.SortTitle,
                                        Summary = collection.Summary,
                                        Items = matchedItemTitles
                                    };

                                    collectionsToUpdate.Add(syncDetail);
                                    updated++;

                                    // Skip the creation path in dry run mode when the collection exists
                                    continue;
                                }
                                else
                                {
                                    // In actual sync mode, delete the existing collection
                                    _logger.LogInformation($"Deleting existing collection '{collection.Title}' for recreation with updated information");

                                    await DeleteCollectionFromPlexAsync(collection.Title, dryRun, syncStatus).ConfigureAwait(false);

                                    // Set existingCollection to null to force creation path
                                    existingCollection = null;
                                }
                            }

                            // New collection creation path - at this point, existingCollection is always null
                            {
                                // Create new collection
                                _logger.LogNewCollection(dryRun ? "Would create" : "Creating", collection.Title);

                                // In dry run mode, just record what would be done
                                if (dryRun)
                                {
                                    var syncDetail = new SyncCollectionDetail
                                    {
                                        Title = collection.Title,
                                        SortTitle = collection.SortTitle,
                                        Summary = collection.Summary,
                                        Items = matchedItemTitles
                                    };

                                    collectionsToAdd.Add(syncDetail);
                                }
                                else
                                {
                                    try
                                    {
                                        // Before creating a new collection, make sure no collection with this name already exists
                                        var existingCollectionCheck = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                                        {
                                            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                                            Name = collection.Title
                                        }).FirstOrDefault();

                                        if (existingCollectionCheck != null)
                                        {
                                            // If one still exists with this name, we need to make it invisible to avoid conflicts
                                            _logger.LogExistingCollectionConflict(collection.Title);
                                            existingCollectionCheck.Name = $".conflict_{existingCollectionCheck.Id}_{DateTime.Now.Ticks}";
                                            await _libraryManager.UpdateItemAsync(
                                                existingCollectionCheck,
                                                existingCollectionCheck.GetParent(),
                                                ItemUpdateType.MetadataEdit,
                                                CancellationToken.None).ConfigureAwait(false);
                                        }

                                        // Create collection using the built-in collection manager
                                        var options = new MediaBrowser.Controller.Collections.CollectionCreationOptions
                                        {
                                            Name = collection.Title,
                                            ItemIdList = jellyfinItems.Select(id => id.ToString()).ToList(),
                                            IsLocked = true
                                        };

                                        // Create the collection
                                        var newCollectionId = await _collectionManager.CreateCollectionAsync(options).ConfigureAwait(false);
                                        _logger.LogCreatedCollection(newCollectionId.ToString());

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
                                            // Update the collection metadata
                                            newCollection.Overview = collection.Summary;

                                            // Handle sort title specifically
                                            if (!string.IsNullOrEmpty(collection.SortTitle))
                                            {
                                                _logger.LogInformation("Setting sort title '{SortTitle}' for collection '{Title}'", collection.SortTitle, collection.Title);
                                                newCollection.ForcedSortName = collection.SortTitle;
                                            }

                                            // Save the metadata changes
                                            await _libraryManager.UpdateItemAsync(
                                                newCollection,
                                                newCollection.GetParent(),
                                                ItemUpdateType.MetadataEdit,
                                                CancellationToken.None).ConfigureAwait(false);

                                            // Process artwork if enabled
                                            if (config.SyncArtwork)
                                            {
                                                await ProcessCollectionArtwork(newCollection, collection).ConfigureAwait(false);
                                            }

                                            added++;
                                            _logger.LogSuccessfullyCreatedCollection(collection.Title);
                                        }
                                        else
                                        {
                                            _logger.LogUnableToRetrieveCollection(collection.Title);
                                        }
                                    }
                                    catch (InvalidOperationException ex)
                                    {
                                        _logger.LogErrorCreatingCollection(collection.Title, ex);
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        _logger.LogErrorCreatingCollection(collection.Title, ex);
                                    }
                                    catch (IOException ex)
                                    {
                                        _logger.LogErrorCreatingCollection(collection.Title, ex);
                                    }
                                }
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogErrorCollection(collection.Title, ex);
                        }
                        catch (ArgumentException ex)
                        {
                            _logger.LogErrorCollection(collection.Title, ex);
                        }
                        catch (HttpRequestException ex)
                        {
                            _logger.LogErrorCollection(collection.Title, ex);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogErrorCollection(collection.Title, ex);
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
            catch (HttpRequestException ex)
            {
                _logger.LogErrorSyncingCollections(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error connecting to Plex: {ex.Message}";
                }
            }
            catch (IOException ex)
            {
                _logger.LogErrorSyncingCollections(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"I/O error during sync: {ex.Message}";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorSyncingCollections(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error during sync operation: {ex.Message}";
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorSyncingCollections(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error with invalid arguments: {ex.Message}";
                }
            }

            return (added, updated, collectionsToAdd, collectionsToUpdate);
        }

        /// <summary>
        /// Processes artwork for a collection without requiring type conversions.
        /// </summary>
        /// <param name="jellyfinCollection">The Jellyfin collection.</param>
        /// <param name="plexCollection">The Plex collection.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessCollectionArtwork(BaseItem jellyfinCollection, PlexCollection plexCollection)
        {
            if (jellyfinCollection == null)
            {
                _logger.LogCannotProcessArtwork("collection");
                return;
            }

            _logger.LogProcessingArtwork("collection", jellyfinCollection.Name);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Plexyfin-Plugin");
            bool hasChanges = false;

            try
            {
                // Process primary image (poster/thumbnail)
                if (plexCollection.ThumbUrl != null)
                {
                    _logger.LogDownloadingThumbnail("collection", plexCollection.ThumbUrl.ToString());

                    var response = await httpClient.GetAsync(plexCollection.ThumbUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        // Using null-conditional operator and null-coalescing operator to safely handle nullable
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                        _logger.LogMimeTypeFromResponse(mimeType);
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexCollection.ThumbUrl.ToString());
                        _logger.LogMimeTypeFromUrl(mimeType);
                    }

                    // Use a memory stream to fully buffer the content before passing it to Jellyfin's image saver
                    // This helps avoid file locking issues by ensuring we're not trying to read and write at the same time
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the BaseItem
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellyfinCollection,
                            jellyfinCollection.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        // Create a new memory stream for each save operation to avoid sharing stream positions
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellyfinCollection,
                                saveStream,
                                mimeType, // Use the detected MIME type
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogIoErrorSavingImage("collection", ex);
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellyfinCollection,
                                retryStream,
                                mimeType,
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        hasChanges = true;
                        _logger.LogSavedThumbnail("collection", jellyfinCollection.Name);
                    }
                    else
                    {
                        _logger.LogProviderManagerNull("collection", jellyfinCollection.Name);
                    }
                }

                // Process background image (art)
                if (plexCollection.ArtUrl != null)
                {
                    _logger.LogDownloadingArt(plexCollection.ArtUrl.ToString());

                    var response = await httpClient.GetAsync(plexCollection.ArtUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        // Using null-conditional operator and null-coalescing operator to safely handle nullable
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                        _logger.LogMimeTypeFromResponse(mimeType);
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexCollection.ArtUrl.ToString());
                        _logger.LogMimeTypeFromUrl(mimeType);
                    }

                    // Use a memory stream to fully buffer the content before passing it to Jellyfin's image saver
                    // This helps avoid file locking issues by ensuring we're not trying to read and write at the same time
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the BaseItem
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellyfinCollection,
                            jellyfinCollection.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        // Create a new memory stream for each save operation to avoid sharing stream positions
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellyfinCollection,
                                saveStream,
                                mimeType, // Use the detected MIME type
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogIoErrorSavingBackdrop("collection", ex);
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellyfinCollection,
                                retryStream,
                                mimeType,
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        hasChanges = true;
                        _logger.LogSavedArtwork("collection");
                    }
                    else
                    {
                        _logger.LogCannotSaveBackdrop("collection");
                    }
                }

                // Refresh artwork after changes
                if (hasChanges)
                {
                    await _libraryManager.UpdateItemAsync(
                        jellyfinCollection,
                        jellyfinCollection.GetParent(),
                        ItemUpdateType.ImageUpdate,
                        CancellationToken.None).ConfigureAwait(false);

                    _logger.LogUpdatedRepoWithImages("collection");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorProcessingArtwork("collection", jellyfinCollection.Name, ex);
            }
            catch (IOException ex)
            {
                _logger.LogErrorProcessingArtwork("collection", jellyfinCollection.Name, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorProcessingArtwork("collection", jellyfinCollection.Name, ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorProcessingArtwork("collection", jellyfinCollection.Name, ex);
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorProcessingArtwork("collection", jellyfinCollection.Name, ex);
            }
        }

        /// <summary>
        /// Clears all images of a specific type for a media item
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item to clear images from</param>
        /// <param name="imageType">The type of images to clear (Primary or Backdrop)</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task ClearItemImages(BaseItem jellyfinItem, ImageType imageType)
        {
            try
            {
                var images = jellyfinItem.GetImages(imageType).ToList();

                if (images.Count == 0)
                {
                    _logger.LogInformation("No {0} images to clear for {1}", imageType, jellyfinItem.Name);
                    return;
                }

                _logger.LogInformation("Clearing {0} {1} images for {2}", images.Count, imageType, jellyfinItem.Name);

                // Create a copy of the image list to avoid potential issues with modifying while iterating
                var imagesCopy = images.ToList();

                // First remove images from the item metadata
                foreach (var image in imagesCopy)
                {
                    try
                    {
                        _logger.LogInformation("Removing {0} image from item {1}", imageType, jellyfinItem.Name);
                        jellyfinItem.RemoveImage(image);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error removing {0} image from {1}", imageType, jellyfinItem.Name);
                    }
                }

                try
                {
                    // Update the repository immediately to register the image removal
                    _logger.LogInformation("Updating repository after {0} image removal for {1}", imageType, jellyfinItem.Name);
                    await jellyfinItem.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);

                    // Add a delay to allow the repository update to take effect
                    await Task.Delay(500).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating repository after removing {0} images from {1}", imageType, jellyfinItem.Name);
                }

                // Then attempt to delete the physical files
                foreach (var image in imagesCopy)
                {
                    try
                    {
                        if (image.IsLocalFile && !string.IsNullOrEmpty(image.Path))
                        {
                            if (System.IO.File.Exists(image.Path))
                            {
                                _logger.LogInformation("Deleting existing {0} image file: {1}", imageType, image.Path);
                                BaseItem.FileSystem.DeleteFile(image.Path);

                                // Add a small delay between deleting each file to prevent race conditions
                                await Task.Delay(50).ConfigureAwait(false);
                            }
                            else
                            {
                                _logger.LogInformation("{0} image file already deleted or not found: {1}", imageType, image.Path);
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        // File might be in use by another process, just log and continue
                        _logger.LogWarning(ex, "File in use, unable to delete {0} image file: {1}", imageType, image.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting {0} image file: {1}", imageType, image.Path);
                    }
                }

                // Add a longer delay to allow file system operations to complete
                await Task.Delay(500).ConfigureAwait(false);

                // Final repository update to ensure all changes are saved
                try
                {
                    await _libraryManager.UpdateItemAsync(
                        jellyfinItem,
                        jellyfinItem.GetParent(),
                        ItemUpdateType.ImageUpdate,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in final update after clearing {0} images for {1}", imageType, jellyfinItem.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing {0} images for {1}", imageType, jellyfinItem.Name);
            }
        }

        /// <summary>
        /// Processes artwork for a TV show season.
        /// </summary>
        /// <param name="series">The Jellyfin TV series.</param>
        /// <param name="plexSeason">The Plex season.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task<bool> ProcessSeasonArtwork(BaseItem series, PlexSeason plexSeason)
        {
            if (series == null)
            {
                _logger.LogError("Cannot process season artwork: Series is null");
                return false;
            }

            // Find the matching season in Jellyfin
            var jellySeason = FindJellyfinSeason(series, plexSeason.Index);
            if (jellySeason == null)
            {
                _logger.LogError("Cannot find Jellyfin season {0} for series {1}", plexSeason.Index, series.Name);
                return false;
            }

            _logger.LogInformation("Processing artwork for season {0} of {1}", plexSeason.Index, series.Name);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Plexyfin-Plugin");
            bool hasChanges = false;

            try
            {
                // Process primary image (poster/thumbnail) for the season
                if (plexSeason.ThumbUrl != null)
                {
                    _logger.LogInformation("Downloading season thumbnail: {0}", plexSeason.ThumbUrl.ToString());

                    // Clear existing Primary images before saving the new one
                    await ClearItemImages(jellySeason, ImageType.Primary).ConfigureAwait(false);

                    var response = await httpClient.GetAsync(plexSeason.ThumbUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexSeason.ThumbUrl.ToString());
                    }

                    // Use a memory stream to fully buffer the content
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the Season item
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellySeason,
                            jellySeason.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellySeason,
                                saveStream,
                                mimeType,
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogError(ex, "I/O error saving season thumbnail image");
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellySeason,
                                retryStream,
                                mimeType,
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        hasChanges = true;
                        _logger.LogInformation("Saved season thumbnail for {0} season {1}", series.Name, plexSeason.Index);
                    }
                    else
                    {
                        _logger.LogError("Cannot save season thumbnail: Provider manager is null");
                    }
                }

                // Process backdrop image for the season (if available)
                if (plexSeason.ArtUrl != null)
                {
                    _logger.LogInformation("Downloading season backdrop: {0}", plexSeason.ArtUrl.ToString());

                    // Clear existing Backdrop images before saving the new one
                    await ClearItemImages(jellySeason, ImageType.Backdrop).ConfigureAwait(false);

                    var response = await httpClient.GetAsync(plexSeason.ArtUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexSeason.ArtUrl.ToString());
                    }

                    // Use a memory stream to fully buffer the content
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the Season item
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellySeason,
                            jellySeason.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellySeason,
                                saveStream,
                                mimeType,
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogError(ex, "I/O error saving season backdrop image");
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellySeason,
                                retryStream,
                                mimeType,
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        hasChanges = true;
                        _logger.LogInformation("Saved season backdrop for {0} season {1}", series.Name, plexSeason.Index);
                    }
                    else
                    {
                        _logger.LogError("Cannot save season backdrop: Provider manager is null");
                    }
                }

                // Refresh artwork after changes
                if (hasChanges)
                {
                    await _libraryManager.UpdateItemAsync(
                        jellySeason,
                        jellySeason.GetParent(),
                        ItemUpdateType.ImageUpdate,
                        CancellationToken.None).ConfigureAwait(false);

                    _logger.LogInformation("Updated repository with season images");
                }

                return hasChanges;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error processing season artwork for {0} season {1}", series.Name, plexSeason.Index);
                return false;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "I/O error processing season artwork for {0} season {1}", series.Name, plexSeason.Index);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing season artwork for {0} season {1}", series.Name, plexSeason.Index);
                return false;
            }
        }

        /// <summary>
        /// Finds a season in a Jellyfin TV series by its index.
        /// </summary>
        /// <param name="series">The TV series.</param>
        /// <param name="seasonIndex">The season index (0 for Specials, 1 for Season 1, etc.).</param>
        /// <returns>The season item if found, null otherwise.</returns>
        private BaseItem? FindJellyfinSeason(BaseItem series, int seasonIndex)
        {
            try
            {
                // Check if the item is a Series
                if (!(series is MediaBrowser.Controller.Entities.TV.Series tvSeries))
                {
                    _logger.LogError("Cannot find season: Item is not a TV series");
                    return null;
                }

                // Get all seasons from the series
                var seasons = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                {
                    Parent = tvSeries,
                    IncludeItemTypes = new[] { BaseItemKind.Season }
                });

                // Look for a season with the matching index number
                foreach (var season in seasons)
                {
                    if (season is MediaBrowser.Controller.Entities.TV.Season jellySeason)
                    {
                        int jellySeasonNumber = jellySeason.IndexNumber.HasValue ? (int)jellySeason.IndexNumber.Value : -1;

                        // Match the season numbers
                        if (jellySeasonNumber == seasonIndex)
                        {
                            return season;
                        }
                    }
                }

                _logger.LogError("Season {0} not found in TV series {1}", seasonIndex, series.Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding season {0} in TV series {1}", seasonIndex, series.Name);
                return null;
            }
        }

        /// <summary>
        /// Processes artwork for an individual movie or TV show item.
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item.</param>
        /// <param name="plexItem">The Plex item.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessItemArtwork(BaseItem jellyfinItem, PlexItem plexItem)
        {
            if (jellyfinItem == null)
            {
                _logger.LogCannotProcessArtwork("item");
                return;
            }

            _logger.LogProcessingArtwork("item", jellyfinItem.Name);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Plexyfin-Plugin");
            bool hasChanges = false;

            try
            {
                // Check if this is a TV series, as seasons need special handling
                bool isTvSeries = jellyfinItem is MediaBrowser.Controller.Entities.TV.Series;
                if (isTvSeries && plexItem.Type.Equals("show", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Item {0} is a TV series, processing seasons", jellyfinItem.Name);

                    // Process the main series artwork first
                    int artworkCount = await ProcessMainItemArtwork(jellyfinItem, plexItem).ConfigureAwait(false);
                    hasChanges = artworkCount > 0;

                    // Get and process seasons for TV series
                    var plexSeasons = await GetAndProcessTvSeriesSeasons(jellyfinItem, plexItem.Id).ConfigureAwait(false);
                    _logger.LogInformation("Processed {0} seasons for TV series {1}", plexSeasons, jellyfinItem.Name);
                }
                else
                {
                    // Process as a normal item (movie, music album, etc.)
                    int artworkCount = await ProcessMainItemArtwork(jellyfinItem, plexItem).ConfigureAwait(false);
                    hasChanges = artworkCount > 0;
                }

                // Refresh artwork after changes
                if (hasChanges)
                {
                    await _libraryManager.UpdateItemAsync(
                        jellyfinItem,
                        jellyfinItem.GetParent(),
                        ItemUpdateType.ImageUpdate,
                        CancellationToken.None).ConfigureAwait(false);

                    _logger.LogUpdatedRepoWithImages("item");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorProcessingArtwork("item", jellyfinItem.Name, ex);
            }
            catch (IOException ex)
            {
                _logger.LogErrorProcessingArtwork("item", jellyfinItem.Name, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorProcessingArtwork("item", jellyfinItem.Name, ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorProcessingArtwork("item", jellyfinItem.Name, ex);
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorProcessingArtwork("item", jellyfinItem.Name, ex);
            }
        }

        /// <summary>
        /// Processes the main artwork (Primary and Backdrop) for a Jellyfin item.
        /// </summary>
        /// <param name="jellyfinItem">The Jellyfin item.</param>
        /// <param name="plexItem">The Plex item.</param>
        /// <returns>The number of artwork images processed.</returns>
        private async Task<int> ProcessMainItemArtwork(BaseItem jellyfinItem, PlexItem plexItem)
        {
            int processedCount = 0;
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Plexyfin-Plugin");

            try
            {
                // Process primary image (poster/thumbnail)
                if (plexItem.ThumbUrl != null)
                {
                    _logger.LogDownloadingThumbnail("item", plexItem.ThumbUrl.ToString());

                    // Clear existing Primary images before saving the new one
                    await ClearItemImages(jellyfinItem, ImageType.Primary).ConfigureAwait(false);

                    var response = await httpClient.GetAsync(plexItem.ThumbUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        // Using null-conditional operator and null-coalescing operator to safely handle nullable
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                        // MIME type logging handled internally
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexItem.ThumbUrl.ToString());
                        // MIME type logging handled internally
                    }

                    // Use a memory stream to fully buffer the content before passing it to Jellyfin's image saver
                    // This helps avoid file locking issues by ensuring we're not trying to read and write at the same time
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the BaseItem
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellyfinItem,
                            jellyfinItem.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        // Create a new memory stream for each save operation to avoid sharing stream positions
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellyfinItem,
                                saveStream,
                                mimeType, // Use the detected MIME type
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogIoErrorSavingImage("item", ex);
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellyfinItem,
                                retryStream,
                                mimeType,
                                ImageType.Primary,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        processedCount++;
                        _logger.LogSavedThumbnail("item", jellyfinItem.Name);
                    }
                    else
                    {
                        _logger.LogProviderManagerNull("item", jellyfinItem.Name);
                    }
                }

                // Process background image (art)
                if (plexItem.ArtUrl != null)
                {
                    _logger.LogDownloadingItemArt("item", plexItem.ArtUrl.ToString());

                    // Clear existing Backdrop images before saving the new one
                    await ClearItemImages(jellyfinItem, ImageType.Backdrop).ConfigureAwait(false);

                    var response = await httpClient.GetAsync(plexItem.ArtUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        // Using null-conditional operator and null-coalescing operator to safely handle nullable
                        mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                        _logger.LogMimeTypeFromResponse(mimeType);
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexItem.ArtUrl.ToString());
                        _logger.LogMimeTypeFromUrl(mimeType);
                    }

                    // Use a memory stream to fully buffer the content before passing it to Jellyfin's image saver
                    // This helps avoid file locking issues by ensuring we're not trying to read and write at the same time
                    byte[] imageData;
                    using (var imageStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        using var memStream = new MemoryStream();
                        await imageStream.CopyToAsync(memStream).ConfigureAwait(false);
                        imageData = memStream.ToArray();
                    }

                    // Add a small delay to allow any file handles to be completely closed
                    await Task.Delay(100).ConfigureAwait(false);

                    // Set the image directly on the BaseItem
                    if (_providerManager != null)
                    {
                        await _libraryManager.UpdateItemAsync(
                            jellyfinItem,
                            jellyfinItem.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);

                        // Use memory stream and ImageType enum
                        // Create a new memory stream for each save operation to avoid sharing stream positions
                        using var saveStream = new MemoryStream(imageData);
                        try
                        {
                            await _providerManager.SaveImage(
                                jellyfinItem,
                                saveStream,
                                mimeType, // Use the detected MIME type
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogIoErrorSavingBackdrop("item", ex);
                            // Add a longer delay and try once more
                            await Task.Delay(500).ConfigureAwait(false);

                            using var retryStream = new MemoryStream(imageData);
                            await _providerManager.SaveImage(
                                jellyfinItem,
                                retryStream,
                                mimeType,
                                ImageType.Backdrop,
                                null,
                                CancellationToken.None).ConfigureAwait(false);
                        }

                        processedCount++;
                        _logger.LogSavedArtwork("item");
                    }
                    else
                    {
                        _logger.LogCannotSaveBackdrop("item");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing main artwork for {0}", jellyfinItem.Name);
            }

            return processedCount;
        }

        /// <summary>
        /// Gets and processes TV series seasons from Plex.
        /// </summary>
        /// <param name="series">The Jellyfin TV series.</param>
        /// <param name="plexSeriesId">The Plex series ID.</param>
        /// <returns>The number of seasons processed.</returns>
        private async Task<int> GetAndProcessTvSeriesSeasons(BaseItem series, string plexSeriesId)
        {
            int processedSeasons = 0;

            try
            {
                // Check if the item is a Series
                if (!(series is MediaBrowser.Controller.Entities.TV.Series tvSeries))
                {
                    _logger.LogError("Cannot process seasons: Item is not a TV series");
                    return 0;
                }

                // Create a Plex client instance
                var config = Plugin.Instance!.Configuration;
                var plexServerUri = string.IsNullOrEmpty(config.PlexServerUrl)
                    ? new Uri("http://localhost")
                    : new Uri(config.PlexServerUrl ?? "http://localhost");
                var plexClient = new PlexClient(_httpClientFactory, _logger, plexServerUri, config.PlexApiToken, config);

                // Get seasons from Plex
                var plexSeasons = await plexClient.GetTvSeriesSeasons(plexSeriesId).ConfigureAwait(false);

                _logger.LogInformation("Found {0} seasons for TV series {1}", plexSeasons.Count, series.Name);

                // Process each season
                foreach (var plexSeason in plexSeasons)
                {
                    bool seasonProcessed = await ProcessSeasonArtwork(series, plexSeason).ConfigureAwait(false);
                    if (seasonProcessed)
                    {
                        processedSeasons++;
                    }
                }

                _logger.LogInformation("Processed artwork for {0} seasons of {1}", processedSeasons, series.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing seasons for TV series {0}", series.Name);
            }

            return processedSeasons;
        }

        /// <summary>
        /// Helper method to get the library name for an item by traversing up the parent chain
        /// </summary>
        /// <param name="item">The item to find the library for</param>
        /// <returns>The library name or null if not found</returns>
        private string? GetItemLibraryName(BaseItem item)
        {
            try
            {
                // Track the current parent item as we walk up the tree
                BaseItem? current = item;

                // To prevent infinite loops in case of circular references
                int maxDepth = 10;
                int depth = 0;

                // Keep going up the parent chain until we reach the root or hit maxDepth
                while (current != null && depth < maxDepth)
                {
                    // Try to get the parent using the GetParent method
                    var parent = current.GetParent();

                    // If parent is null, current might be a top-level item like a library
                    if (parent == null)
                    {
                        return current.Name;
                    }

                    // Move up to the parent for the next iteration
                    current = parent;
                    depth++;
                }

                // If we get here and current is not null, return its name
                return current?.Name;
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorGettingLibraryName(item.Name, ex);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingLibraryName(item.Name, ex);
                return null;
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorGettingLibraryName(item.Name, ex);
                return null;
            }
        }

        /// <summary>
        /// Syncs artwork for all items in all selected libraries.
        /// </summary>
        /// <param name="plexClient">The Plex client.</param>
        /// <param name="dryRun">If true, changes will not be applied, only reported.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        /// <returns>The number of items with artwork updated.</returns>
        private async Task<int> SyncItemArtworkFromPlexAsync(
            PlexClient plexClient,
            JellyfinItemIndex jellyfinIndex,
            bool dryRun = false,
            SyncStatus? syncStatus = null)
        {
            int artworkUpdated = 0;

            try
            {
                // Get selected libraries from configuration
                var config = Plugin.Instance!.Configuration;
                var selectedLibraries = config.SelectedLibraries ?? new List<string>();

                if (selectedLibraries.Count == 0)
                {
                    _logger.LogNoLibrariesSelected();
                    return 0;
                }

                // Track progress metrics
                int totalLibraries = selectedLibraries.Count;
                int processedLibraries = 0;

                // Process each selected library
                foreach (var libraryId in selectedLibraries)
                {
                    processedLibraries++;

                    if (syncStatus != null)
                    {
                        int libraryProgress = processedLibraries * 100 / totalLibraries;
                        // Scale progress between 5-30%
                        int scaledProgress = 5 + (libraryProgress * 25 / 100);
                        syncStatus.Progress = scaledProgress;
                        syncStatus.Message = $"Processing library {processedLibraries} of {totalLibraries} for item artwork...";
                    }

                    _logger.LogProcessingLibraryArtwork(libraryId);

                    // Get all items in the library
                    var plexItems = await plexClient.GetLibraryItems(libraryId).ConfigureAwait(false);
                    _logger.LogFoundPlexItems(plexItems.Count, libraryId);

                    // Track item progress within this library
                    int totalItems = plexItems.Count;
                    int processedItems = 0;

                    foreach (var plexItem in plexItems)
                    {
                        processedItems++;

                        if (syncStatus != null && totalItems > 0 && processedItems % 10 == 0)
                        {
                            // Update progress every 10 items to avoid excessive UI updates
                            syncStatus.Message = $"Processing library {processedLibraries} of {totalLibraries}: " +
                                               $"Item {processedItems} of {totalItems} ({plexItem.Title})";
                        }

                        _logger.LogProcessingItemArtwork(plexItem.Title);

                        try
                        {
                            // Use the efficient index lookup
                            var jellyfinItem = jellyfinIndex.FindMatch(plexItem);

                            if (jellyfinItem != null)
                            {
                                _logger.LogMatchedItem(plexItem.Title, jellyfinItem.Id);

                                // Process artwork for this item
                                if (!dryRun)
                                {
                                    await ProcessItemArtwork(jellyfinItem, plexItem).ConfigureAwait(false);
                                    artworkUpdated++;
                                }
                            }
                            else
                            {
                                _logger.LogNoMatchItem(plexItem.Title);
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            _logger.LogErrorProcessingArtwork("item", plexItem.Title, ex);
                        }
                        catch (IOException ex)
                        {
                            _logger.LogErrorProcessingArtwork("item", plexItem.Title, ex);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogErrorProcessingArtwork("item", plexItem.Title, ex);
                        }
                        catch (ArgumentException ex)
                        {
                            _logger.LogErrorProcessingArtwork("item", plexItem.Title, ex);
                        }
                    }

                    _logger.LogLibraryArtworkComplete(libraryId, artworkUpdated);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorSyncingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error connecting to Plex: {ex.Message}";
                }
            }
            catch (IOException ex)
            {
                _logger.LogErrorSyncingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"I/O error during image processing: {ex.Message}";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorSyncingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error during sync operation: {ex.Message}";
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorSyncingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error with invalid arguments: {ex.Message}";
                }
            }

            return artworkUpdated;
        }

        /// <summary>
        /// Estimates the number of items that would have artwork updated in a dry run.
        /// </summary>
        /// <param name="plexClient">The Plex client.</param>
        /// <param name="syncStatus">Optional sync status object for progress tracking.</param>
        /// <returns>The estimated number of items with artwork that would be updated.</returns>
        private async Task<int> EstimateItemArtworkUpdatesAsync(
            PlexClient plexClient,
            SyncStatus? syncStatus = null)
        {
            int estimate = 0;

            try
            {
                // Get selected libraries from configuration
                var config = Plugin.Instance!.Configuration;
                var selectedLibraries = config.SelectedLibraries ?? new List<string>();

                if (selectedLibraries.Count == 0)
                {
                    _logger.LogNoLibrariesSelected();
                    return 0;
                }

                // Track progress metrics
                int totalLibraries = selectedLibraries.Count;
                int processedLibraries = 0;

                // Sample a subset of items from each library to estimate the total
                foreach (var libraryId in selectedLibraries)
                {
                    processedLibraries++;

                    if (syncStatus != null)
                    {
                        int libraryProgress = processedLibraries * 100 / totalLibraries;
                        // Scale progress between 5-30%
                        int scaledProgress = 5 + (libraryProgress * 25 / 100);
                        syncStatus.Progress = scaledProgress;
                        syncStatus.Message = $"Estimating artwork updates for library {processedLibraries} of {totalLibraries}...";
                    }

                    _logger.LogEstimatingArtworkUpdates(libraryId);

                    // For dry run, we'll either sample items or just count them based on library size
                    try
                    {
                        // Count items in library with artwork
                        var plexItems = await plexClient.GetLibraryItems(libraryId).ConfigureAwait(false);

                        // Count items that have artwork
                        int itemsWithArtwork = plexItems.Count(item => item.ThumbUrl != null || item.ArtUrl != null);

                        // Count matching items in Jellyfin
                        int matchedItems = 0;

                        // For large libraries, just sample a subset to estimate
                        var sampleSize = plexItems.Count > 100 ? 50 : plexItems.Count;
                        var sampleItems = plexItems.Take(sampleSize).ToList();

                        foreach (var plexItem in sampleItems)
                        {
                            var matchingItems = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                            {
                                Name = plexItem.Title,
                                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
                            });

                            if (matchingItems.Any())
                            {
                                matchedItems++;
                            }
                        }

                        // Calculate match rate and extrapolate to full library
                        if (sampleSize > 0)
                        {
                            double matchRate = (double)matchedItems / sampleSize;
                            int estimatedMatches = (int)(matchRate * itemsWithArtwork);
                            estimate += estimatedMatches;

                            _logger.LogEstimatedArtworkUpdates(libraryId, estimatedMatches, sampleSize, matchRate);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogErrorEstimatingLibraryArtwork(libraryId, ex);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogErrorEstimatingLibraryArtwork(libraryId, ex);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogErrorEstimatingLibraryArtwork(libraryId, ex);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorEstimatingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error connecting to Plex: {ex.Message}";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorEstimatingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error during estimation operation: {ex.Message}";
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorEstimatingItemArtwork(ex);

                if (syncStatus != null)
                {
                    syncStatus.Message = $"Error with invalid arguments: {ex.Message}";
                }
            }

            return estimate;
        }

        /// <summary>
        /// Gets the MIME type based on a file URL's extension.
        /// </summary>
        /// <param name="url">The URL to analyze.</param>
        /// <returns>The MIME type string.</returns>
        private string GetMimeTypeFromUrl(string url)
        {
            // Default to image/png if we can't determine the type
            string mimeType = "image/png";

            try
            {
                string extension = Path.GetExtension(url.Split('?')[0]).ToLowerInvariant();

                switch (extension)
                {
                    case ".jpg":
                    case ".jpeg":
                        mimeType = "image/jpeg";
                        break;
                    case ".png":
                        mimeType = "image/png";
                        break;
                    case ".gif":
                        mimeType = "image/gif";
                        break;
                    case ".webp":
                        mimeType = "image/webp";
                        break;
                    case ".bmp":
                        mimeType = "image/bmp";
                        break;
                    case ".tiff":
                    case ".tif":
                        mimeType = "image/tiff";
                        break;
                    default:
                        // If we can't determine the type from the extension, default to image/png
                        mimeType = "image/png";
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorDeterminingMimeType(url, ex);
            }
            catch (FormatException ex)
            {
                _logger.LogErrorDeterminingMimeType(url, ex);
            }
            catch (PathTooLongException ex)
            {
                _logger.LogErrorDeterminingMimeType(url, ex);
            }

            return mimeType;
        }

        /// <summary>
        /// Splits a library name into individual words for comparison
        /// </summary>
        /// <param name="input">The library name to split</param>
        /// <returns>A list of individual words</returns>
        private List<string> SplitIntoWords(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new List<string>();
            }

            // Split by common word separators:
            // - Space
            // - Underscore
            // - Dash
            // - Period
            // - Comma
            var result = input
                .Split(new[] { ' ', '_', '-', '.', ',', '(', ')', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim())
                .Where(word => !string.IsNullOrEmpty(word))
                .ToList();

            // Remove common words that don't help with matching:
            // "the", "and", "or", "of", etc.
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "or", "of", "in", "a", "an", "is", "to", "by", "for", "with"
            };

            // Also remove common words used in library names
            stopWords.Add("library");
            stopWords.Add("movies");
            stopWords.Add("shows");
            stopWords.Add("tv");
            stopWords.Add("series");
            stopWords.Add("collection");
            stopWords.Add("collections");

            return result
                .Where(word => !stopWords.Contains(word))
                .ToList();
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
                        _logger.LogStartingBackgroundSync(syncStatus.Id);
                        var result = await SyncFromPlexAsync(false, syncStatus).ConfigureAwait(false);
                        syncStatus.Result = result;
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = "Sync completed successfully.";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogErrorBackgroundSync(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Error connecting to Plex: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogErrorBackgroundSync(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Operation error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogErrorBackgroundSync(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Parameter error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogErrorBackgroundSync(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"I/O error: {ex.Message}";
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
            catch (HttpRequestException ex)
            {
                _logger.LogErrorStartingSync(ex);
                return StatusCode(500, new { Error = $"Connection error: {ex.Message}" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorStartingSync(ex);
                return StatusCode(500, new { Error = $"Operation error: {ex.Message}" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorStartingSync(ex);
                return StatusCode(500, new { Error = $"Parameter error: {ex.Message}" });
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorStartingSync(ex);
                return StatusCode(500, new { Error = $"Null reference error: {ex.Message}" });
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
                                collectionsDeleted = syncStatus.Result.CollectionsDeleted,
                                itemArtworkUpdated = syncStatus.Result.ItemArtworkUpdated,
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
            catch (KeyNotFoundException ex)
            {
                _logger.LogErrorGettingSyncStatus(ex);
                return StatusCode(500, new { Error = $"Sync operation not found: {ex.Message}" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingSyncStatus(ex);
                return StatusCode(500, new { Error = $"Operation error: {ex.Message}" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorGettingSyncStatus(ex);
                return StatusCode(500, new { Error = $"Parameter error: {ex.Message}" });
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorGettingSyncStatus(ex);
                return StatusCode(500, new { Error = $"Null reference error: {ex.Message}" });
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
            // Validate parameters
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentNullException(nameof(token));
            }

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
            catch (HttpRequestException ex)
            {
                _logger.LogErrorTestingPlexConnection(ex);
                return Ok(new {
                    success = false,
                    error = $"Error connecting to Plex: {ex.Message}"
                });
            }
            catch (UriFormatException ex)
            {
                _logger.LogErrorTestingPlexConnection(ex);
                return Ok(new {
                    success = false,
                    error = $"Invalid Plex URL: {ex.Message}"
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorTestingPlexConnection(ex);
                return Ok(new {
                    success = false,
                    error = $"Invalid operation when connecting to Plex: {ex.Message}"
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorTestingPlexConnection(ex);
                return Ok(new {
                    success = false,
                    error = $"Invalid argument when connecting to Plex: {ex.Message}"
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
                        _logger.LogStartingBackgroundDryRun(syncStatus.Id);
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
                                             result.Details.CollectionsToUpdate.Count;

                            _logger.LogDryRunCompleted(totalChanges, result.Details.CollectionsToAdd.Count, result.Details.CollectionsToUpdate.Count);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogErrorBackgroundDryRun(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Connection error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogErrorBackgroundDryRun(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Operation error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogErrorBackgroundDryRun(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"Parameter error: {ex.Message}";
                        syncStatus.EndTime = DateTime.UtcNow;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogErrorBackgroundDryRun(ex);
                        syncStatus.IsComplete = true;
                        syncStatus.Progress = 100;
                        syncStatus.Message = $"I/O error: {ex.Message}";
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
            catch (HttpRequestException ex)
            {
                _logger.LogErrorStartingDryRun(ex);
                return StatusCode(500, new { Error = $"Connection error: {ex.Message}" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorStartingDryRun(ex);
                return StatusCode(500, new { Error = $"Operation error: {ex.Message}" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorStartingDryRun(ex);
                return StatusCode(500, new { Error = $"Parameter error: {ex.Message}" });
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorStartingDryRun(ex);
                return StatusCode(500, new { Error = $"Null reference error: {ex.Message}" });
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
            // Validate parameter
            if (libraryIds == null)
            {
                throw new ArgumentNullException(nameof(libraryIds));
            }

            try
            {
                _logger.LogReceivedLibraryIDs(string.Join(", ", libraryIds));

                var config = Plugin.Instance!.Configuration;
                config.SelectedLibraries = libraryIds;
                Plugin.Instance.SaveConfiguration();

                return Ok(new {
                    success = true,
                    message = "Selected libraries updated successfully"
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorUpdatingLibraries(ex);
                return Ok(new {
                    success = false,
                    error = $"Operation error: {ex.Message}"
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorUpdatingLibraries(ex);
                return Ok(new {
                    success = false,
                    error = $"Invalid argument: {ex.Message}"
                });
            }
            catch (NullReferenceException ex)
            {
                _logger.LogErrorUpdatingLibraries(ex);
                return Ok(new {
                    success = false,
                    error = $"Null reference error: {ex.Message}"
                });
            }
            catch (IOException ex)
            {
                _logger.LogErrorUpdatingLibraries(ex);
                return Ok(new {
                    success = false,
                    error = $"I/O error: {ex.Message}"
                });
            }
        }


        /// <summary>
        /// Finds a Jellyfin season that matches the given Plex season.
        /// </summary>
        /// <param name="plexSeason">The Plex season to match.</param>
        /// <param name="seriesItem">The parent series item in Jellyfin.</param>
        /// <returns>The matching Jellyfin season, or null if no match is found.</returns>
        private BaseItem? FindMatchingJellyfinSeason(PlexSeason plexSeason, BaseItem seriesItem)
        {
            // Use the existing FindJellyfinSeason method that already handles this correctly
            return FindJellyfinSeason(seriesItem, plexSeason.Index);
        }

    }
}
