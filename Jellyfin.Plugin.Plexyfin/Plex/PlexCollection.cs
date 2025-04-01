namespace Jellyfin.Plugin.Plexyfin.Plex
{
    /// <summary>
    /// Represents a Plex collection.
    /// </summary>
    public class PlexCollection
    {
        /// <summary>
        /// Gets or sets the collection ID.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection title.
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the collection summary.
        /// </summary>
        public string Summary { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the thumbnail URL.
        /// </summary>
        public string ThumbUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the art URL.
        /// </summary>
        public string ArtUrl { get; set; } = string.Empty;
    }
}
