using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        /// Gets or sets the number of items with artwork updated.
        /// </summary>
        public int ItemArtworkUpdated { get; set; }
        
        /// <summary>
        /// Gets or sets detailed information about the sync operation.
        /// Only populated in dry run mode.
        /// </summary>
        [JsonPropertyName("details")]
        public DryRunDetails? Details { get; set; }
    }
    
    /// <summary>
    /// Detailed information about sync operations for dry run mode.
    /// </summary>
    public class DryRunDetails
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DryRunDetails"/> class.
        /// </summary>
        public DryRunDetails()
        {
            CollectionsToAdd = new List<SyncCollectionDetail>();
            CollectionsToUpdate = new List<SyncCollectionDetail>();
        }
        
        /// <summary>
        /// Gets or sets the list of collections that would be created.
        /// </summary>
        [JsonPropertyName("collectionsToAdd")]
        public List<SyncCollectionDetail> CollectionsToAdd { get; set; }
        
        /// <summary>
        /// Gets or sets the list of collections that would be updated.
        /// </summary>
        [JsonPropertyName("collectionsToUpdate")]
        public List<SyncCollectionDetail> CollectionsToUpdate { get; set; }
        
    }
    
    /// <summary>
    /// Detailed information about a collection sync operation.
    /// </summary>
    public class SyncCollectionDetail
    {
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
        /// Gets or sets the items that would be added to the collection.
        /// </summary>
        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new List<string>();
    }
    
}
