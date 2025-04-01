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
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the library title.
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the library type.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether the library is selected for synchronization.
        /// </summary>
        public bool IsSelected { get; set; }
    }
}
