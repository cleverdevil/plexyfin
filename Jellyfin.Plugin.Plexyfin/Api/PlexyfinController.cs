#nullable enable

using System;
using System.Collections.Generic;
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
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task<SyncResult> SyncFromPlexAsync()
        {
            var config = Plugin.Instance!.Configuration;
            var result = new SyncResult();
            
            // Create a PlexClient instance
            var plexClient = new PlexClient(_httpClientFactory, _logger, config.PlexServerUrl, config.PlexApiToken);
            
            // Sync collections if enabled
            if (config.SyncCollections)
            {
                _logger.LogInformation("Starting collection sync from Plex");
                
                // Delete existing collections if configured to do so
                if (config.DeleteBeforeSync)
                {
                    var deletedCount = await DeleteExistingCollectionsAsync().ConfigureAwait(false);
                    _logger.LogInformation("Deleted {Count} existing collections", deletedCount);
                }
                
                var collectionResults = await SyncCollectionsFromPlexAsync(plexClient).ConfigureAwait(false);
                result.CollectionsAdded = collectionResults.added;
                result.CollectionsUpdated = collectionResults.updated;
                
                _logger.LogInformation("Collection sync completed. Added {Added} collections, updated {Updated} collections", 
                    result.CollectionsAdded, result.CollectionsUpdated);
            }
            
            // Sync playlists if enabled (not implemented yet)
            if (config.SyncPlaylists)
            {
                _logger.LogInformation("Playlist sync not implemented yet");
                // Future implementation
            }
            
            // Sync watch state if enabled
            if (config.SyncWatchState && _userManager != null && _userDataManager != null)
            {
                try
                {
                    _logger.LogInformation("Starting watch state synchronization");
                    
                    // Get all media items from Jellyfin
                    var allItems = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode }
                    });
                    
                    // Create watch state manager
                    var watchStateManager = new PlexWatchStateManager(
                        _logger,
                        _httpClientFactory,
                        _userManager,
                        _userDataManager);
                        
                    // Sync watch states
                    await watchStateManager.SyncWatchStateAsync(
                        allItems,
                        config.PlexServerUrl,
                        config.PlexApiToken,
                        config.SyncWatchStateDirection);
                        
                    _logger.LogInformation("Watch state synchronization completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing watch states");
                }
            }
            
            return result;
        }

        /// <summary>
        /// Deletes all existing collections in Jellyfin.
        /// </summary>
        /// <returns>The number of collections deleted.</returns>
        private async Task<int> DeleteExistingCollectionsAsync()
        {
            var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet }
            });
            
            int count = 0;
            foreach (var collection in collections)
            {
                await _collectionManager.RemoveFromCollectionAsync(collection.Id, Array.Empty<string>()).ConfigureAwait(false);
                count++;
            }
            
            return count;
        }

        /// <summary>
        /// Syncs collections from Plex to Jellyfin.
        /// </summary>
        /// <param name="plexClient">The Plex client.</param>
        /// <returns>A tuple containing the number of collections added and updated.</returns>
        private async Task<(int added, int updated)> SyncCollectionsFromPlexAsync(PlexClient plexClient)
        {
            int added = 0;
            int updated = 0;
            
            // Get selected libraries from configuration
            var config = Plugin.Instance!.Configuration;
            var selectedLibraries = config.SelectedLibraries ?? new List<string>();
            
            // Get collections from Plex
            foreach (var libraryId in selectedLibraries)
            {
                var collections = await plexClient.GetCollections(libraryId).ConfigureAwait(false);
                
                foreach (var collection in collections)
                {
                    // Check if collection already exists in Jellyfin
                    var existingCollection = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        Name = collection.Title,
                        IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                    }).FirstOrDefault();
                    
                    // Get collection items from Plex
                    var plexItems = await plexClient.GetCollectionItems(collection.Id).ConfigureAwait(false);
                    
                    // Find matching items in Jellyfin
                    var jellyfinItems = new List<Guid>();
                    foreach (var plexItem in plexItems)
                    {
                        // Try to find by name
                        var jellyfinItem = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                        {
                            Name = plexItem.Title,
                            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
                        }).FirstOrDefault();
                        
                        if (jellyfinItem != null)
                        {
                            jellyfinItems.Add(jellyfinItem.Id);
                        }
                    }
                    
                    if (existingCollection == null)
                    {
                        // Create new collection
                        var collectionId = await _collectionManager.CreateCollectionAsync(new MediaBrowser.Controller.Collections.CollectionCreationOptions
                        {
                            Name = collection.Title,
                            ItemIdList = jellyfinItems.Select(id => id.ToString()).ToList(),
                            IsLocked = true
                        }).ConfigureAwait(false);
                        
                        // Update collection metadata
                        var newCollection = _libraryManager.GetItemById(collectionId);
                        if (newCollection != null)
                        {
                            // Set overview
                            newCollection.Overview = collection.Summary;
                            
                            // Save changes
                            await _libraryManager.UpdateItemAsync(newCollection, newCollection.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                            
                            // Sync artwork if enabled
                            if (config.SyncArtwork)
                            {
                                await SyncCollectionArtworkAsync(newCollection, collection, plexClient).ConfigureAwait(false);
                            }
                        }
                        
                        added++;
                    }
                    else
                    {
                        // Update existing collection
                        existingCollection.Overview = collection.Summary;
                        
                        // Update items
                        await _collectionManager.AddToCollectionAsync(existingCollection.Id, jellyfinItems.ToArray()).ConfigureAwait(false);
                        
                        // Save changes
                        await _libraryManager.UpdateItemAsync(existingCollection, existingCollection.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                        
                        // Sync artwork if enabled
                        if (config.SyncArtwork)
                        {
                            await SyncCollectionArtworkAsync(existingCollection, collection, plexClient).ConfigureAwait(false);
                        }
                        
                        updated++;
                    }
                }
            }
            
            return (added, updated);
        }

        /// <summary>
        /// Syncs artwork from a Plex collection to a Jellyfin collection.
        /// </summary>
        /// <param name="jellyfinCollection">The Jellyfin collection.</param>
        /// <param name="plexCollection">The Plex collection.</param>
        /// <param name="plexClient">The Plex client.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task SyncCollectionArtworkAsync(BaseItem jellyfinCollection, PlexCollection plexCollection, PlexClient plexClient)
        {
            // This is a simplified implementation - in a real implementation, we would:
            // 1. Download the artwork from Plex
            // 2. Save it to the appropriate location for Jellyfin
            // 3. Refresh the metadata for the collection
            
            _logger.LogInformation("Syncing artwork for collection: {CollectionName}", jellyfinCollection.Name);
            
            // For now, just log that we would sync the artwork
            if (!string.IsNullOrEmpty(plexCollection.ThumbUrl))
            {
                _logger.LogInformation("Would sync thumbnail: {ThumbUrl}", plexCollection.ThumbUrl);
            }
            
            if (!string.IsNullOrEmpty(plexCollection.ArtUrl))
            {
                _logger.LogInformation("Would sync art: {ArtUrl}", plexCollection.ArtUrl);
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// API endpoint for syncing from Plex.
        /// </summary>
        /// <returns>The result of the sync operation.</returns>
        [HttpPost("sync")]
        [Authorize(Policy = "RequiresElevation")]
        public async Task<ActionResult<SyncResult>> SyncFromPlexEndpoint()
        {
            try
            {
                var result = await SyncFromPlexAsync().ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing from Plex");
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
        public async Task<ActionResult<IEnumerable<PlexLibrary>>> TestConnection(
            [Required] string url,
            [Required] string token)
        {
            try
            {
                var plexClient = new PlexClient(_httpClientFactory, _logger, url, token);
                var libraries = await plexClient.GetLibraries().ConfigureAwait(false);
                
                // Save the URL and token to the configuration
                var config = Plugin.Instance!.Configuration;
                config.PlexServerUrl = url;
                config.PlexApiToken = token;
                Plugin.Instance.SaveConfiguration();
                
                return Ok(libraries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Plex connection");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}
