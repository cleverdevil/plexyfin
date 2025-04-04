using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Plexyfin.Plex
{
    /// <summary>
    /// Represents a Plex library.
    /// </summary>
    public class PlexLibrary
    {
        /// <summary>
        /// Gets or sets the library ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the library title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the library type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether the library is selected for synchronization.
        /// </summary>
        [JsonPropertyName("isSelected")]
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// Represents a Plex item.
    /// </summary>
    public class PlexItem
    {
        /// <summary>
        /// Gets or sets the item ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the item title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the item type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }
}
