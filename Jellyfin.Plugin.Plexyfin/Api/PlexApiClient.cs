using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Xml.XPath;

namespace Jellyfin.Plugin.Plexyfin.Api
{
    /// <summary>
    /// Client for interacting with the Plex API.
    /// </summary>
    public class PlexApiClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexApiClient"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger"/>.</param>
        public PlexApiClient(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets the watch state of a Plex item.
        /// </summary>
        /// <param name="plexItemId">The Plex item ID.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task<PlexWatchState?> GetItemWatchStateAsync(string plexItemId, Uri plexServerUrl, string plexToken)
        {
            try
            {
                // For our test build, only log extensive information if we're dealing with "Wicked"
                var isTestMovie = plexItemId.Contains("wicked", StringComparison.OrdinalIgnoreCase);
                
                if (isTestMovie)
                {
                    _logger.LogWarning("==== DIAGNOSTIC: Getting Plex watch state for test movie (ID: {PlexId}) ====", plexItemId);
                }
                
                using var client = _httpClientFactory.CreateClient();
                // Construct URL
                var baseUrl = plexServerUrl.ToString().TrimEnd('/');
                var fullUrl = $"{baseUrl}/library/metadata/{plexItemId}?X-Plex-Token={plexToken}";
                var url = new Uri(fullUrl);
                
                // Log the URL (with token redacted)
                var redactedUrl = fullUrl.Replace(plexToken, "TOKEN-REDACTED");
                
                if (isTestMovie)
                {
                    _logger.LogWarning("Fetching Plex watch state from URL: {Url}", redactedUrl);
                }
                else
                {
                    _logger.LogInformation("Fetching Plex watch state from URL: {Url}", redactedUrl);
                }
                
                // Make the request
                var response = await client.GetAsync(url);
                
                // Log the status code
                if (isTestMovie)
                {
                    _logger.LogWarning("Plex API response status: {StatusCode} {ReasonPhrase}", 
                        (int)response.StatusCode, response.ReasonPhrase);
                }
                else
                {
                    _logger.LogInformation("Plex API response status: {StatusCode} {ReasonPhrase}", 
                        (int)response.StatusCode, response.ReasonPhrase);
                }
                
                response.EnsureSuccessStatusCode();
                
                // Get the content
                var content = await response.Content.ReadAsStringAsync();
                
                // Log a sample of the response
                var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                
                if (isTestMovie)
                {
                    _logger.LogWarning("Plex API response for test movie (first 500 chars):\n{Content}", preview);
                }
                else
                {
                    _logger.LogDebug("Plex API response (first 500 chars):\n{Content}", preview);
                }
                
                var doc = XDocument.Parse(content);
                
                if (isTestMovie)
                {
                    // Log all tags in the document
                    var allTags = doc.Descendants().Select(e => e.Name.LocalName).Distinct().ToList();
                    _logger.LogWarning("XML tags in Plex response: {Tags}", string.Join(", ", allTags));
                    
                    // Also try to get the MediaContainer element and check if it has size attribute
                    var mediaContainer = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "MediaContainer");
                    if (mediaContainer != null)
                    {
                        var sizeAttr = mediaContainer.Attribute("size")?.Value;
                        _logger.LogWarning("MediaContainer size attribute: {Size}", sizeAttr ?? "null");
                        
                        // Log all MediaContainer attributes
                        var mcAttrs = mediaContainer.Attributes().Select(a => $"{a.Name.LocalName}='{a.Value}'");
                        _logger.LogWarning("MediaContainer attributes: {Attrs}", string.Join(", ", mcAttrs));
                    }
                }
                
                // Try to find media info element (could be Video, Episode, Movie, etc.)
                var mediaElement = doc.XPathSelectElements("//Video").FirstOrDefault() ?? 
                                  doc.XPathSelectElements("//Episode").FirstOrDefault() ?? 
                                  doc.XPathSelectElements("//Movie").FirstOrDefault();
                
                if (mediaElement == null)
                {
                    if (isTestMovie)
                    {
                        _logger.LogWarning("TEST MOVIE ERROR: Could not find media element in Plex response for item {PlexItemId}", plexItemId);
                        _logger.LogWarning("Full Plex response content: {Content}", content);
                    }
                    else
                    {
                        _logger.LogWarning("Could not find media element in Plex response for item {PlexItemId}", plexItemId);
                        _logger.LogDebug("Plex response content: {Content}", content);
                    }
                    return null;
                }
                
                // Log all attributes for debugging
                if (isTestMovie)
                {
                    _logger.LogWarning("TEST MOVIE: Found media element type: {ElementType}", mediaElement.Name.LocalName);
                    _logger.LogWarning("TEST MOVIE: Media element attributes: {Attributes}", 
                        string.Join(", ", mediaElement.Attributes().Select(a => $"{a.Name.LocalName}='{a.Value}'")));
                }
                else
                {
                    _logger.LogDebug("Plex item attributes: {Attributes}", 
                        string.Join(", ", mediaElement.Attributes().Select(a => $"{a.Name}={a.Value}")));
                }
                
                var viewCount = mediaElement.Attribute("viewCount")?.Value;
                var viewOffset = mediaElement.Attribute("viewOffset")?.Value;
                
                if (isTestMovie)
                {
                    _logger.LogWarning("TEST MOVIE: Watch state attributes - viewCount='{ViewCount}', viewOffset='{ViewOffset}'", 
                        viewCount ?? "null", viewOffset ?? "null");
                }
                
                var state = new PlexWatchState
                {
                    Watched = !string.IsNullOrEmpty(viewCount) && int.Parse(viewCount, CultureInfo.InvariantCulture) > 0,
                    PlaybackPosition = !string.IsNullOrEmpty(viewOffset) ? double.Parse(viewOffset, CultureInfo.InvariantCulture) / 1000 : 0 // Convert milliseconds to seconds
                };
                
                if (isTestMovie)
                {
                    _logger.LogWarning("TEST MOVIE: Final Plex watch state - Watched={Watched}, Position={Position:F0} seconds", 
                        state.Watched, state.PlaybackPosition);
                }
                
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting watch state for Plex item {PlexItemId}", plexItemId);
                return null;
            }
        }
        
