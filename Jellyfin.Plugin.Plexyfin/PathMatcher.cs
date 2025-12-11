using System;
using System.Linq;

namespace Jellyfin.Plugin.Plexyfin
{
    /// <summary>
    /// Utility class for matching file paths between Plex and Jellyfin.
    /// Handles differences in mount points and path separators.
    /// </summary>
    public static class PathMatcher
    {
        /// <summary>
        /// Determines if two file paths represent the same file, accounting for different mount points.
        /// Compares paths from the end (most specific parts) to handle container path differences.
        /// </summary>
        /// <param name="plexPath">The file path from Plex.</param>
        /// <param name="jellyfinPath">The file path from Jellyfin.</param>
        /// <returns>True if the paths likely represent the same file.</returns>
        public static bool IsMatch(string? plexPath, string? jellyfinPath)
        {
            if (string.IsNullOrEmpty(plexPath) || string.IsNullOrEmpty(jellyfinPath))
            {
                return false;
            }
            
            // Normalize separators and case for comparison
            var plexParts = NormalizePath(plexPath).Split('/');
            var jellyfinParts = NormalizePath(jellyfinPath).Split('/');
            
            var minLength = Math.Min(plexParts.Length, jellyfinParts.Length);
            
            // Require at least filename + parent folder to match (e.g., "MovieName (2023)/MovieName.1080p.mkv")
            if (minLength < 2)
            {
                return false;
            }
            
            // Check if last N parts match (working backwards from filename)
            // We check up to 3 segments: filename, parent folder, and grandparent folder
            for (int i = 1; i <= Math.Min(minLength, 5); i++)
            {
                if (!string.Equals(plexParts[^i], jellyfinParts[^i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Gets the number of matching path segments from the end of two paths.
        /// Used for scoring similarity when multiple candidates exist.
        /// </summary>
        /// <param name="plexPath">The file path from Plex.</param>
        /// <param name="jellyfinPath">The file path from Jellyfin.</param>
        /// <returns>The number of matching segments from the end.</returns>
        public static int GetMatchingSegments(string? plexPath, string? jellyfinPath)
        {
            if (string.IsNullOrEmpty(plexPath) || string.IsNullOrEmpty(jellyfinPath))
            {
                return 0;
            }
            
            var plexParts = NormalizePath(plexPath).Split('/');
            var jellyfinParts = NormalizePath(jellyfinPath).Split('/');
            
            int matches = 0;
            int minLength = Math.Min(plexParts.Length, jellyfinParts.Length);
            
            // Count matching segments from the end
            for (int i = 1; i <= minLength; i++)
            {
                if (string.Equals(plexParts[^i], jellyfinParts[^i], StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                }
                else
                {
                    break; // Stop at first mismatch
                }
            }
            
            return matches;
        }
        
        /// <summary>
        /// Normalizes a file path for comparison by converting separators and lowercasing.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        private static string NormalizePath(string path)
        {
            // Replace backslashes with forward slashes and lowercase for case-insensitive comparison
            return path.Replace('\\', '/').ToLowerInvariant();
        }
    }
}
