using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plexyfin.Plex
{
    /// <summary>
    /// Client for communicating with the Plex Media Server API.
    /// </summary>
    public class PlexClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly string _apiToken;
        private readonly string _clientIdentifier = "PlexifinJellyfinPlugin";

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexClient"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        /// <param name="baseUrl">The base URL of the Plex Media Server.</param>
        /// <param name="apiToken">The Plex API token.</param>
        public PlexClient(IHttpClientFactory httpClientFactory, ILogger logger, string baseUrl, string apiToken)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Ensure the baseUrl is properly formatted
            if (!string.IsNullOrEmpty(baseUrl))
            {
                // Make sure the URL has a scheme
                if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                    !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    baseUrl = "http://" + baseUrl;
                    _logger.LogInformation("Added http:// scheme to Plex server URL: {Url}", baseUrl);
                }
                
                // Trim trailing slash
                _baseUrl = baseUrl.TrimEnd('/');
            }
            else
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }
            
            _apiToken = apiToken ?? throw new ArgumentNullException(nameof(apiToken));
        }

        /// <summary>
        /// Gets the list of library sections from Plex.
        /// </summary>
        /// <returns>A list of Plex library sections.</returns>
        public async Task<List<PlexLibrarySection>> GetLibrarySections()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                // Ensure baseUrl is valid
                if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri))
                {
                    throw new ArgumentException($"Invalid Plex server URL: {_baseUrl}");
                }
                
                var requestUri = new Uri(baseUri, "/library/sections");
                var requestUrl = requestUri.ToString();

                _logger.LogDebug("Fetching library sections from Plex at {Url}", requestUrl);
                
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                SetPlexHeaders(request);
                
                var response = await client.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogDebug("Received response: {Content}", content.Length > 100 ? 
                    content.Substring(0, 100) + "..." : content);
                
                var sections = new List<PlexLibrarySection>();

                // Parse the XML response from Plex
                var doc = XDocument.Parse(content);
                var container = doc.Root;
                
                if (container == null)
                {
                    _logger.LogWarning("No root element found in Plex response");
                    return sections;
                }
                
                foreach (var directory in container.Elements("Directory"))
                {
                    var plexSection = new PlexLibrarySection
                    {
                        Key = directory.Attribute("key")?.Value ?? string.Empty,
                        Title = directory.Attribute("title")?.Value ?? string.Empty,
                        Type = directory.Attribute("type")?.Value ?? string.Empty
                    };
                    
                    sections.Add(plexSection);
                }
                
                return sections;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Plex library sections from {BaseUrl}", _baseUrl);
                throw;
            }
        }

        /// <summary>
        /// Gets collections from a Plex library section.
        /// </summary>
        /// <param name="sectionId">The library section ID.</param>
        /// <returns>A list of Plex collections.</returns>
        public async Task<List<PlexCollection>> GetCollections(string sectionId)
        {
            using var client = _httpClientFactory.CreateClient();
            // The URL for collections is different - we need to use this specific endpoint
            var requestUrl = $"{_baseUrl}/library/sections/{sectionId}/all?type=18";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            SetPlexHeaders(request);

            _logger.LogDebug("Fetching collections from Plex section {SectionId} with URL {Url}", sectionId, requestUrl);
            
            var response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogDebug("Collection response: {Content}", content.Substring(0, Math.Min(200, content.Length)));
            
            var collections = new List<PlexCollection>();

            try
            {
                // Parse the XML response from Plex
                var doc = XDocument.Parse(content);
                var container = doc.Root;
                
                if (container == null)
                {
                    _logger.LogWarning("No root element found in Plex response");
                    return collections;
                }
                
                foreach (var metadata in container.Elements("Directory"))
                {
                    // Log all attributes for debugging
                    var attributeNames = metadata.Attributes().Select(a => $"{a.Name}={a.Value}");
                    _logger.LogDebug("Collection attributes: {Attributes}", string.Join(", ", attributeNames));
                    
                    // Try several different attribute names that might contain the count
                    var childCount = 0;
                    
                    // Try leafCount first
                    if (int.TryParse(metadata.Attribute("leafCount")?.Value, out var leafCount))
                    {
                        childCount = leafCount;
                    }
                    // Try childCount next
                    else if (int.TryParse(metadata.Attribute("childCount")?.Value, out var count))
                    {
                        childCount = count;
                    }
                    // Try size as a last resort
                    else if (int.TryParse(metadata.Attribute("size")?.Value, out var size))
                    {
                        childCount = size;
                    }
                    
                    // Set minimum of 1 for now, so we process all collections
                    childCount = Math.Max(1, childCount);
                    
                    // Extract the summary/description if available
                    var summary = metadata.Attribute("summary")?.Value ?? string.Empty;
                    
                    // Extract image URLs if available
                    var thumb = metadata.Attribute("thumb")?.Value ?? string.Empty;
                    var art = metadata.Attribute("art")?.Value ?? string.Empty;
                    
                    // Construct full URLs for the images if they're relative paths
                    var thumbUrl = string.Empty;
                    var artUrl = string.Empty;
                    
                    if (!string.IsNullOrEmpty(thumb))
                    {
                        // Handle different URL formats
                        if (thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            // Already a full URL, but make sure it has a token
                            thumbUrl = thumb;
                            if (!thumbUrl.Contains("X-Plex-Token=") && !thumbUrl.Contains("token="))
                            {
                                var separator = thumbUrl.Contains("?") ? "&" : "?";
                                thumbUrl = $"{thumbUrl}{separator}X-Plex-Token={_apiToken}";
                            }
                        }
                        else
                        {
                            // Relative path, construct full URL with token
                            thumbUrl = $"{_baseUrl}{thumb}";
                            var separator = thumbUrl.Contains("?") ? "&" : "?";
                            thumbUrl = $"{thumbUrl}{separator}X-Plex-Token={_apiToken}";
                        }
                        
                        _logger.LogDebug("Constructed thumb URL: {Url}", thumbUrl);
                    }
                    
                    if (!string.IsNullOrEmpty(art))
                    {
                        // Handle different URL formats
                        if (art.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            // Already a full URL, but make sure it has a token
                            artUrl = art;
                            if (!artUrl.Contains("X-Plex-Token=") && !artUrl.Contains("token="))
                            {
                                var separator = artUrl.Contains("?") ? "&" : "?";
                                artUrl = $"{artUrl}{separator}X-Plex-Token={_apiToken}";
                            }
                        }
                        else
                        {
                            // Relative path, construct full URL with token
                            artUrl = $"{_baseUrl}{art}";
                            var separator = artUrl.Contains("?") ? "&" : "?";
                            artUrl = $"{artUrl}{separator}X-Plex-Token={_apiToken}";
                        }
                        
                        _logger.LogDebug("Constructed art URL: {Url}", artUrl);
                    }
                    
                    var plexCollection = new PlexCollection
                    {
                        Key = metadata.Attribute("key")?.Value ?? string.Empty,
                        Title = metadata.Attribute("title")?.Value ?? string.Empty,
                        ItemCount = childCount,
                        Summary = summary,
                        ThumbUrl = thumbUrl,
                        ArtUrl = artUrl
                    };
                    
                    collections.Add(plexCollection);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Plex collections response for section {SectionId}: {Message}", sectionId, ex.Message);
                throw new InvalidOperationException($"Error parsing Plex collections for section {sectionId}", ex);
            }
            
            return collections;
        }

        /// <summary>
        /// Gets the items in a Plex collection.
        /// </summary>
        /// <param name="collectionKey">The collection key.</param>
        /// <returns>A list of Plex items in the collection.</returns>
        public async Task<List<PlexItem>> GetCollectionItems(string collectionKey)
        {
            if (string.IsNullOrEmpty(collectionKey))
            {
                throw new ArgumentNullException(nameof(collectionKey));
            }
            
            using var client = _httpClientFactory.CreateClient();
            var items = new List<PlexItem>();
            
            _logger.LogInformation("Getting collection items for key: {Key}", collectionKey);
            
            // Extract the collection ID from the key
            string collectionId = string.Empty;
            
            // Try to extract the ID from the key using regex
            var match = System.Text.RegularExpressions.Regex.Match(collectionKey, @"/collections/(\d+)");
            if (match.Success)
            {
                collectionId = match.Groups[1].Value;
                _logger.LogInformation("Extracted collection ID: {Id}", collectionId);
            }
            else
            {
                // If we couldn't extract an ID, use the key as is
                collectionId = collectionKey;
            }
            
            // IMPORTANT: We've discovered that there are duplicate collections with the same name
            // but in different libraries. We need to check all libraries to find the correct one.
            var sections = await GetLibrarySections().ConfigureAwait(false);
            
            // First, try to find the collection in each library section
            foreach (var section in sections)
            {
                try
                {
                    _logger.LogInformation("Checking section {SectionTitle} (ID: {SectionId}) for collection", 
                        section.Title, section.Key);
                    
                    // Get all collections in this section
                    var url = $"{_baseUrl}/library/sections/{section.Key}/collections";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    SetPlexHeaders(request);
                    
                    var response = await client.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Failed to get collections from section {SectionId}: {Status}", 
                            section.Key, response.StatusCode);
                        continue;
                    }
                    
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var doc = XDocument.Parse(content);
                    var container = doc.Root;
                    
                    if (container == null)
                    {
                        _logger.LogWarning("No root element found in collections response");
                        continue;
                    }
                    
                    // Look for a collection with matching ID or key
                    var collectionElements = container.Elements("Directory").ToList();
                    _logger.LogInformation("Found {Count} collections in section {SectionTitle}", 
                        collectionElements.Count, section.Title);
                    
                    foreach (var collectionElement in collectionElements)
                    {
                        var key = collectionElement.Attribute("key")?.Value ?? string.Empty;
                        var title = collectionElement.Attribute("title")?.Value ?? string.Empty;
                        var currentRatingKey = collectionElement.Attribute("ratingKey")?.Value ?? string.Empty;
                        var childCount = int.TryParse(collectionElement.Attribute("childCount")?.Value, out var count) ? count : 0;
                        
                        _logger.LogDebug("Collection: {Title}, Key: {Key}, RatingKey: {RatingKey}, ChildCount: {ChildCount}", 
                            title, key, currentRatingKey, childCount);
                        
                        // Check if this collection matches our target
                        if (key.Contains(collectionId) || 
                            (currentRatingKey == collectionId) || 
                            key.EndsWith(collectionKey))
                        {
                            _logger.LogInformation("Found matching collection: {Title} with key {Key}, ChildCount: {ChildCount}", 
                                title, key, childCount);
                            
                            // If the collection has no items according to Plex, return an empty list
                            if (childCount == 0)
                            {
                                _logger.LogInformation("Collection {Title} has no items according to Plex", title);
                                return new List<PlexItem>();
                            }
                            
                            // Try to get the items directly from this element's key
                            var directUrl = $"{_baseUrl}{key}";
                            _logger.LogInformation("Trying direct URL from collection listing: {Url}", directUrl);
                            
                            if (await TryGetCollectionItems(client, directUrl, items).ConfigureAwait(false))
                            {
                                return items;
                            }
                            
                            // If we found a match but couldn't get items, try with the section ID
                            var sectionId = section.Key;
                            _logger.LogInformation("Trying to get collection items using section ID: {SectionId}", sectionId);
                            
                            // Try with the section ID and collection rating key
                            var alternativeUrl = $"{_baseUrl}/library/sections/{sectionId}/collections/{currentRatingKey}";
                            _logger.LogInformation("Trying alternative URL: {Url}", alternativeUrl);
                            
                            if (await TryGetCollectionItems(client, alternativeUrl, items).ConfigureAwait(false))
                            {
                                return items;
                            }
                            
                            // Try with the section ID and collection rating key + children
                            alternativeUrl = $"{_baseUrl}/library/sections/{sectionId}/collections/{currentRatingKey}/children";
                            _logger.LogInformation("Trying alternative URL: {Url}", alternativeUrl);
                            
                            if (await TryGetCollectionItems(client, alternativeUrl, items).ConfigureAwait(false))
                            {
                                return items;
                            }
                            
                            // Try with the section ID and collection rating key + all
                            alternativeUrl = $"{_baseUrl}/library/sections/{sectionId}/collections/{currentRatingKey}/all";
                            _logger.LogInformation("Trying alternative URL: {Url}", alternativeUrl);
                            
                            if (await TryGetCollectionItems(client, alternativeUrl, items).ConfigureAwait(false))
                            {
                                return items;
                            }
                            
                            // Try with the section ID and collection rating key + items
                            alternativeUrl = $"{_baseUrl}/library/sections/{sectionId}/collections/{currentRatingKey}/items";
                            _logger.LogInformation("Trying alternative URL: {Url}", alternativeUrl);
                            
                            if (await TryGetCollectionItems(client, alternativeUrl, items).ConfigureAwait(false))
                            {
                                return items;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking section {SectionId} for collection", section.Key);
                }
            }
            
            // If we still don't have items, try the original URL patterns
            List<string> urlsToTry = new List<string>();
            
            // Format 1: If the key is a full path like "/library/collections/7764/children"
            if (collectionKey.StartsWith("/library/collections/", StringComparison.OrdinalIgnoreCase))
            {
                string directUrl = $"{_baseUrl}{collectionKey}";
                urlsToTry.Add(directUrl);
                
                // Try alternative formats based on the collection ID
                var idMatch = System.Text.RegularExpressions.Regex.Match(collectionKey, @"/collections/(\d+)");
                if (idMatch.Success)
                {
                    var id = idMatch.Groups[1].Value;
                    
                    urlsToTry.Add($"{_baseUrl}/library/collections/{id}/items");
                    urlsToTry.Add($"{_baseUrl}/library/collections/{id}/all");
                    urlsToTry.Add($"{_baseUrl}/library/collections/{id}/children");
                    urlsToTry.Add($"{_baseUrl}/library/collections/{id}/metadata");
                    urlsToTry.Add($"{_baseUrl}/library/metadata/{id}/children");
                }
            }
            // Format 2: Extract ID from key if available
            else if (!string.IsNullOrEmpty(collectionId))
            {
                urlsToTry.Add($"{_baseUrl}/library/collections/{collectionId}/items");
                urlsToTry.Add($"{_baseUrl}/library/collections/{collectionId}/all");
                urlsToTry.Add($"{_baseUrl}/library/collections/{collectionId}/children");
                urlsToTry.Add($"{_baseUrl}/library/collections/{collectionId}/metadata");
                urlsToTry.Add($"{_baseUrl}/library/metadata/{collectionId}/children");
            }
            
            // Try with the raw collection key
            urlsToTry.Add($"{_baseUrl}/library/collections/{collectionKey}/items");
            urlsToTry.Add($"{_baseUrl}/library/collections/{collectionKey}/all");
            urlsToTry.Add($"{_baseUrl}/library/collections/{collectionKey}/children");
            urlsToTry.Add($"{_baseUrl}/library/collections/{collectionKey}/metadata");
            urlsToTry.Add($"{_baseUrl}{collectionKey}");  // Use the key directly
            
            // Try all URLs
            foreach (var url in urlsToTry)
            {
                _logger.LogInformation("Trying URL: {Url}", url);
                if (await TryGetCollectionItems(client, url, items).ConfigureAwait(false))
                {
                    return items;
                }
            }
            
            // If we got here, we weren't able to get the items
            _logger.LogWarning("Failed to get collection items for key: {Key} after trying all URL patterns. Returning empty list.", collectionKey);
            
            // Return an empty list instead of throwing an exception
            return new List<PlexItem>();
        }
        
        private async Task<bool> TryGetCollectionItems(HttpClient client, string url, List<PlexItem> items)
        {
            try
            {
                // Make sure we have the token in the URL
                if (!url.Contains("X-Plex-Token=") && !url.Contains("token="))
                {
                    var separator = url.Contains("?") ? "&" : "?";
                    url = $"{url}{separator}X-Plex-Token={_apiToken}";
                }
                
                _logger.LogDebug("Trying to fetch collection items with URL: {Url}", url);
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                SetPlexHeaders(request);
                
                var response = await client.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get collection items with URL {Url}: {Status}", url, response.StatusCode);
                    return false;
                }
                
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogDebug("Response content length: {Length} bytes", content.Length);
                
                // Try to parse the response
                try
                {
                    var doc = XDocument.Parse(content);
                    var container = doc.Root;
                    
                    if (container == null)
                    {
                        _logger.LogWarning("No root element found in Plex response for URL {Url}", url);
                        return false;
                    }
                    
                    // Try to find videos first
                    var videoElements = container.Elements("Video").ToList();
                    var directoryElements = container.Elements("Directory").ToList();
                    
                    // No media found
                    if (videoElements.Count == 0 && directoryElements.Count == 0)
                    {
                        _logger.LogWarning("No media found in response for URL {Url}. Container has {Count} child elements.", 
                            url, container.Elements().Count());
                        
                        // Log the first few elements to help debug
                        var firstElements = container.Elements().Take(3).Select(e => e.Name.ToString()).ToList();
                        if (firstElements.Any())
                        {
                            _logger.LogWarning("First few element types: {Elements}", string.Join(", ", firstElements));
                        }
                        
                        // Check if there's a MediaContainer/Metadata structure which is another common format
                        var metadataElements = container.Elements("Metadata").ToList();
                        if (metadataElements.Count > 0)
                        {
                            _logger.LogInformation("Found {Count} Metadata elements, processing these instead", metadataElements.Count);
                            
                            foreach (var metadata in metadataElements)
                            {
                                var year = 0;
                                if (int.TryParse(metadata.Attribute("year")?.Value, out var y))
                                {
                                    year = y;
                                }
                                
                                var type = metadata.Attribute("type")?.Value ?? "movie"; // Default to movie if not specified
                                
                                var plexItem = new PlexItem
                                {
                                    Key = metadata.Attribute("key")?.Value ?? string.Empty,
                                    Title = metadata.Attribute("title")?.Value ?? string.Empty,
                                    Type = type,
                                    Year = year,
                                    Guid = metadata.Attribute("guid")?.Value ?? string.Empty
                                };
                                
                                items.Add(plexItem);
                            }
                            
                            _logger.LogInformation("Successfully processed {Count} items from Metadata elements", items.Count);
                            return true;
                        }
                        
                        return false;
                    }
                    
                    // Log success
                    _logger.LogInformation("Successfully found collection items using URL: {Url}", url);
                    _logger.LogInformation("Found {VideoCount} videos and {DirectoryCount} directories", 
                        videoElements.Count, directoryElements.Count);
                    
                    // Process videos (movies)
                    foreach (var metadata in videoElements)
                    {
                        var year = 0;
                        if (int.TryParse(metadata.Attribute("year")?.Value, out var y))
                        {
                            year = y;
                        }
                        
                        var plexItem = new PlexItem
                        {
                            Key = metadata.Attribute("key")?.Value ?? string.Empty,
                            Title = metadata.Attribute("title")?.Value ?? string.Empty,
                            Type = metadata.Attribute("type")?.Value ?? "movie", // Default to movie
                            Year = year,
                            Guid = metadata.Attribute("guid")?.Value ?? string.Empty
                        };
                        
                        items.Add(plexItem);
                    }
                    
                    // Process directories (shows/seasons)
                    foreach (var metadata in directoryElements)
                    {
                        var year = 0;
                        if (int.TryParse(metadata.Attribute("year")?.Value, out var y))
                        {
                            year = y;
                        }
                        
                        var plexItem = new PlexItem
                        {
                            Key = metadata.Attribute("key")?.Value ?? string.Empty,
                            Title = metadata.Attribute("title")?.Value ?? string.Empty,
                            Type = metadata.Attribute("type")?.Value ?? "show", // Default to show
                            Year = year,
                            Guid = metadata.Attribute("guid")?.Value ?? string.Empty
                        };
                        
                        items.Add(plexItem);
                    }
                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing Plex response for URL {Url}", url);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error trying to fetch collection items from URL {Url}", url);
                return false;
            }
        }
        
        private void SetPlexHeaders(HttpRequestMessage request)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Add("X-Plex-Token", _apiToken);
            request.Headers.Add("X-Plex-Client-Identifier", _clientIdentifier);
            request.Headers.Add("X-Plex-Product", "Plexyfin");
            request.Headers.Add("X-Plex-Version", "1.0");
            request.Headers.Add("X-Plex-Device", "Jellyfin");
            request.Headers.Add("X-Plex-Platform", "Jellyfin Plugin");
        }
    }

    /// <summary>
    /// Represents a Plex library section.
    /// </summary>
    public class PlexLibrarySection
    {
        /// <summary>
        /// Gets or sets the section key.
        /// </summary>
        public string Key { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the section title.
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the section type.
        /// </summary>
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a Plex collection.
    /// </summary>
    public class PlexCollection
    {
        /// <summary>
        /// Gets or sets the collection key.
        /// </summary>
        public string Key { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection title.
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the number of items in the collection.
        /// </summary>
        public int ItemCount { get; set; }
        
        /// <summary>
        /// Gets or sets the collection summary/description.
        /// </summary>
        public string Summary { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the URL to the collection thumbnail/poster.
        /// </summary>
        public string ThumbUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the URL to the collection art/backdrop.
        /// </summary>
        public string ArtUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a Plex item.
    /// </summary>
    public class PlexItem
    {
        /// <summary>
        /// Gets or sets the item key.
        /// </summary>
        public string Key { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the item title.
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the item type.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the item year.
        /// </summary>
        public int Year { get; set; }
        
        /// <summary>
        /// Gets or sets the item GUID.
        /// </summary>
        public string Guid { get; set; } = string.Empty;
    }
}