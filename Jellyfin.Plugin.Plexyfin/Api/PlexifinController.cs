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
    public class PlexifinController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProviderManager? _providerManager;
        private readonly IFileSystem? _fileSystem;
        private readonly ILogger<PlexifinController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexifinController"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/>.</param>
        /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/>. Can be null.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/>. Can be null.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{T}"/>.</param>
        public PlexifinController(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IHttpClientFactory httpClientFactory,
            IProviderManager? providerManager,
            IFileSystem? fileSystem,
            ILogger<PlexifinController> logger)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _collectionManager = collectionManager ?? throw new ArgumentNullException(nameof(collectionManager));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _providerManager = providerManager; // Can be null
            _fileSystem = fileSystem; // Can be null
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Tests connection to the Plex server and returns library sections.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [HttpGet("TestPlexConnection")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> TestPlexConnection([FromQuery] string? url = null, [FromQuery] string? token = null)
        {
            try
            {
                var config = Plugin.Instance!.Configuration;
                
                // Use the URL and token from query parameters if provided, otherwise use the ones from config
                string serverUrl = url ?? config.PlexServerUrl;
                string apiToken = token ?? config.PlexApiToken;
                
                // Validate configuration
                if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(apiToken))
                {
                    return BadRequest(new { Success = false, Error = "Plex server URL and API token must be provided" });
                }
                
                // Create a PlexClient instance
                var plexClient = new PlexClient(_httpClientFactory, _logger, serverUrl, apiToken);
                
                try
                {
                    // Try to get library sections to test the connection
                    var sections = await plexClient.GetLibrarySections().ConfigureAwait(false);
                    
                    // Return success with sections
                    // Return with explicit camelCase property names to match JavaScript conventions
                    return Ok(new 
                    { 
                        success = true, 
                        message = "Successfully connected to Plex Media Server",
                        libraries = sections.Select(s => new
                        {
                            id = s.Key,
                            title = s.Title,
                            type = s.Type,
                            // If no libraries have been explicitly selected yet, check all by default
                            isSelected = config.SelectedLibraries.Count == 0 || config.SelectedLibraries.Contains(s.Key)
                        }).ToList()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error connecting to Plex server at {Url}", serverUrl);
                    return Ok(new { success = false, error = "Cannot connect to Plex. Check your URL and API Token." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TestPlexConnection endpoint");
                return StatusCode(500, new { Success = false, Error = ex.Message });
            }
        }
        
        /// <summary>
        /// Updates the list of selected Plex libraries.
        /// </summary>
        /// <param name="libraryIds">The list of library IDs to select.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [HttpPost("UpdateSelectedLibraries")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public ActionResult UpdateSelectedLibraries([FromBody] List<string> libraryIds)
        {
            try
            {
                var config = Plugin.Instance!.Configuration;
                
                // Update the selected libraries in the configuration
                config.SelectedLibraries = libraryIds ?? new List<string>();
                
                // Save the updated configuration
                Plugin.Instance.SaveConfiguration();
                
                return Ok(new { Success = true, Message = "Selected libraries updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating selected libraries");
                return StatusCode(500, new { Success = false, Error = ex.Message });
            }
        }

        /// <summary>
        /// Syncs collections from Plex Media Server.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [HttpPost("SyncFromPlex")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> SyncFromPlex()
        {
            try
            {
                // This is optional validation if you want to enforce admin permissions
                // If you want to require admin permissions, uncomment this code:
                /*
                if (!User.IsInRole("Admin"))
                {
                    return Unauthorized(new { Error = "This endpoint requires administrator access" });
                }
                */
                
                var config = Plugin.Instance!.Configuration;
                
                // Validate configuration
                if (string.IsNullOrEmpty(config.PlexServerUrl) || string.IsNullOrEmpty(config.PlexApiToken))
                {
                    return BadRequest(new { Error = "Plex server URL and API token must be configured" });
                }
                
                var syncResult = await SyncFromPlexAsync().ConfigureAwait(false);
                
                return Ok(new
                {
                    Message = "Sync completed successfully",
                    CollectionsAdded = syncResult.CollectionsAdded,
                    CollectionsUpdated = syncResult.CollectionsUpdated,
                    PlaylistsAdded = syncResult.PlaylistsAdded,
                    PlaylistsUpdated = syncResult.PlaylistsUpdated
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing from Plex");
                return StatusCode(500, new { Error = ex.Message });
            }
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
            
            return result;
        }
        
        /// <summary>
        /// Deletes all existing collections in Jellyfin.
        /// </summary>
        /// <returns>The number of collections deleted.</returns>
        private async Task<int> DeleteExistingCollectionsAsync()
        {
            _logger.LogInformation("Deleting existing collections");
            
            try
            {
                // Get all collections
                var collections = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                });
                
                int deletedCount = 0;
                
                // Delete each collection
                foreach (var collection in collections)
                {
                    try
                    {
                        _logger.LogInformation("Deleting collection: {Name}", collection.Name);
                        
                        // Get all items in the collection
                        var itemIds = _libraryManager.GetItemIds(new InternalItemsQuery { 
                            AncestorIds = new[] { collection.Id } 
                        });
                        
                        // Remove all items from the collection
                        if (itemIds.Count > 0)
                        {
                            await _collectionManager.RemoveFromCollectionAsync(collection.Id, itemIds.ToArray()).ConfigureAwait(false);
                        }
                        
                        // Now manually delete the collection from the database
                        // We'll mark it for deletion by changing its name to indicate it should be removed
                        collection.Name = $"[DELETED] {collection.Name}";
                        await _libraryManager.UpdateItemAsync(collection, collection.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                        
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting collection {Name}", collection.Name);
                    }
                }
                
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting existing collections");
                return 0;
            }
        }
        
        /// <summary>
        /// Updates the metadata (description, artwork) for a collection.
        /// </summary>
        /// <param name="collectionId">The ID of the collection to update.</param>
        /// <param name="plexCollection">The Plex collection with metadata.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task UpdateCollectionMetadataAsync(Guid collectionId, PlexCollection plexCollection)
        {
            var config = Plugin.Instance!.Configuration;
            
            try
            {
                var boxSet = _libraryManager.GetItemById(collectionId);
                if (boxSet == null)
                {
                    _logger.LogWarning("Collection with ID {Id} not found", collectionId);
                    return;
                }
                
                bool hasChanges = false;
                
                // Set description/overview
                if (!string.IsNullOrEmpty(plexCollection.Summary))
                {
                    boxSet.Overview = plexCollection.Summary;
                    hasChanges = true;
                    _logger.LogInformation("Updated collection overview for {Title}", boxSet.Name);
                }
                
                // Save if we have changes so far
                if (hasChanges)
                {
                    // Save the updated metadata
                    await _libraryManager.UpdateItemAsync(boxSet, boxSet.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                }
                
                // Set images if enabled in config
                if (config.SyncArtwork && 
                    (!string.IsNullOrEmpty(plexCollection.ThumbUrl) || !string.IsNullOrEmpty(plexCollection.ArtUrl)))
                {
                    await SyncArtworkAsync(boxSet, plexCollection).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metadata for collection {Title}", plexCollection.Title);
            }
        }
        
        /// <summary>
        /// Syncs artwork (poster and backdrop) from Plex to Jellyfin for a collection.
        /// </summary>
        /// <param name="jellyfItem">The Jellyfin item to update.</param>
        /// <param name="plexCollection">The Plex collection with artwork URLs.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task SyncArtworkAsync(BaseItem jellyfItem, PlexCollection plexCollection)
        {
            if (jellyfItem == null)
            {
                _logger.LogError("Cannot sync artwork: Jellyfin item is null");
                return;
            }

            if (plexCollection == null)
            {
                _logger.LogError("Cannot sync artwork: Plex collection is null");
                return;
            }

            try
            {
                _logger.LogInformation("Starting artwork sync for {JellyfinTitle} from Plex collection {PlexTitle}", 
                    jellyfItem.Name, plexCollection.Title);
                
                // Process poster/thumbnail
                if (!string.IsNullOrEmpty(plexCollection.ThumbUrl))
                {
                    if (IsValidImageUrl(plexCollection.ThumbUrl))
                    {
                        // Ensure URL has a token
                        var thumbUrl = plexCollection.ThumbUrl;
                        if (thumbUrl.Contains("plex") && !thumbUrl.Contains("X-Plex-Token=") && !thumbUrl.Contains("token="))
                        {
                            var config = Plugin.Instance.Configuration;
                            var separator = thumbUrl.Contains("?") ? "&" : "?";
                            thumbUrl = $"{thumbUrl}{separator}X-Plex-Token={config.PlexApiToken}";
                            _logger.LogInformation("Added token to thumb URL for collection {Title}", jellyfItem.Name);
                        }
                        
                        _logger.LogInformation("Downloading poster image for collection {Title}: {Url}", 
                            jellyfItem.Name, thumbUrl);
                        
                        // Download and upload the image
                        await DownloadAndUploadImageAsync(thumbUrl, jellyfItem.Id, ImageType.Primary).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid poster URL for collection {Title}: {Url}", 
                            jellyfItem.Name, plexCollection.ThumbUrl);
                    }
                }
                else
                {
                    _logger.LogInformation("No poster image available for Plex collection {Title}", plexCollection.Title);
                }
                
                // Process backdrop/art
                if (!string.IsNullOrEmpty(plexCollection.ArtUrl))
                {
                    if (IsValidImageUrl(plexCollection.ArtUrl))
                    {
                        // Ensure URL has a token
                        var artUrl = plexCollection.ArtUrl;
                        if (artUrl.Contains("plex") && !artUrl.Contains("X-Plex-Token=") && !artUrl.Contains("token="))
                        {
                            var config = Plugin.Instance.Configuration;
                            var separator = artUrl.Contains("?") ? "&" : "?";
                            artUrl = $"{artUrl}{separator}X-Plex-Token={config.PlexApiToken}";
                            _logger.LogInformation("Added token to art URL for collection {Title}", jellyfItem.Name);
                        }
                        
                        _logger.LogInformation("Downloading backdrop image for collection {Title}: {Url}", 
                            jellyfItem.Name, artUrl);
                        
                        // Download and upload the image
                        await DownloadAndUploadImageAsync(artUrl, jellyfItem.Id, ImageType.Backdrop).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid backdrop URL for collection {Title}: {Url}", 
                            jellyfItem.Name, plexCollection.ArtUrl);
                    }
                }
                else
                {
                    _logger.LogInformation("No backdrop image available for Plex collection {Title}", plexCollection.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing artwork for collection {Title}", plexCollection.Title);
            }
        }
        
        /// <summary>
        /// Downloads an image from a URL and saves it directly to the Jellyfin collection directory.
        /// </summary>
        /// <param name="imageUrl">The URL of the image to download.</param>
        /// <param name="itemId">The ID of the item to associate the image with.</param>
        /// <param name="imageType">The type of image (Primary, Backdrop, etc.).</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task DownloadAndUploadImageAsync(string imageUrl, Guid itemId, ImageType imageType)
        {
            try
            {
                // Download the image
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                _logger.LogInformation("Downloading image from URL: {Url}", imageUrl);
                using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to download image: {StatusCode}", response.StatusCode);
                    return;
                }
                
                // Read the image data
                var imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("Downloaded image has zero length");
                    return;
                }
                
                _logger.LogInformation("Successfully downloaded image: {Length} bytes", imageBytes.Length);
                
                // Save to a temp file for debugging
                var tempDir = "/config/logs/plexyfin-debug";
                try
                {
                    Directory.CreateDirectory(tempDir);
                    var tempFile = Path.Combine(tempDir, $"debug_image_{DateTime.Now.Ticks}.jpg");
                    await System.IO.File.WriteAllBytesAsync(tempFile, imageBytes).ConfigureAwait(false);
                    _logger.LogInformation("Saved debug copy of image to {Path}", tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not save debug image file");
                }
                
                // Get the item to update directly using the library manager
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    _logger.LogError("Failed to find item with ID {ItemId} for image upload", itemId);
                    return;
                }
                
                try
                {
                    // Get the data directory path - typically /config/data in Docker installs
                    // This can be different in various installations, but this path is common
                    var dataPath = "/config/data";
                    
                    // Allow overriding the data path through environment variables
                    var envDataPath = Environment.GetEnvironmentVariable("JELLYFIN_DATA_PATH");
                    if (!string.IsNullOrEmpty(envDataPath))
                    {
                        dataPath = envDataPath;
                        _logger.LogInformation("Using data path from environment: {0}", dataPath);
                    }
                    // Collections are stored in the "collections" subfolder
                    var collectionsPath = Path.Combine(dataPath, "collections");
                    
                    // The collection folder is named after the collection name with [boxset] suffix
                    // NOT the UUID as we previously thought
                    var collectionName = item.Name + " [boxset]";
                    
                    // Replace any invalid filename characters
                    foreach (var invalidChar in Path.GetInvalidFileNameChars())
                    {
                        collectionName = collectionName.Replace(invalidChar, '_');
                    }
                    
                    var collectionFolder = Path.Combine(collectionsPath, collectionName);
                    
                    _logger.LogInformation("Using collection folder: {Path} instead of UUID-based path", collectionFolder);
                    
                    _logger.LogInformation("Collection data path: {Path}", collectionFolder);
                    
                    // Make sure the collection folder exists
                    if (!Directory.Exists(collectionFolder))
                    {
                        _logger.LogInformation("Creating collection folder: {Path}", collectionFolder);
                        Directory.CreateDirectory(collectionFolder);
                        
                        // Set permissions on the directory to ensure Jellyfin can access it
                        try
                        {
                            // Use bash to set permissions (777 is very permissive, but ensures access)
                            using var process = new System.Diagnostics.Process();
                            process.StartInfo.FileName = "chmod";
                            process.StartInfo.Arguments = $"777 \"{collectionFolder}\"";
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.RedirectStandardOutput = true;
                            process.StartInfo.RedirectStandardError = true;
                            process.Start();
                            
                            process.WaitForExit();
                            
                            _logger.LogInformation("Set permissions on collection folder");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to set permissions on collection folder");
                        }
                    }
                    
                    // Determine the image filename based on the image type
                    string imageFilename;
                    switch (imageType)
                    {
                        case ImageType.Primary:
                            imageFilename = "poster.jpg";
                            break;
                        case ImageType.Backdrop:
                            imageFilename = "backdrop.jpg";
                            break;
                        case ImageType.Logo:
                            imageFilename = "logo.jpg";
                            break;
                        case ImageType.Thumb:
                            imageFilename = "thumb.jpg";
                            break;
                        default:
                            imageFilename = $"{imageType.ToString().ToLowerInvariant()}.jpg";
                            break;
                    }
                    
                    // Full path to the image file
                    var imagePath = Path.Combine(collectionFolder, imageFilename);
                    
                    // Save the image to the collection folder
                    _logger.LogInformation("Saving image to collection folder: {Path}", imagePath);
                    await System.IO.File.WriteAllBytesAsync(imagePath, imageBytes).ConfigureAwait(false);
                    
                    // Set permissions on the image file
                    try
                    {
                        using var process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = "chmod";
                        process.StartInfo.Arguments = $"666 \"{imagePath}\"";
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.Start();
                        
                        process.WaitForExit();
                        
                        _logger.LogInformation("Set permissions on image file");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set permissions on image file");
                    }
                    
                    _logger.LogInformation("Successfully saved {ImageType} image for collection {Name}", 
                        imageType, item.Name);
                    
                    // Queue a refresh of the item to pick up the new image
                    _logger.LogInformation("Queuing metadata refresh for collection: {Name}", item.Name);
                    if (_providerManager != null)
                    {
                        // Trigger an image update first
                        await _libraryManager.UpdateItemAsync(
                            item, 
                            item.GetParent(), 
                            ItemUpdateType.ImageUpdate, 
                            CancellationToken.None).ConfigureAwait(false);
                        
                        // Since we're having issues with MetadataRefreshOptions constructor,
                        // let's use a simpler approach to trigger a refresh
                        _logger.LogInformation("Triggering image refresh for {0}", item.Name);
                        
                        // Use UpdateItemAsync with the ImageUpdate type to trigger a refresh
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.ImageUpdate,
                            CancellationToken.None).ConfigureAwait(false);
                        
                        // Also trigger a metadata refresh
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("Provider manager not available, using UpdateItemAsync");
                        // Trigger an image refresh
                        await _libraryManager.UpdateItemAsync(
                            item, 
                            item.GetParent(), 
                            ItemUpdateType.ImageUpdate, 
                            CancellationToken.None).ConfigureAwait(false);
                            
                        // Also trigger a metadata refresh
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving image for collection {Name}", item.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading and uploading image");
            }
        }
        
        
        /// <summary>
        /// Validates if a URL is a valid image URL.
        /// </summary>
        /// <param name="url">The URL to validate.</param>
        /// <returns>True if the URL is valid, otherwise false.</returns>
        private bool IsValidImageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }
            
            try
            {
                // Basic validation - ensure it's a valid URI
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    _logger.LogWarning("Invalid image URL format: {Url}", url);
                    return false;
                }
                
                // Check if the URL contains a Plex token
                if (url.Contains("X-Plex-Token=") || url.Contains("?token=") || url.Contains("&token="))
                {
                    return true;
                }
                
                // Check scheme
                if (uri.Scheme != "http" && uri.Scheme != "https")
                {
                    _logger.LogWarning("Invalid URL scheme: {Scheme}", uri.Scheme);
                    return false;
                }
                
                // Check for common image file extensions in path
                string path = uri.AbsolutePath.ToLowerInvariant();
                if (path.EndsWith(".jpg") || path.EndsWith(".jpeg") || 
                    path.EndsWith(".png") || path.EndsWith(".gif") || 
                    path.EndsWith(".webp") || path.EndsWith(".bmp"))
                {
                    return true;
                }
                
                // If the URL doesn't have a file extension but is from Plex server, it's probably valid
                if (url.Contains(Plugin.Instance!.Configuration.PlexServerUrl))
                {
                    return true;
                }
                
                // Otherwise, assume it's valid and let Jellyfin handle any issues
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating image URL: {Url}", url);
                return false;
            }
        }
        
        /// <summary>
        /// Represents the result of a Plex to Jellyfin sync operation.
        /// </summary>
        public class SyncResult
        {
            /// <summary>
            /// Gets or sets the number of collections added.
            /// </summary>
            public int CollectionsAdded { get; set; }
            
            /// <summary>
            /// Gets or sets the number of collections updated.
            /// </summary>
            public int CollectionsUpdated { get; set; }
            
            /// <summary>
            /// Gets or sets the number of playlists added.
            /// </summary>
            public int PlaylistsAdded { get; set; }
            
            /// <summary>
            /// Gets or sets the number of playlists updated.
            /// </summary>
            public int PlaylistsUpdated { get; set; }
        }
        
        private async Task<(int added, int updated)> SyncCollectionsFromPlexAsync(PlexClient plexClient)
        {
            _logger.LogInformation("Starting Plex collection sync");
            
            try
            {
                // Get all library sections from Plex
                var sections = await plexClient.GetLibrarySections().ConfigureAwait(false);
                _logger.LogInformation("Found {Count} library sections in Plex", sections.Count);
                
                // Get the list of selected libraries from configuration
                var config = Plugin.Instance!.Configuration;
                var selectedLibraries = config.SelectedLibraries;
                
                // Always filter based on selected libraries
                _logger.LogInformation("Filtering sections based on {Count} selected libraries", selectedLibraries.Count);
                sections = sections.Where(s => selectedLibraries.Contains(s.Key)).ToList();
                _logger.LogInformation("{Count} sections selected for processing", sections.Count);
                
                // If no libraries are selected, log a message and return early
                if (sections.Count == 0)
                {
                    _logger.LogWarning("No libraries selected for synchronization. Please select at least one library in the configuration.");
                }
                
                // Log sections for debugging
                foreach (var section in sections)
                {
                    _logger.LogInformation("Processing Plex section: {Title} (Type: {Type})", section.Title, section.Type);
                }
                
                int collectionsAdded = 0;
                int collectionsUpdated = 0;
                
                // Process all selected media sections (movies, shows, etc.)
                _logger.LogInformation("Processing library sections for collections");
                
                foreach (var section in sections)
                {
                    _logger.LogInformation("Processing collections in section: {Title} (Type: {Type})", section.Title, section.Type);
                    
                    try
                    {
                        // Get all collections in this section
                        var collections = await plexClient.GetCollections(section.Key).ConfigureAwait(false);
                        _logger.LogInformation("Found {Count} collections in section {Title}", collections.Count, section.Title);
                        
                        foreach (var collection in collections)
                        {
                            try
                            {
                                _logger.LogInformation("Processing collection: {Title} ({ItemCount} items)", collection.Title, collection.ItemCount);
                                
                                // Skip empty collections
                                if (collection.ItemCount == 0)
                                {
                                    _logger.LogInformation("Skipping empty collection: {Title}", collection.Title);
                                    continue;
                                }
                                
                                // Check if collection already exists in Jellyfin
                                var existingCollection = _libraryManager.GetItemList(new InternalItemsQuery
                                {
                                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                                    Name = collection.Title
                                }).FirstOrDefault();
                                
                                Guid collectionId = Guid.Empty; // Initialize with default value
                                bool isNewCollection = false;
                                
                                if (existingCollection != null)
                                {
                                    _logger.LogInformation("Collection already exists in Jellyfin, updating: {Title}", collection.Title);
                                    collectionId = existingCollection.Id;
                                }
                                else
                                {
                                    _logger.LogInformation("Creating new collection in Jellyfin: {Title}", collection.Title);
                                    isNewCollection = true;
                                }
                                
                                // Log key for debugging
                                _logger.LogInformation("Collection {Title} has key: {Key}", collection.Title, collection.Key);
                                
                                // Get items in this collection
                                var collectionItems = await plexClient.GetCollectionItems(collection.Key).ConfigureAwait(false);
                                _logger.LogInformation("Collection {Title} has {Count} items in Plex", collection.Title, collectionItems.Count);
                                
                                // Find matching items in Jellyfin
                                var jellyfItems = new List<Guid>();
                                
                                foreach (var item in collectionItems)
                                {
                                    // Determine the appropriate Jellyfin item type based on Plex item type
                                    BaseItemKind[] itemTypes;
                                    
                                    switch (item.Type.ToLowerInvariant())
                                    {
                                        case "movie":
                                            itemTypes = new[] { BaseItemKind.Movie };
                                            break;
                                        case "show":
                                            itemTypes = new[] { BaseItemKind.Series };
                                            break;
                                        case "season":
                                            itemTypes = new[] { BaseItemKind.Season };
                                            break;
                                        case "episode":
                                            itemTypes = new[] { BaseItemKind.Episode };
                                            break;
                                        default:
                                            // For unknown types, try both movies and series
                                            itemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series };
                                            break;
                                    }
                                    
                                    // Try to find by title and year
                                    var query = new InternalItemsQuery
                                    {
                                        Name = item.Title,
                                        IncludeItemTypes = itemTypes
                                    };
                                    
                                    if (item.Year > 0)
                                    {
                                        query.Years = new[] { item.Year };
                                    }
                                    
                                    var jellyfItem = _libraryManager.GetItemList(query).FirstOrDefault();
                                    
                                    if (jellyfItem != null)
                                    {
                                        _logger.LogInformation("Found matching item in Jellyfin for Plex item: {Title} (Type: {Type})", 
                                            item.Title, item.Type);
                                        jellyfItems.Add(jellyfItem.Id);
                                    }
                                    else
                                    {
                                        // If direct match fails, try a less restrictive search
                                        query = new InternalItemsQuery
                                        {
                                            SearchTerm = item.Title
                                        };
                                        
                                        var potentialMatches = _libraryManager.GetItemList(query)
                                            .Where(i => i.Name.Equals(item.Title, StringComparison.OrdinalIgnoreCase))
                                            .ToList();
                                            
                                        if (potentialMatches.Count > 0)
                                        {
                                            _logger.LogInformation("Found {Count} potential matches for Plex item: {Title} using search", 
                                                potentialMatches.Count, item.Title);
                                            // Use the first match
                                            jellyfItems.Add(potentialMatches[0].Id);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("No matching item found in Jellyfin for Plex item: {Title} ({Type}, {Year})", 
                                                item.Title, item.Type, item.Year > 0 ? item.Year.ToString() : "no year");
                                        }
                                    }
                                }
                                
                                if (jellyfItems.Count == 0)
                                {
                                    _logger.LogWarning("No matching items found in Jellyfin for collection: {Title}, skipping", collection.Title);
                                    continue;
                                }
                                
                                if (isNewCollection)
                                {
                                    // Create a new collection
                                    _logger.LogInformation("Creating collection {Title} with {Count} items", collection.Title, jellyfItems.Count);
                                    
                                    var jellyfCollection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                                    {
                                        Name = collection.Title,
                                        IsLocked = true,
                                        UserIds = Array.Empty<Guid>() // System collection
                                    }).ConfigureAwait(false);
                                    
                                    collectionId = jellyfCollection.Id;
                                    
                                    _logger.LogInformation("Successfully created collection: {Title} with ID {Id}", 
                                        jellyfCollection.Name, collectionId);
                                    
                                    collectionsAdded++;
                                }
                                else
                                {
                                    // For existing collections, first remove all current items
                                    _logger.LogInformation("Updating existing collection {Title} with new items", collection.Title);
                                    
                                    // Get current items in the collection
                                    var currentItemIds = _libraryManager.GetItemIds(new InternalItemsQuery { 
                                        AncestorIds = new[] { collectionId } 
                                    });
                                    
                                    if (currentItemIds.Count > 0)
                                    {
                                        _logger.LogInformation("Removing {Count} existing items from collection", currentItemIds.Count);
                                        await _collectionManager.RemoveFromCollectionAsync(collectionId, currentItemIds.ToArray()).ConfigureAwait(false);
                                    }
                                    
                                    collectionsUpdated++;
                                }
                                
                                // Add items to the collection (for both new and existing collections)
                                if (jellyfItems.Count > 0)
                                {
                                    _logger.LogInformation("Adding {Count} items to collection {Title}", jellyfItems.Count, collection.Title);
                                    await _collectionManager.AddToCollectionAsync(collectionId, jellyfItems.ToArray()).ConfigureAwait(false);
                                }
                                
                                // Set collection overview/description and images (for both new and existing collections)
                                await UpdateCollectionMetadataAsync(collectionId, collection).ConfigureAwait(false);
                                
                                _logger.LogInformation("Successfully {Action} collection: {Title} with {Count} items", 
                                    isNewCollection ? "created" : "updated", collection.Title, jellyfItems.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing collection {Title}", collection.Title);
                                // Continue with next collection
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting collections from section {Title}", section.Title);
                        // Continue with next section
                    }
                }
                
                return (collectionsAdded, collectionsUpdated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Plex collection synchronization");
                throw;
            }
        }
    }
}