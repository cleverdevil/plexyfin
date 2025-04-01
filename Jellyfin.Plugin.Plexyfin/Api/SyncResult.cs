namespace Jellyfin.Plugin.Plexyfin.Api
{
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
}
