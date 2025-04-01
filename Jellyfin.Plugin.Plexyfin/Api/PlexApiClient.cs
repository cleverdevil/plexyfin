using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

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
        /// <param name="plexServerUrl">Plex server URL.</param>
        /// <param name="plexApiToken">Plex API token.</param>
        /// <param name="plexItemId">Plex item ID (rating key).</param>
        /// <returns>The watch state of the item.</returns>
        public async Task<PlexWatchState> GetItemWatchStateAsync(string plexServerUrl, string plexApiToken, string plexItemId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{plexServerUrl}/library/metadata/{plexItemId}?X-Plex-Token={plexApiToken}";
                
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var xml = XDocument.Parse(content);
                
                // Parse the XML response to get watch state
                var videoElement = xml.Descendants("Video").FirstOrDefault();
                if (videoElement != null)
                {
                    var viewCount = int.Parse(videoElement.Attribute("viewCount")?.Value ?? "0");
                    var viewOffset = double.Parse(videoElement.Attribute("viewOffset")?.Value ?? "0") / 1000; // Convert milliseconds to seconds
                    
                    return new PlexWatchState
                    {
                        Watched = viewCount > 0,
                        PlaybackPosition = viewOffset
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting watch state from Plex for item {PlexItemId}", plexItemId);
                return null;
            }
        }

        /// <summary>
        /// Updates the watch state of a Plex item.
        /// </summary>
        /// <param name="plexServerUrl">Plex server URL.</param>
        /// <param name="plexApiToken">Plex API token.</param>
        /// <param name="plexItemId">Plex item ID (rating key).</param>
        /// <param name="watched">Whether the item has been watched.</param>
        /// <param name="playbackPosition">Playback position in seconds.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        public async Task<bool> UpdateItemWatchStateAsync(
            string plexServerUrl, 
            string plexApiToken, 
            string plexItemId, 
            bool watched, 
            double playbackPosition)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                string url;
                
                if (watched)
                {
                    // Mark as watched
                    url = $"{plexServerUrl}/:/scrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexApiToken}";
                }
                else
                {
                    // Mark as unwatched
                    url = $"{plexServerUrl}/:/unscrobble?identifier=com.plexapp.plugins.library&key={plexItemId}&X-Plex-Token={plexApiToken}";
                }
                
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                // If there's a playback position to update and the item isn't marked as watched
                if (!watched && playbackPosition > 0)
                {
                    // Convert seconds to milliseconds for Plex API
                    var offsetMs = (int)(playbackPosition * 1000);
                    
                    url = $"{plexServerUrl}/:/progress?identifier=com.plexapp.plugins.library&key={plexItemId}&time={offsetMs}&X-Plex-Token={plexApiToken}";
                    response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating watch state in Plex for item {PlexItemId}", plexItemId);
                return false;
            }
        }
    }
}
