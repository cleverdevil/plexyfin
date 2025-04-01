using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
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
        public async Task<PlexWatchState?> GetItemWatchStateAsync(string plexItemId, string plexServerUrl, string plexToken)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                var url = $"{plexServerUrl}/library/metadata/{plexItemId}?X-Plex-Token={plexToken}";
                
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(content);
                
                // Use XPath to find the viewCount and viewOffset elements
                var videoElement = doc.XPathSelectElements("//Video").FirstOrDefault();
                
                if (videoElement == null)
                {
                    return null;
                }
                
                var viewCount = videoElement.Attribute("viewCount")?.Value;
                var viewOffset = videoElement.Attribute("viewOffset")?.Value;
                
                return new PlexWatchState
                {
                    Watched = !string.IsNullOrEmpty(viewCount) && int.Parse(viewCount) > 0,
                    PlaybackPosition = !string.IsNullOrEmpty(viewOffset) ? double.Parse(viewOffset) / 1000 : 0 // Convert milliseconds to seconds
                };
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
        public async Task<bool> UpdateItemWatchStateAsync(string plexItemId, string plexServerUrl, string plexToken, bool watched, double playbackPosition = 0)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                string url;
                
                if (watched)
                {
                    // Mark as watched
                    url = $"{plexServerUrl}/:/scrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexToken}";
                }
                else
                {
                    // Mark as unwatched
                    url = $"{plexServerUrl}/:/unscrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexToken}";
                }
                
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                // If we need to update the playback position and the item is not marked as watched
                if (!watched && playbackPosition > 0)
                {
                    // Convert seconds to milliseconds for Plex
                    var positionMs = (int)(playbackPosition * 1000);
                    url = $"{plexServerUrl}/:/progress?identifier=com.plexapp.plugins.library&key={plexItemId}&time={positionMs}&X-Plex-Token={plexToken}";
                    
                    response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
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
