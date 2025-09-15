using System;
using Xunit;
using Jellyfin.Plugin.Plexyfin;

namespace Jellyfin.Plugin.Plexyfin.Tests
{
    public class PathMatcherTests
    {
        [Theory]
        [InlineData("/data/media/movies/Avatar (2009)/Avatar.2009.1080p.mkv", 
                    "/jellyfin/movies/Avatar (2009)/Avatar.2009.1080p.mkv", true)]
        [InlineData("/plex/Movies HD/Avatar (2009)/Avatar.2009.1080p.mkv", 
                    "/media/Movies HD/Avatar (2009)/Avatar.2009.1080p.mkv", true)]
        [InlineData("/data/movies/Avatar (2009)/Avatar.2009.1080p.mkv", 
                    "/media/movies/Avatar (2009)/Avatar.2009.4K.mkv", false)]
        [InlineData("/movies/Avatar (2009)/Avatar.1080p.mkv", 
                    "/movies/Titanic (1997)/Avatar.1080p.mkv", false)]
        [InlineData("C:\\Media\\Movies\\Avatar (2009)\\Avatar.2009.1080p.mkv", 
                    "/media/movies/Avatar (2009)/Avatar.2009.1080p.mkv", true)]
        [InlineData("/very/long/path/to/movies/Avatar (2009)/Avatar.mkv", 
                    "/short/movies/Avatar (2009)/Avatar.mkv", true)]
        public void IsMatch_ShouldCorrectlyIdentifyMatches(string plexPath, string jellyfinPath, bool expectedResult)
        {
            var result = PathMatcher.IsMatch(plexPath, jellyfinPath);
            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("/data/movies/Avatar (2009)/Avatar.2009.1080p.mkv", 
                    "/media/movies/Avatar (2009)/Avatar.2009.1080p.mkv", 3)]
        [InlineData("/plex/Movies/Avatar.mkv", 
                    "/jellyfin/Movies/Avatar.mkv", 2)]
        [InlineData("/data/movies/Avatar.mkv", 
                    "/media/tv/Avatar.mkv", 1)]
        [InlineData("/movies/Avatar.mkv", 
                    "/movies/Titanic.mkv", 0)]
        public void GetMatchingSegments_ShouldReturnCorrectCount(string plexPath, string jellyfinPath, int expectedCount)
        {
            var result = PathMatcher.GetMatchingSegments(plexPath, jellyfinPath);
            Assert.Equal(expectedCount, result);
        }

        [Fact]
        public void IsMatch_ShouldHandleNullPaths()
        {
            Assert.False(PathMatcher.IsMatch(null, "/some/path"));
            Assert.False(PathMatcher.IsMatch("/some/path", null));
            Assert.False(PathMatcher.IsMatch(null, null));
        }

        [Fact]
        public void IsMatch_ShouldHandleEmptyPaths()
        {
            Assert.False(PathMatcher.IsMatch("", "/some/path"));
            Assert.False(PathMatcher.IsMatch("/some/path", ""));
            Assert.False(PathMatcher.IsMatch("", ""));
        }

        [Fact]
        public void GetMatchingSegments_ShouldHandleNullPaths()
        {
            Assert.Equal(0, PathMatcher.GetMatchingSegments(null, "/some/path"));
            Assert.Equal(0, PathMatcher.GetMatchingSegments("/some/path", null));
            Assert.Equal(0, PathMatcher.GetMatchingSegments(null, null));
        }
    }
}