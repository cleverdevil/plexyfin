using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Plexyfin.Plex {
    /// <summary>
    /// Represents a TV show episode in Plex.
    /// </summary>
    public class PlexEpisode
    {
        /// <summary>
        /// Gets or sets the episode ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the episode title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the episode index (e.g., 1 for S01E01).
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the season index (e.g., 1 for Season 1).
        /// </summary>
        [JsonPropertyName("parentIndex")]
        public int ParentIndex { get; set; }

        /// <summary>
        /// Gets or sets the parent TV series ID.
        /// </summary>
        [JsonPropertyName("seriesTitle")]
        public string SeriesTitle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parent TV series ID.
        /// </summary>
        [JsonPropertyName("seriesId")]
        public string SeriesId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the thumbnail URL (poster) for the episode.
        /// </summary>
        [JsonPropertyName("thumbUrl")]
        public Uri? ThumbUrl { get; set; }

        /// <summary>
        /// Gets or sets the art URL (backdrop) for the episode.
        /// </summary>
        [JsonPropertyName("artUrl")]
        public Uri? ArtUrl { get; set; }

        /// <summary>
        /// Gets or sets the IMDb ID for the episode.
        /// </summary>
        [JsonPropertyName("imdbId")]
        public string? ImdbId { get; set; }

        /// <summary>
        /// Gets or sets the TMDb ID for the episode.
        /// </summary>
        [JsonPropertyName("tmdbId")]
        public string? TmdbId { get; set; }

        /// <summary>
        /// Gets or sets the TVDb ID for the episode.
        /// </summary>
        [JsonPropertyName("tvdbId")]
        public string? TvdbId { get; set; }
    }
    
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

        /// <summary>
        /// Gets or sets the thumbnail URL (poster).
        /// </summary>
        [JsonPropertyName("thumbUrl")]
        public Uri? ThumbUrl { get; set; }

        /// <summary>
        /// Gets or sets the art URL (backdrop).
        /// </summary>
        [JsonPropertyName("artUrl")]
        public Uri? ArtUrl { get; set; }

        /// <summary>
        /// Gets or sets the IMDb ID.
        /// </summary>
        [JsonPropertyName("imdbId")]
        public string? ImdbId { get; set; }

        /// <summary>
        /// Gets or sets the TMDb ID.
        /// </summary>
        [JsonPropertyName("tmdbId")]
        public string? TmdbId { get; set; }

        /// <summary>
        /// Gets or sets the TVDb ID.
        /// </summary>
        [JsonPropertyName("tvdbId")]
        public string? TvdbId { get; set; }

        /// <summary>
        /// Gets or sets the file path of the media item.
        /// Used for matching specific versions when multiple exist.
        /// </summary>
        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }
    }

    /// <summary>
    /// Represents a TV show season in Plex.
    /// </summary>
    public class PlexSeason
    {
        /// <summary>
        /// Gets or sets the season ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the season title (e.g., "Season 1").
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the season index (0 for Specials, 1 for Season 1, etc.).
        /// </summary>
        [JsonPropertyName("seasonIndex")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the parent TV series ID.
        /// </summary>
        [JsonPropertyName("seriesId")]
        public string SeriesId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parent TV series title.
        /// </summary>
        [JsonPropertyName("seriesTitle")]
        public string SeriesTitle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the thumbnail URL (poster) for the season.
        /// </summary>
        [JsonPropertyName("thumbUrl")]
        public Uri? ThumbUrl { get; set; }

        /// <summary>
        /// Gets or sets the art URL (backdrop) for the season.
        /// </summary>
        [JsonPropertyName("artUrl")]
        public Uri? ArtUrl { get; set; }

        /// <summary>
        /// Gets or sets the TVDb ID inherited from the parent series.
        /// </summary>
        [JsonPropertyName("tvdbId")]
        public string? TvdbId { get; set; }
    }
}
