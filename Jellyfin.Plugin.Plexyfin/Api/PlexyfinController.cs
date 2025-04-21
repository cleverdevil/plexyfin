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
                        syncStatus.Message = "Removing existing collections...";
                        syncStatus.Progress = 15;
                    }
                    
                    var deletedCount = await DeleteExistingCollectionsAsync().ConfigureAwait(false);
                    _logger.LogInformation("Removed {Count} existing collections (emptied and made invisible)", deletedCount);
                }
                else if (config.DeleteBeforeSync && dryRun)
                {
                    // In dry run mode, just count existing collections that would be deleted
                    var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                    });
                    _logger.LogInformation("Would remove {Count} existing collections (empty and make invisible)", collections.Count());
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
            
            
            // Watch state synchronization removed
            
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
        /// Empties all existing collections in Jellyfin by removing all items, making them invisible in UI.
        /// </summary>
        /// <returns>The number of collections emptied.</returns>
        private async Task<int> DeleteExistingCollectionsAsync()
        {
            // Get all BoxSet items (collections) from the library
            var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
            }).ToList();
            
            int count = 0;
            
            // We can't permanently delete collections, so we'll have to empty them
            // and make them invisible to the user, which effectively makes them "gone"
            foreach (var collection in collections)
            {
                try
                {
                    // Step 1: First empty the collection by removing all items from it
                    await _collectionManager.RemoveFromCollectionAsync(collection.Id, Array.Empty<Guid>()).ConfigureAwait(false);
                    
                    // Step 2: Make the collection invisible to users by:
                    // - Setting a special name starting with "."
                    // - Setting IsVisible to false if the property exists
                    // - Setting other properties that may hide it in the UI
                    collection.Name = $".deleted_{collection.Id}_{DateTime.Now.Ticks}";
                    
                    try
                    {
                        // Try to use reflection to set IsVisible property if it exists
                        var isVisibleProperty = collection.GetType().GetProperty("IsVisible");
                        if (isVisibleProperty != null)
                        {
                            isVisibleProperty.SetValue(collection, false);
                        }
                        
                        // Try to set other properties that might hide the collection
                        var lockProperty = collection.GetType().GetProperty("IsLocked");
                        if (lockProperty != null)
                        {
                            lockProperty.SetValue(collection, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error setting visibility properties on collection {Id}", collection.Id);
                    }
                    
                    // Update the collection in the database
                    await _libraryManager.UpdateItemAsync(
                        collection,
                        collection.GetParent(),
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None).ConfigureAwait(false);
                    
                    _logger.LogDebug("Made collection invisible: {Id}", collection.Id);
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing collection: {Name}", collection.Name);
                }
            }
            
            // Just give the system a moment to process changes
            await Task.Delay(500).ConfigureAwait(false);
            
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
                                        // Before creating a new collection, make sure no collection with this name already exists
                                        var existingCollectionCheck = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                                        {
                                            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                                            Name = collection.Title
                                        }).FirstOrDefault();
                                        
                                        if (existingCollectionCheck != null)
                                        {
                                            // If one still exists with this name, we need to make it invisible to avoid conflicts
                                            _logger.LogWarning("Found existing collection with name {Title}, making it invisible first", collection.Title);
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
                                                await ProcessCollectionArtwork(newCollection, collection).ConfigureAwait(false);
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
                                            await ProcessCollectionArtwork(existingCollection, collection).ConfigureAwait(false);
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
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessCollectionArtwork(BaseItem jellyfinCollection, PlexCollection plexCollection)
        {
            if (jellyfinCollection == null)
            {
                _logger.LogWarning("Cannot process artwork for null collection");
                return;
            }
            
            _logger.LogDebug("Processing artwork for collection: {CollectionName}", jellyfinCollection.Name);
            
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Plexyfin-Plugin");
            bool hasChanges = false;
            
            try 
            {
                // Process primary image (poster/thumbnail)
                if (plexCollection.ThumbUrl != null)
                {
                    _logger.LogDebug("Downloading thumbnail: {ThumbUrl}", plexCollection.ThumbUrl);
                    
                    var response = await httpClient.GetAsync(plexCollection.ThumbUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    
                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        mimeType = response.Content.Headers.ContentType.MediaType;
                        _logger.LogDebug("Using MIME type from response: {MimeType}", mimeType);
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexCollection.ThumbUrl.ToString());
                        _logger.LogDebug("Using MIME type from URL: {MimeType}", mimeType);
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
                            _logger.LogWarning(ex, "I/O error saving primary image for collection {CollectionName}, will retry after delay", jellyfinCollection.Name);
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
                        _logger.LogDebug("Successfully saved thumbnail for collection: {CollectionName}", jellyfinCollection.Name);
                    }
                    else
                    {
                        _logger.LogWarning("Provider manager is null, cannot save thumbnail for collection: {CollectionName}", jellyfinCollection.Name);
                    }
                }
                
                // Process background image (art)
                if (plexCollection.ArtUrl != null)
                {
                    _logger.LogDebug("Downloading art: {ArtUrl}", plexCollection.ArtUrl);
                    
                    var response = await httpClient.GetAsync(plexCollection.ArtUrl).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    
                    // Get content type from the response if available
                    string mimeType = "image/png"; // Default to image/png
                    if (response.Content.Headers.ContentType != null)
                    {
                        mimeType = response.Content.Headers.ContentType.MediaType;
                        _logger.LogDebug("Using MIME type from response: {MimeType}", mimeType);
                    }
                    else
                    {
                        // Try to determine MIME type from URL
                        mimeType = GetMimeTypeFromUrl(plexCollection.ArtUrl.ToString());
                        _logger.LogDebug("Using MIME type from URL: {MimeType}", mimeType);
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
                            _logger.LogWarning(ex, "I/O error saving backdrop image for collection {CollectionName}, will retry after delay", jellyfinCollection.Name);
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
                        _logger.LogDebug("Successfully saved art for collection: {CollectionName}", jellyfinCollection.Name);
                    }
                    else
                    {
                        _logger.LogWarning("Provider manager is null, cannot save backdrop for collection: {CollectionName}", jellyfinCollection.Name);
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
                        
                    _logger.LogDebug("Updated repository with new images for collection: {CollectionName}", jellyfinCollection.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing artwork for collection {CollectionName}", jellyfinCollection.Name);
            }
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting library name for item {ItemName}", item.Name);
                return null;
            }
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error determining MIME type from URL: {Url}", url);
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