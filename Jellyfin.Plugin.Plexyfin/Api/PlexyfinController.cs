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
    }
}