        /// <summary>
        /// Updates the watch state of a Plex item.
        /// </summary>
        /// <param name="plexItemId">The Plex item ID.</param>
        /// <param name="plexServerUrl">The Plex server URL.</param>
        /// <param name="plexToken">The Plex API token.</param>
        /// <param name="watched">Whether the item is watched.</param>
        /// <param name="playbackPosition">The playback position in seconds.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task<bool> UpdateItemWatchStateAsync(string plexItemId, Uri plexServerUrl, string plexToken, bool watched, double playbackPosition = 0)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                Uri url;
                
                if (watched)
                {
                    // Mark as watched - use proper Plex API endpoint
                    url = new Uri(plexServerUrl, $"/:/scrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexToken}");
                    _logger.LogDebug("Marking item as watched in Plex: {PlexItemId}", plexItemId);
                }
                else
                {
                    // Mark as unwatched - use proper Plex API endpoint
                    url = new Uri(plexServerUrl, $"/:/unscrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexToken}");
                    _logger.LogDebug("Marking item as unwatched in Plex: {PlexItemId}", plexItemId);
                }
                
                _logger.LogDebug("Sending request to URL: {Url}", url.ToString().Replace(plexToken, "TOKEN-REDACTED"));
                
                var response = await client.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Plex API error: {StatusCode} {ReasonPhrase} - {Content}", 
                        (int)response.StatusCode, response.ReasonPhrase, errorContent);
                    return false;
                }
                
                // If we need to update the playback position and the item is not marked as watched
                if (!watched && playbackPosition > 0)
                {
                    // Convert seconds to milliseconds for Plex
                    var positionMs = (int)(playbackPosition * 1000);
                    url = new Uri(plexServerUrl, $"/:/progress?identifier=com.plexapp.plugins.library&key={plexItemId}&time={positionMs}&X-Plex-Token={plexToken}");
                    
                    _logger.LogDebug("Updating playback position in Plex: {PlexItemId} to {Position}ms", plexItemId, positionMs);
                    _logger.LogDebug("Sending request to URL: {Url}", url.ToString().Replace(plexToken, "TOKEN-REDACTED"));
                    
                    response = await client.GetAsync(url);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Plex API error while updating position: {StatusCode} {ReasonPhrase} - {Content}", 
                            (int)response.StatusCode, response.ReasonPhrase, errorContent);
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating watch state for Plex item {PlexItemId}", plexItemId);
                return false;
            }
        }
    }
    
    /// <summary>
    /// Represents the watch state of a Plex item.
    /// </summary>
    public class PlexWatchState
    {
        /// <summary>
        /// Gets or sets a value indicating whether the item is watched.
        /// </summary>
        public bool Watched { get; set; }
        
        /// <summary>
        /// Gets or sets the playback position in seconds.
        /// </summary>
        public double PlaybackPosition { get; set; }
    }
}
