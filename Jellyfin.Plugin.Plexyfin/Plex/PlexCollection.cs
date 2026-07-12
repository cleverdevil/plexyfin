using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Plexyfin.Plex
{
    /// <summary>
    /// Represents a Plex collection item.
    /// </summary>
    public class PlexCollection
    {
        /// <summary>
        /// Gets or sets the collection ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection sort title.
        /// </summary>
        [JsonPropertyName("sortTitle")]
        public string SortTitle { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection summary.
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the thumbnail URL.
        /// </summary>
        [JsonPropertyName("thumbUrl")]
        public Uri? ThumbUrl { get; set; }
        
        /// <summary>
        /// Gets or sets the art URL.
        /// </summary>
        [JsonPropertyName("artUrl")]
        public Uri? ArtUrl { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the last time this collection was updated in Plex.
        /// Used to determine whether locally cached artwork is still current, so unchanged
        /// artwork does not need to be re-downloaded on every sync.
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
