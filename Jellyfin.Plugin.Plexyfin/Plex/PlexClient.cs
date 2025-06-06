using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Xml.Linq;
using System.Linq;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.Plexyfin.Configuration;

namespace Jellyfin.Plugin.Plexyfin.Plex
{
    /// <summary>
    /// Client for interacting with Plex Media Server.
    /// </summary>
    public class PlexClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly Uri _baseUrl;
        private readonly string _token;
        private readonly PluginConfiguration _config;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexClient"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        /// <param name="baseUrl">The base URL of the Plex server.</param>
        /// <param name="token">The Plex API token.</param>
        /// <param name="config">The plugin configuration.</param>
        public PlexClient(IHttpClientFactory httpClientFactory, ILogger logger, Uri baseUrl, string token, PluginConfiguration? config = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _baseUrl = baseUrl;
            _token = token;
            _config = config ?? new PluginConfiguration();
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="PlexClient"/> class with string URL.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        /// <param name="baseUrl">The base URL of the Plex server as a string.</param>
        /// <param name="token">The Plex API token.</param>
        /// <param name="config">The plugin configuration.</param>
        public PlexClient(IHttpClientFactory httpClientFactory, ILogger logger, string baseUrl, string token, PluginConfiguration? config = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _baseUrl = new Uri(baseUrl);
            _token = token;
            _config = config ?? new PluginConfiguration();
        }

        /// <summary>
        /// Gets the available libraries from Plex.
        /// </summary>
        /// <returns>A list of Plex libraries.</returns>
        public async Task<List<PlexLibrary>> GetLibraries()
        {
            var libraries = new List<PlexLibrary>();
            
            try
            {
                using var client = _httpClientFactory.CreateClient();
                var url = new Uri(_baseUrl, $"library/sections?X-Plex-Token={_token}");
                
                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                
                foreach (var directory in doc.Descendants("Directory"))
                {
                    var library = new PlexLibrary
                    {
                        Id = directory.Attribute("key")?.Value ?? string.Empty,
                        Title = directory.Attribute("title")?.Value ?? string.Empty,
                        Type = directory.Attribute("type")?.Value ?? string.Empty
                    };
                    
                    libraries.Add(library);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorGettingLibraries(ex);
            }
            catch (UriFormatException ex)
            {
                _logger.LogErrorGettingLibraries(ex);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogErrorGettingLibraries(ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingLibraries(ex);
            }
            
            return libraries;
        }

        /// <summary>
        /// Gets collections from a Plex library.
        /// </summary>
        /// <param name="libraryId">The library ID.</param>
        /// <returns>A list of Plex collections.</returns>
        public async Task<List<PlexCollection>> GetCollections(string libraryId)
        {
            var collections = new List<PlexCollection>();
            
            try
            {
                using var client = _httpClientFactory.CreateClient();
                var url = new Uri(_baseUrl, $"library/sections/{libraryId}/collections?X-Plex-Token={_token}");
                
                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = XDocument.Parse(content);
                
                foreach (var directory in doc.Descendants("Directory"))
                {
                    var collection = new PlexCollection
                    {
                        Id = directory.Attribute("ratingKey")?.Value ?? string.Empty,
                        Title = directory.Attribute("title")?.Value ?? string.Empty,
                        SortTitle = directory.Attribute("titleSort")?.Value ?? string.Empty,
                        Summary = directory.Attribute("summary")?.Value ?? string.Empty
                    };
                    
                    // Set thumbnail URL if available
                    string? thumbPath = directory.Attribute("thumb")?.Value;
                    if (!string.IsNullOrEmpty(thumbPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string thumbWithToken = thumbPath;
                        if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            thumbWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            thumbWithToken += $"?X-Plex-Token={_token}";
                        }
                        collection.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                    }
                    
                    // Set art URL if available
                    string? artPath = directory.Attribute("art")?.Value;
                    if (!string.IsNullOrEmpty(artPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string artWithToken = artPath;
                        if (artWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            artWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            artWithToken += $"?X-Plex-Token={_token}";
                        }
                        collection.ArtUrl = new Uri(_baseUrl, artWithToken);
                    }
                    
                    collections.Add(collection);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorGettingCollections(libraryId, ex);
            }
            catch (UriFormatException ex)
            {
                _logger.LogErrorGettingCollections(libraryId, ex);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogErrorGettingCollections(libraryId, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingCollections(libraryId, ex);
            }
            
            return collections;
        }

        /// <summary>
        /// Gets all items in a Plex library.
        /// </summary>
        /// <param name="libraryId">The library ID.</param>
        /// <returns>A list of Plex items.</returns>
        public async Task<List<PlexItem>> GetLibraryItems(string libraryId)
        {
            var items = new List<PlexItem>();
            
            try
            {
                using var client = _httpClientFactory.CreateClient();
                // Include external IDs (Guids) in the response for better matching
                var url = new Uri(_baseUrl, $"library/sections/{libraryId}/all?includeGuids=1&includeExternalMedia=1&X-Plex-Token={_token}");
                
                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = XDocument.Parse(content);
                
                // Look for Video elements (movies) or Directory elements (TV shows)
                var videoElements = doc.Descendants("Video").ToList();
                var directoryElements = doc.Descendants("Directory").ToList();
                
                foreach (var element in videoElements)
                {
                    var item = new PlexItem
                    {
                        Id = element.Attribute("ratingKey")?.Value ?? string.Empty,
                        Title = element.Attribute("title")?.Value ?? string.Empty,
                        Type = element.Attribute("type")?.Value ?? "movie"
                    };
                    
                    // Extract external IDs from Guid elements
                    ExtractExternalIds(element, item);
                    
                    // Set thumbnail URL if available
                    string? thumbPath = element.Attribute("thumb")?.Value;
                    if (!string.IsNullOrEmpty(thumbPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string thumbWithToken = thumbPath;
                        if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            thumbWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            thumbWithToken += $"?X-Plex-Token={_token}";
                        }
                        item.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                    }
                    
                    // Set art URL if available
                    string? artPath = element.Attribute("art")?.Value;
                    if (!string.IsNullOrEmpty(artPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string artWithToken = artPath;
                        if (artWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            artWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            artWithToken += $"?X-Plex-Token={_token}";
                        }
                        item.ArtUrl = new Uri(_baseUrl, artWithToken);
                    }
                    
                    items.Add(item);
                }
                
                foreach (var element in directoryElements)
                {
                    var item = new PlexItem
                    {
                        Id = element.Attribute("ratingKey")?.Value ?? string.Empty,
                        Title = element.Attribute("title")?.Value ?? string.Empty,
                        Type = element.Attribute("type")?.Value ?? "show"
                    };
                    
                    // Extract external IDs from Guid elements
                    ExtractExternalIds(element, item);
                    
                    // Set thumbnail URL if available
                    string? thumbPath = element.Attribute("thumb")?.Value;
                    if (!string.IsNullOrEmpty(thumbPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string thumbWithToken = thumbPath;
                        if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            thumbWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            thumbWithToken += $"?X-Plex-Token={_token}";
                        }
                        item.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                    }
                    
                    // Set art URL if available
                    string? artPath = element.Attribute("art")?.Value;
                    if (!string.IsNullOrEmpty(artPath))
                    {
                        // Append Plex token to the URL to allow authenticated access
                        string artWithToken = artPath;
                        if (artWithToken.Contains('?', StringComparison.Ordinal))
                        {
                            artWithToken += $"&X-Plex-Token={_token}";
                        }
                        else
                        {
                            artWithToken += $"?X-Plex-Token={_token}";
                        }
                        item.ArtUrl = new Uri(_baseUrl, artWithToken);
                    }
                    
                    items.Add(item);
                }
                
                _logger.LogLibraryItems(libraryId, items.Count);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorGettingLibraryItems(libraryId, ex);
            }
            catch (UriFormatException ex)
            {
                _logger.LogErrorGettingLibraryItems(libraryId, ex);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogErrorGettingLibraryItems(libraryId, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingLibraryItems(libraryId, ex);
            }
            
            return items;
        }
        
        /// <summary>
        /// Gets seasons for a TV series from Plex.
        /// </summary>
        /// <param name="seriesId">The TV series ID.</param>
        /// <returns>A list of Plex seasons.</returns>
        public async Task<List<PlexSeason>> GetTvSeriesSeasons(string seriesId)
        {
            var seasons = new List<PlexSeason>();

            try
            {
                using var client = _httpClientFactory.CreateClient();
                var url = new Uri(_baseUrl, $"library/metadata/{seriesId}/children?X-Plex-Token={_token}");

                _logger.LogInformation("Getting seasons for TV series ID {0}", seriesId);

                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = XDocument.Parse(content);

                // Get the series title for reference
                string seriesTitle = doc.Root?.Attribute("parentTitle")?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(seriesTitle))
                {
                    seriesTitle = doc.Root?.Attribute("grandparentTitle")?.Value ?? string.Empty;
                }
                
                // Extract series external IDs - these will be inherited by all seasons
                string? seriesTvdbId = null;
                var rootGuidElements = doc.Root?.Descendants("Guid").ToList() ?? new List<XElement>();
                foreach (var guid in rootGuidElements)
                {
                    var id = guid.Attribute("id")?.Value;
                    if (!string.IsNullOrEmpty(id) && id.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase))
                    {
                        seriesTvdbId = id.Substring(7); // Remove "tvdb://" prefix
                        _logger.LogDebug("Found TVDb ID for series '{0}': {1}", seriesTitle, seriesTvdbId);
                        break;
                    }
                }

                // Look for Directory elements (seasons)
                var seasonElements = doc.Descendants("Directory").ToList();

                foreach (var element in seasonElements)
                {
                    var seasonType = element.Attribute("type")?.Value ?? string.Empty;

                    // Only process elements that are actually seasons
                    if (seasonType.Equals("season", StringComparison.OrdinalIgnoreCase))
                    {
                        var seasonIndexAttr = element.Attribute("index")?.Value;
                        int.TryParse(seasonIndexAttr, out int seasonIndex);

                        var season = new PlexSeason
                        {
                            Id = element.Attribute("ratingKey")?.Value ?? string.Empty,
                            Title = element.Attribute("title")?.Value ?? string.Empty,
                            Index = seasonIndex,
                            SeriesId = seriesId,
                            SeriesTitle = seriesTitle,
                            TvdbId = seriesTvdbId // Inherit from parent series
                        };

                        // Set thumbnail URL if available
                        string? thumbPath = element.Attribute("thumb")?.Value;
                        if (!string.IsNullOrEmpty(thumbPath))
                        {
                            // Append Plex token to the URL to allow authenticated access
                            string thumbWithToken = thumbPath;
                            if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                            {
                                thumbWithToken += $"&X-Plex-Token={_token}";
                            }
                            else
                            {
                                thumbWithToken += $"?X-Plex-Token={_token}";
                            }
                            season.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                        }

                        // Set art URL if available
                        string? artPath = element.Attribute("art")?.Value;
                        if (!string.IsNullOrEmpty(artPath))
                        {
                            // Append Plex token to the URL to allow authenticated access
                            string artWithToken = artPath;
                            if (artWithToken.Contains('?', StringComparison.Ordinal))
                            {
                                artWithToken += $"&X-Plex-Token={_token}";
                            }
                            else
                            {
                                artWithToken += $"?X-Plex-Token={_token}";
                            }
                            season.ArtUrl = new Uri(_baseUrl, artWithToken);
                        }

                        seasons.Add(season);
                    }
                }

                _logger.LogInformation("Found {0} seasons for TV series ID {1}", seasons.Count, seriesId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting seasons for TV series ID {0}", seriesId);
            }
            catch (UriFormatException ex)
            {
                _logger.LogError(ex, "Error with URI format when getting seasons for TV series ID {0}", seriesId);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogError(ex, "Error parsing XML when getting seasons for TV series ID {0}", seriesId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation when getting seasons for TV series ID {0}", seriesId);
            }

            return seasons;
        }

        /// <summary>
        /// Gets items in a Plex collection.
        /// </summary>
        /// <param name="collectionId">The collection ID.</param>
        /// <returns>A list of Plex items.</returns>
        public async Task<List<PlexItem>> GetCollectionItems(string collectionId)
        {
            var items = new List<PlexItem>();
            
            try
            {
                // Define a prioritized list of URL patterns to try
                var urlPatterns = new List<Uri>
                {
                    // Most common pattern
                    new Uri(_baseUrl, $"library/collections/{collectionId}/children?X-Plex-Token={_token}"),
                    
                    // Alternative pattern for older Plex servers
                    new Uri(_baseUrl, $"library/metadata/{collectionId}/children?X-Plex-Token={_token}"),
                    
                    // Another alternative pattern
                    new Uri(_baseUrl, $"library/collections/{collectionId}/all?X-Plex-Token={_token}"),
                    
                    // Yet another pattern
                    new Uri(_baseUrl, $"library/metadata/{collectionId}/items?X-Plex-Token={_token}"),
                    
                    // Last resort pattern
                    new Uri(_baseUrl, $"library/collections/{collectionId}/items?X-Plex-Token={_token}")
                };
                
                // Use all patterns since MaxUrlPatternAttempts was removed
                int maxAttempts = urlPatterns.Count;
                _logger.LogCollectionUrlPatterns(maxAttempts, collectionId);
                
                using var client = _httpClientFactory.CreateClient();
                bool success = false;
                
                // Try each pattern until one works or we reach the maximum attempts
                for (int i = 0; i < maxAttempts && !success; i++)
                {
                    try
                    {
                        var url = urlPatterns[i];
                        _logger.LogTryingUrlPattern(i + 1, maxAttempts, url.ToString());
                        
                        var response = await client.GetAsync(url).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var doc = XDocument.Parse(content);
                        
                        // Look for Video elements (movies) or Directory elements (TV shows)
                        var videoElements = doc.Descendants("Video").ToList();
                        var directoryElements = doc.Descendants("Directory").ToList();
                        
                        foreach (var element in videoElements)
                        {
                            var item = new PlexItem
                            {
                                Id = element.Attribute("ratingKey")?.Value ?? string.Empty,
                                Title = element.Attribute("title")?.Value ?? string.Empty,
                                Type = element.Attribute("type")?.Value ?? "movie"
                            };
                            
                            // Extract external IDs from Guid elements
                            ExtractExternalIds(element, item);
                            
                            // Set thumbnail URL if available
                            string? thumbPath = element.Attribute("thumb")?.Value;
                            if (!string.IsNullOrEmpty(thumbPath))
                            {
                                // Append Plex token to the URL to allow authenticated access
                                string thumbWithToken = thumbPath;
                                if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                                {
                                    thumbWithToken += $"&X-Plex-Token={_token}";
                                }
                                else
                                {
                                    thumbWithToken += $"?X-Plex-Token={_token}";
                                }
                                item.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                            }
                            
                            // Set art URL if available
                            string? artPath = element.Attribute("art")?.Value;
                            if (!string.IsNullOrEmpty(artPath))
                            {
                                // Append Plex token to the URL to allow authenticated access
                                string artWithToken = artPath;
                                if (artWithToken.Contains('?', StringComparison.Ordinal))
                                {
                                    artWithToken += $"&X-Plex-Token={_token}";
                                }
                                else
                                {
                                    artWithToken += $"?X-Plex-Token={_token}";
                                }
                                item.ArtUrl = new Uri(_baseUrl, artWithToken);
                            }
                            
                            items.Add(item);
                        }
                        
                        foreach (var element in directoryElements)
                        {
                            var item = new PlexItem
                            {
                                Id = element.Attribute("ratingKey")?.Value ?? string.Empty,
                                Title = element.Attribute("title")?.Value ?? string.Empty,
                                Type = element.Attribute("type")?.Value ?? "show"
                            };
                            
                            // Extract external IDs from Guid elements
                            ExtractExternalIds(element, item);
                            
                            // Set thumbnail URL if available
                            string? thumbPath = element.Attribute("thumb")?.Value;
                            if (!string.IsNullOrEmpty(thumbPath))
                            {
                                // Append Plex token to the URL to allow authenticated access
                                string thumbWithToken = thumbPath;
                                if (thumbWithToken.Contains('?', StringComparison.Ordinal))
                                {
                                    thumbWithToken += $"&X-Plex-Token={_token}";
                                }
                                else
                                {
                                    thumbWithToken += $"?X-Plex-Token={_token}";
                                }
                                item.ThumbUrl = new Uri(_baseUrl, thumbWithToken);
                            }
                            
                            // Set art URL if available
                            string? artPath = element.Attribute("art")?.Value;
                            if (!string.IsNullOrEmpty(artPath))
                            {
                                // Append Plex token to the URL to allow authenticated access
                                string artWithToken = artPath;
                                if (artWithToken.Contains('?', StringComparison.Ordinal))
                                {
                                    artWithToken += $"&X-Plex-Token={_token}";
                                }
                                else
                                {
                                    artWithToken += $"?X-Plex-Token={_token}";
                                }
                                item.ArtUrl = new Uri(_baseUrl, artWithToken);
                            }
                            
                            items.Add(item);
                        }
                        
                        success = true;
                        _logger.LogSuccessfulUrlPattern(items.Count, i + 1);
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogErrorUrlPattern(i + 1, maxAttempts, collectionId, ex);
                        
                        // Continue to the next pattern if this one failed
                        continue;
                    }
                    catch (UriFormatException ex)
                    {
                        _logger.LogErrorUrlPattern(i + 1, maxAttempts, collectionId, ex);
                        continue;
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        _logger.LogErrorUrlPattern(i + 1, maxAttempts, collectionId, ex);
                        continue;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogErrorUrlPattern(i + 1, maxAttempts, collectionId, ex);
                        continue;
                    }
                }
                
                if (!success)
                {
                    _logger.LogFailedUrlPatterns(maxAttempts);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogErrorGettingCollectionItems(collectionId, ex);
            }
            catch (UriFormatException ex)
            {
                _logger.LogErrorGettingCollectionItems(collectionId, ex);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogErrorGettingCollectionItems(collectionId, ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorGettingCollectionItems(collectionId, ex);
            }
            
            return items;
        }
        
        /// <summary>
        /// Extracts external IDs from Plex XML element.
        /// </summary>
        /// <param name="element">The XML element containing the item data.</param>
        /// <param name="item">The PlexItem to populate with external IDs.</param>
        private void ExtractExternalIds(XElement element, PlexItem item)
        {
            _logger.LogDebug("Extracting external IDs for '{0}'", item.Title);
            
            // Log the element structure for debugging
            _logger.LogDebug("Element name: {0}, has {1} attributes", element.Name.LocalName, element.Attributes().Count());
            
            // Log first few attributes
            foreach (var attr in element.Attributes().Take(5))
            {
                _logger.LogDebug("  Attribute: {0} = {1}", attr.Name.LocalName, attr.Value);
            }
            
            // Check if we need to look at a different level - Plex might put Guid as direct children
            var guidElements = element.Elements("Guid").ToList();
            if (guidElements.Count == 0)
            {
                _logger.LogDebug("No direct Guid children found, searching descendants...");
                guidElements = element.Descendants("Guid").ToList();
            }
            
            _logger.LogDebug("Found {0} Guid elements for '{1}'", guidElements.Count, item.Title);
            
            foreach (var guid in guidElements)
            {
                var id = guid.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(id))
                {
                    _logger.LogWarning("Found Guid element with empty id attribute for '{0}'", item.Title);
                    continue;
                }
                
                _logger.LogDebug("Processing Guid: '{0}' for '{1}'", id, item.Title);
                
                // Parse IMDb ID (format: imdb://tt1234567)
                if (id.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                {
                    item.ImdbId = id.Substring(7); // Remove "imdb://" prefix
                    _logger.LogDebug("Extracted IMDb ID: '{0}' for '{1}'", item.ImdbId, item.Title);
                }
                // Parse TMDb ID (format: tmdb://12345)
                else if (id.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase))
                {
                    item.TmdbId = id.Substring(7); // Remove "tmdb://" prefix
                    _logger.LogDebug("Extracted TMDb ID: '{0}' for '{1}'", item.TmdbId, item.Title);
                }
                // Parse TVDb ID (format: tvdb://12345)
                else if (id.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase))
                {
                    item.TvdbId = id.Substring(7); // Remove "tvdb://" prefix
                    _logger.LogDebug("Extracted TVDb ID: '{0}' for '{1}'", item.TvdbId, item.Title);
                }
                else
                {
                    _logger.LogDebug("Unknown Guid format: '{0}' for '{1}'", id, item.Title);
                }
            }
            
            // Log summary of external IDs found
            _logger.LogDebug("External IDs extracted for '{0}': IMDb={1}, TMDb={2}, TVDb={3}", 
                item.Title, item.ImdbId ?? "null", item.TmdbId ?? "null", item.TvdbId ?? "null");
        }
    }
}
