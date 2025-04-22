using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plexyfin
{
    /// <summary>
    /// Logger helpers for high-performance structured logging.
    /// </summary>
    public static class LoggerHelpers
    {
        // Plugin.cs log messages
        private static readonly Action<ILogger, string, Exception?> _logPluginInitializing =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(1, nameof(_logPluginInitializing)), 
                "{PluginName} plugin initializing");

        private static readonly Action<ILogger, string, Exception?> _logRegisteringComponents =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(2, nameof(_logRegisteringComponents)), 
                "Registering {PluginName} components with Jellyfin DI system");

        private static readonly Action<ILogger, string, Exception?> _logSuccessfullyRegistered =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(3, nameof(_logSuccessfullyRegistered)), 
                "Successfully registered {PluginName} components");

        private static readonly Action<ILogger, Exception> _logErrorRegistering =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(4, nameof(_logErrorRegistering)), 
                "Error registering plugin components");

        // PlexyfinScheduledTask log messages
        private static readonly Action<ILogger, Exception?> _logSkippedNotEnabled =
            LoggerMessage.Define(
                LogLevel.Information, 
                new EventId(10, nameof(_logSkippedNotEnabled)), 
                "Scheduled sync skipped because collections sync is not enabled");

        private static readonly Action<ILogger, Exception?> _logMissingPlexConfig =
            LoggerMessage.Define(
                LogLevel.Warning, 
                new EventId(11, nameof(_logMissingPlexConfig)), 
                "Scheduled sync skipped because Plex server URL or API token are not configured");

        private static readonly Action<ILogger, Exception?> _logStartingScheduledSync =
            LoggerMessage.Define(
                LogLevel.Information, 
                new EventId(12, nameof(_logStartingScheduledSync)), 
                "Starting scheduled sync from Plex");

        private static readonly Action<ILogger, Exception?> _logCreatingController =
            LoggerMessage.Define(
                LogLevel.Information, 
                new EventId(13, nameof(_logCreatingController)), 
                "Creating controller with provider manager and file system dependencies");

        private static readonly Action<ILogger, Exception?> _logSyncCompleted =
            LoggerMessage.Define(
                LogLevel.Information, 
                new EventId(14, nameof(_logSyncCompleted)), 
                "Scheduled sync completed successfully");

        private static readonly Action<ILogger, Exception> _logSyncError =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(15, nameof(_logSyncError)), 
                "Error during scheduled sync from Plex");

        // PlexClient log messages
        private static readonly Action<ILogger, Exception> _logErrorGettingLibraries =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(20, nameof(_logErrorGettingLibraries)), 
                "Error getting libraries from Plex");

        private static readonly Action<ILogger, string, Exception> _logErrorGettingCollections =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(21, nameof(_logErrorGettingCollections)), 
                "Error getting collections from Plex library {LibraryId}");

        private static readonly Action<ILogger, string, int, Exception?> _logLibraryItems =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug, 
                new EventId(22, nameof(_logLibraryItems)), 
                "Found {Count} items in Plex library {LibraryId}");

        private static readonly Action<ILogger, string, Exception> _logErrorGettingLibraryItems =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(23, nameof(_logErrorGettingLibraryItems)), 
                "Error getting items from Plex library {LibraryId}");
                
        private static readonly Action<ILogger, int, string, Exception?> _logCollectionUrlPatterns =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug, 
                new EventId(24, nameof(_logCollectionUrlPatterns)), 
                "Trying all {MaxAttempts} URL patterns for collection {CollectionId}");
                
        private static readonly Action<ILogger, int, int, string, Exception?> _logTryingUrlPattern =
            LoggerMessage.Define<int, int, string>(
                LogLevel.Debug, 
                new EventId(25, nameof(_logTryingUrlPattern)), 
                "Trying URL pattern {AttemptNumber}/{MaxAttempts}: {Url}");
                
        private static readonly Action<ILogger, int, int, Exception?> _logSuccessfulUrlPattern =
            LoggerMessage.Define<int, int>(
                LogLevel.Debug, 
                new EventId(26, nameof(_logSuccessfulUrlPattern)), 
                "Successfully retrieved {ItemCount} items using pattern {AttemptNumber}");
                
        private static readonly Action<ILogger, int, int, string, Exception> _logErrorUrlPattern =
            LoggerMessage.Define<int, int, string>(
                LogLevel.Debug, 
                new EventId(27, nameof(_logErrorUrlPattern)), 
                "Error with URL pattern {AttemptNumber}/{MaxAttempts} for collection {CollectionId}");
                
        private static readonly Action<ILogger, int, Exception?> _logFailedUrlPatterns =
            LoggerMessage.Define<int>(
                LogLevel.Error, 
                new EventId(28, nameof(_logFailedUrlPatterns)), 
                "Failed to retrieve collection items after trying {MaxAttempts} URL patterns");
                
        private static readonly Action<ILogger, string, Exception> _logErrorGettingCollectionItems =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(29, nameof(_logErrorGettingCollectionItems)), 
                "Error getting items from Plex collection {CollectionId}");

        // PlexyfinController log messages
        private static readonly Action<ILogger, string, string, Exception?> _logProcessingArtwork =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug, 
                new EventId(30, nameof(_logProcessingArtwork)), 
                "Processing artwork for {ItemType}: {ItemName}");

        private static readonly Action<ILogger, string, Exception?> _logCannotProcessArtwork =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(31, nameof(_logCannotProcessArtwork)), 
                "Cannot process artwork for null {ItemType}");

        private static readonly Action<ILogger, string, string, Exception?> _logDownloadingThumbnail =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug, 
                new EventId(32, nameof(_logDownloadingThumbnail)), 
                "Downloading thumbnail for {ItemType}: {ThumbUrl}");

        private static readonly Action<ILogger, string, string, Exception?> _logSavedThumbnail =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug, 
                new EventId(33, nameof(_logSavedThumbnail)), 
                "Successfully saved thumbnail for {ItemType}: {ItemName}");

        private static readonly Action<ILogger, string, string, Exception?> _logProviderManagerNull =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning, 
                new EventId(34, nameof(_logProviderManagerNull)), 
                "Provider manager is null, cannot save thumbnail for {ItemType}: {ItemName}");

        private static readonly Action<ILogger, string, string, Exception> _logErrorProcessingArtwork =
            LoggerMessage.Define<string, string>(
                LogLevel.Error, 
                new EventId(35, nameof(_logErrorProcessingArtwork)), 
                "Error processing artwork for {ItemType} {ItemName}");

        // Additional PlexyfinController log messages
        private static readonly Action<ILogger, string, Exception?> _logSyncMode =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(40, nameof(_logSyncMode)), 
                "{Mode} sync from Plex");

        private static readonly Action<ILogger, string, Exception?> _logDryRun =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(41, nameof(_logDryRun)), 
                "Starting DRY RUN sync - {Message}");

        private static readonly Action<ILogger, int, Exception?> _logItemArtworkSyncCompleted =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(42, nameof(_logItemArtworkSyncCompleted)), 
                "Item artwork sync completed. Updated {ItemCount} items.");
                
        private static readonly Action<ILogger, int, Exception?> _logItemArtworkSimulationCompleted =
            LoggerMessage.Define<int>(
                LogLevel.Information, 
                new EventId(43, nameof(_logItemArtworkSimulationCompleted)), 
                "Item artwork sync simulation completed. Would update {ItemCount} items.");

        private static readonly Action<ILogger, int, Exception?> _logRemovedExistingCollections =
            LoggerMessage.Define<int>(
                LogLevel.Information, 
                new EventId(44, nameof(_logRemovedExistingCollections)), 
                "Removed {Count} existing collections (emptied and made invisible)");

        private static readonly Action<ILogger, int, Exception?> _logWouldRemoveExistingCollections =
            LoggerMessage.Define<int>(
                LogLevel.Information, 
                new EventId(45, nameof(_logWouldRemoveExistingCollections)), 
                "Would remove {Count} existing collections (empty and make invisible)");

        private static readonly Action<ILogger, string, Exception?> _logCollectionSync =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(46, nameof(_logCollectionSync)), 
                "{Mode} collection sync from Plex");

        private static readonly Action<ILogger, string, Exception?> _logCollectionStatus =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(47, nameof(_logCollectionStatus)), 
                "Collection sync {Status}. Would add {Added} collections, would update {Updated} collections");

        private static readonly Action<ILogger, string, int, int, int, Exception?> _logCollectionArtworkStatus =
            LoggerMessage.Define<string, int, int, int>(
                LogLevel.Information, 
                new EventId(48, nameof(_logCollectionArtworkStatus)), 
                "Collection sync {Status}. Would add {Added} collections, would update {Updated} collections, would update artwork for {ArtworkUpdated} items");

        private static readonly Action<ILogger, Guid, Exception?> _logCollectionInvisible =
            LoggerMessage.Define<Guid>(
                LogLevel.Debug, 
                new EventId(49, nameof(_logCollectionInvisible)), 
                "Made collection invisible: {Id}");

        private static readonly Action<ILogger, string, Exception> _logErrorCollection =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(50, nameof(_logErrorCollection)), 
                "Error processing collection: {Name}");

        private static readonly Action<ILogger, string, Exception?> _logProcessingLibrary =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(51, nameof(_logProcessingLibrary)), 
                "Processing library ID: {LibraryId}");

        private static readonly Action<ILogger, string, int, Exception?> _logFoundCollections =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug, 
                new EventId(52, nameof(_logFoundCollections)), 
                "Found {Count} collections in Plex library {LibraryId}");

        private static readonly Action<ILogger, string, Exception?> _logProcessingCollection =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(53, nameof(_logProcessingCollection)), 
                "Processing collection: {Title}");

        private static readonly Action<ILogger, int, Exception?> _logCollectionItems =
            LoggerMessage.Define<int>(
                LogLevel.Debug, 
                new EventId(54, nameof(_logCollectionItems)), 
                "Found {Count} items in Plex collection");

        private static readonly Action<ILogger, string, Guid, Exception?> _logMatchedItem =
            LoggerMessage.Define<string, Guid>(
                LogLevel.Debug, 
                new EventId(55, nameof(_logMatchedItem)), 
                "Matched Plex item '{ItemTitle}' to Jellyfin item with ID: {ItemId}");

        private static readonly Action<ILogger, string, Exception?> _logNoMatchItem =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(56, nameof(_logNoMatchItem)), 
                "Could not find matching Jellyfin item for Plex item: {ItemTitle}");

        private static readonly Action<ILogger, string, Exception?> _logNewCollection =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(57, nameof(_logNewCollection)), 
                "{Action} new collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception?> _logExistingCollectionConflict =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(58, nameof(_logExistingCollectionConflict)), 
                "Found existing collection with name {Title}, making it invisible first");

        private static readonly Action<ILogger, string, Exception?> _logCreatedCollection =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(59, nameof(_logCreatedCollection)), 
                "Created collection with ID: {CollectionId}");

        private static readonly Action<ILogger, string, Exception?> _logSuccessfullyCreatedCollection =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(60, nameof(_logSuccessfullyCreatedCollection)), 
                "Successfully created collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception?> _logUnableToRetrieveCollection =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(61, nameof(_logUnableToRetrieveCollection)), 
                "Unable to retrieve newly created collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception> _logErrorCreatingCollection =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(62, nameof(_logErrorCreatingCollection)), 
                "Error creating collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception?> _logUpdateCollection =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(63, nameof(_logUpdateCollection)), 
                "{Action} existing collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception?> _logSuccessfullyUpdatedCollection =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(64, nameof(_logSuccessfullyUpdatedCollection)), 
                "Successfully updated collection: {CollectionTitle}");

        private static readonly Action<ILogger, string, Exception> _logErrorUpdatingCollection =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(65, nameof(_logErrorUpdatingCollection)), 
                "Error updating collection: {CollectionTitle}");

        private static readonly Action<ILogger, Exception> _logErrorSyncingCollections =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(66, nameof(_logErrorSyncingCollections)), 
                "Error syncing collections");

        private static readonly Action<ILogger, string, Exception?> _logDownloadingArt =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(67, nameof(_logDownloadingArt)), 
                "Downloading art: {ArtUrl}");

        private static readonly Action<ILogger, string, Exception?> _logMimeTypeFromResponse =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(68, nameof(_logMimeTypeFromResponse)), 
                "Using MIME type from response: {MimeType}");

        private static readonly Action<ILogger, string, Exception?> _logMimeTypeFromUrl =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(69, nameof(_logMimeTypeFromUrl)), 
                "Using MIME type from URL: {MimeType}");

        private static readonly Action<ILogger, string, Exception> _logIoErrorSavingImage =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(70, nameof(_logIoErrorSavingImage)), 
                "I/O error saving primary image for {ItemType}, will retry after delay");

        private static readonly Action<ILogger, string, Exception> _logIoErrorSavingBackdrop =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(71, nameof(_logIoErrorSavingBackdrop)), 
                "I/O error saving backdrop image for {ItemType}, will retry after delay");

        private static readonly Action<ILogger, string, Exception?> _logSavedArtwork =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(72, nameof(_logSavedArtwork)), 
                "Successfully saved art for {ItemType}");

        private static readonly Action<ILogger, string, Exception?> _logCannotSaveBackdrop =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(73, nameof(_logCannotSaveBackdrop)), 
                "Provider manager is null, cannot save backdrop for {ItemType}");

        private static readonly Action<ILogger, string, Exception?> _logUpdatedRepoWithImages =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(74, nameof(_logUpdatedRepoWithImages)), 
                "Updated repository with new images for {ItemType}");

        private static readonly Action<ILogger, string, string, Exception?> _logDownloadingItemArt =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug, 
                new EventId(75, nameof(_logDownloadingItemArt)), 
                "Downloading art for item: {ItemType}");

        private static readonly Action<ILogger, string, Exception> _logErrorDeterminingMimeType =
            LoggerMessage.Define<string>(
                LogLevel.Warning, 
                new EventId(76, nameof(_logErrorDeterminingMimeType)), 
                "Error determining MIME type from URL: {Url}");

        private static readonly Action<ILogger, string, Exception> _logErrorGettingLibraryName =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(77, nameof(_logErrorGettingLibraryName)), 
                "Error getting library name for item {ItemName}");

        private static readonly Action<ILogger, Exception?> _logNoLibrariesSelected =
            LoggerMessage.Define(
                LogLevel.Warning, 
                new EventId(78, nameof(_logNoLibrariesSelected)), 
                "No libraries selected for item artwork sync");

        private static readonly Action<ILogger, string, Exception?> _logProcessingLibraryArtwork =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(79, nameof(_logProcessingLibraryArtwork)), 
                "Processing library ID: {LibraryId} for item artwork");

        private static readonly Action<ILogger, int, string, Exception?> _logFoundPlexItems =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug, 
                new EventId(80, nameof(_logFoundPlexItems)), 
                "Found {Count} items in Plex library {LibraryId}");

        private static readonly Action<ILogger, string, Exception?> _logProcessingItemArtwork =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(81, nameof(_logProcessingItemArtwork)), 
                "Processing item artwork: {Title}");

        private static readonly Action<ILogger, string, string, int, Exception?> _logLibraryArtworkComplete =
            LoggerMessage.Define<string, string, int>(
                LogLevel.Information, 
                new EventId(82, nameof(_logLibraryArtworkComplete)), 
                "Completed artwork sync for library {LibraryId} with {UpdatedCount} items updated");

        private static readonly Action<ILogger, Exception> _logErrorSyncingItemArtwork =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(83, nameof(_logErrorSyncingItemArtwork)), 
                "Error syncing item artwork from Plex");

        private static readonly Action<ILogger, string, Exception?> _logEstimatingArtworkUpdates =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(84, nameof(_logEstimatingArtworkUpdates)), 
                "Estimating artwork updates for library ID: {LibraryId}");

        private static readonly Action<ILogger, string, int, int, double, Exception?> _logEstimatedArtworkUpdates =
            LoggerMessage.Define<string, int, int, double>(
                LogLevel.Debug, 
                new EventId(85, nameof(_logEstimatedArtworkUpdates)), 
                "Library {LibraryId}: Estimated {EstimatedMatches} items would have artwork updated (sample size: {SampleSize}, match rate: {MatchRate:P0})");

        private static readonly Action<ILogger, string, Exception> _logErrorEstimatingLibraryArtwork =
            LoggerMessage.Define<string>(
                LogLevel.Error, 
                new EventId(86, nameof(_logErrorEstimatingLibraryArtwork)), 
                "Error estimating artwork updates for library {LibraryId}");

        private static readonly Action<ILogger, Exception> _logErrorEstimatingItemArtwork =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(87, nameof(_logErrorEstimatingItemArtwork)), 
                "Error estimating item artwork updates");

        private static readonly Action<ILogger, string, Exception?> _logStartingBackgroundSync =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(88, nameof(_logStartingBackgroundSync)), 
                "Starting background sync operation with ID: {SyncId}");

        private static readonly Action<ILogger, Exception> _logErrorBackgroundSync =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(89, nameof(_logErrorBackgroundSync)), 
                "Error in background sync operation");

        private static readonly Action<ILogger, Exception> _logErrorStartingSync =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(90, nameof(_logErrorStartingSync)), 
                "Error starting sync from Plex");

        private static readonly Action<ILogger, Exception> _logErrorGettingSyncStatus =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(91, nameof(_logErrorGettingSyncStatus)), 
                "Error getting sync status");

        private static readonly Action<ILogger, Exception> _logErrorTestingPlexConnection =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(92, nameof(_logErrorTestingPlexConnection)), 
                "Error testing Plex connection");

        private static readonly Action<ILogger, string, Exception?> _logStartingBackgroundDryRun =
            LoggerMessage.Define<string>(
                LogLevel.Information, 
                new EventId(93, nameof(_logStartingBackgroundDryRun)), 
                "Starting background dry run with ID: {SyncId}");

        private static readonly Action<ILogger, int, int, int, Exception?> _logDryRunCompleted =
            LoggerMessage.Define<int, int, int>(
                LogLevel.Information, 
                new EventId(94, nameof(_logDryRunCompleted)), 
                "Dry run completed with {TotalChanges} total changes: {AddCount} collections to add, {UpdateCount} to update");

        private static readonly Action<ILogger, Exception> _logErrorBackgroundDryRun =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(95, nameof(_logErrorBackgroundDryRun)), 
                "Error in background dry run operation");

        private static readonly Action<ILogger, Exception> _logErrorStartingDryRun =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(96, nameof(_logErrorStartingDryRun)), 
                "Error starting dry run");

        private static readonly Action<ILogger, string, Exception?> _logReceivedLibraryIDs =
            LoggerMessage.Define<string>(
                LogLevel.Debug, 
                new EventId(97, nameof(_logReceivedLibraryIDs)), 
                "Received library IDs: {LibraryIds}");

        private static readonly Action<ILogger, Exception> _logErrorUpdatingLibraries =
            LoggerMessage.Define(
                LogLevel.Error, 
                new EventId(98, nameof(_logErrorUpdatingLibraries)), 
                "Error updating selected libraries");
                
        private static readonly Action<ILogger, Guid, Exception> _logErrorSettingVisibility =
            LoggerMessage.Define<Guid>(
                LogLevel.Warning, 
                new EventId(99, nameof(_logErrorSettingVisibility)), 
                "Error setting visibility properties on collection {Id}");

        // Public extension methods for Plugin class
        public static void LogPluginInitializing(this ILogger logger, string pluginName) =>
            _logPluginInitializing(logger, pluginName, null);

        public static void LogRegisteringComponents(this ILogger logger, string pluginName) =>
            _logRegisteringComponents(logger, pluginName, null);

        public static void LogSuccessfullyRegistered(this ILogger logger, string pluginName) =>
            _logSuccessfullyRegistered(logger, pluginName, null);

        public static void LogErrorRegistering(this ILogger logger, Exception ex) =>
            _logErrorRegistering(logger, ex);

        // Extension methods for PlexyfinScheduledTask
        public static void LogSkippedNotEnabled(this ILogger logger) =>
            _logSkippedNotEnabled(logger, null);

        public static void LogMissingPlexConfig(this ILogger logger) =>
            _logMissingPlexConfig(logger, null);

        public static void LogStartingScheduledSync(this ILogger logger) =>
            _logStartingScheduledSync(logger, null);

        public static void LogCreatingController(this ILogger logger) =>
            _logCreatingController(logger, null);

        public static void LogSyncCompleted(this ILogger logger) =>
            _logSyncCompleted(logger, null);

        public static void LogSyncError(this ILogger logger, Exception ex) =>
            _logSyncError(logger, ex);

        // Extension methods for PlexClient
        public static void LogErrorGettingLibraries(this ILogger logger, Exception ex) =>
            _logErrorGettingLibraries(logger, ex);

        public static void LogErrorGettingCollections(this ILogger logger, string libraryId, Exception ex) =>
            _logErrorGettingCollections(logger, libraryId, ex);

        public static void LogLibraryItems(this ILogger logger, string libraryId, int count) =>
            _logLibraryItems(logger, libraryId, count, null);

        public static void LogErrorGettingLibraryItems(this ILogger logger, string libraryId, Exception ex) =>
            _logErrorGettingLibraryItems(logger, libraryId, ex);
            
        public static void LogCollectionUrlPatterns(this ILogger logger, int maxAttempts, string collectionId) =>
            _logCollectionUrlPatterns(logger, maxAttempts, collectionId, null);
            
        public static void LogTryingUrlPattern(this ILogger logger, int attemptNumber, int maxAttempts, string url) =>
            _logTryingUrlPattern(logger, attemptNumber, maxAttempts, url, null);
            
        public static void LogSuccessfulUrlPattern(this ILogger logger, int itemCount, int attemptNumber) =>
            _logSuccessfulUrlPattern(logger, itemCount, attemptNumber, null);
            
        public static void LogErrorUrlPattern(this ILogger logger, int attemptNumber, int maxAttempts, string collectionId, Exception ex) =>
            _logErrorUrlPattern(logger, attemptNumber, maxAttempts, collectionId, ex);
            
        public static void LogFailedUrlPatterns(this ILogger logger, int maxAttempts) =>
            _logFailedUrlPatterns(logger, maxAttempts, null);
            
        public static void LogErrorGettingCollectionItems(this ILogger logger, string collectionId, Exception ex) =>
            _logErrorGettingCollectionItems(logger, collectionId, ex);
            
        // Extension methods for PlexyfinController
        public static void LogProcessingArtwork(this ILogger logger, string itemType, string itemName) =>
            _logProcessingArtwork(logger, itemType, itemName, null);
            
        public static void LogCannotProcessArtwork(this ILogger logger, string itemType) =>
            _logCannotProcessArtwork(logger, itemType, null);
            
        public static void LogDownloadingThumbnail(this ILogger logger, string itemType, string thumbUrl) =>
            _logDownloadingThumbnail(logger, itemType, thumbUrl, null);
            
        public static void LogSavedThumbnail(this ILogger logger, string itemType, string itemName) =>
            _logSavedThumbnail(logger, itemType, itemName, null);
            
        public static void LogProviderManagerNull(this ILogger logger, string itemType, string itemName) =>
            _logProviderManagerNull(logger, itemType, itemName, null);
            
        public static void LogErrorProcessingArtwork(this ILogger logger, string itemType, string itemName, Exception ex) =>
            _logErrorProcessingArtwork(logger, itemType, itemName, ex);

        // Additional extension methods for PlexyfinController
        public static void LogSyncMode(this ILogger logger, string mode) =>
            _logSyncMode(logger, mode, null);

        public static void LogDryRun(this ILogger logger, string message) =>
            _logDryRun(logger, message, null);

        public static void LogItemArtworkSyncCompleted(this ILogger logger, int itemCount) =>
            _logItemArtworkSyncCompleted(logger, itemCount, null);

        public static void LogItemArtworkSimulationCompleted(this ILogger logger, int itemCount) =>
            _logItemArtworkSimulationCompleted(logger, itemCount, null);

        public static void LogRemovedExistingCollections(this ILogger logger, int count) =>
            _logRemovedExistingCollections(logger, count, null);

        public static void LogWouldRemoveExistingCollections(this ILogger logger, int count) =>
            _logWouldRemoveExistingCollections(logger, count, null);

        public static void LogCollectionSync(this ILogger logger, string mode) =>
            _logCollectionSync(logger, mode, null);

        public static void LogCollectionStatus(this ILogger logger, string status) =>
            _logCollectionStatus(logger, status, null);

        public static void LogCollectionArtworkStatus(this ILogger logger, string status, int added, int updated, int artworkUpdated) =>
            _logCollectionArtworkStatus(logger, status, added, updated, artworkUpdated, null);

        public static void LogCollectionInvisible(this ILogger logger, Guid id) =>
            _logCollectionInvisible(logger, id, null);

        public static void LogErrorCollection(this ILogger logger, string name, Exception ex) =>
            _logErrorCollection(logger, name, ex);

        public static void LogProcessingLibrary(this ILogger logger, string libraryId) =>
            _logProcessingLibrary(logger, libraryId, null);

        public static void LogFoundCollections(this ILogger logger, string libraryId, int count) =>
            _logFoundCollections(logger, libraryId, count, null);

        public static void LogProcessingCollection(this ILogger logger, string title) =>
            _logProcessingCollection(logger, title, null);

        public static void LogCollectionItems(this ILogger logger, int count) =>
            _logCollectionItems(logger, count, null);

        public static void LogMatchedItem(this ILogger logger, string itemTitle, Guid itemId) =>
            _logMatchedItem(logger, itemTitle, itemId, null);

        public static void LogNoMatchItem(this ILogger logger, string itemTitle) =>
            _logNoMatchItem(logger, itemTitle, null);

        public static void LogNewCollection(this ILogger logger, string action, string collectionTitle) =>
            _logNewCollection(logger, string.Format("{0} {1}", action, collectionTitle), null);

        public static void LogExistingCollectionConflict(this ILogger logger, string title) =>
            _logExistingCollectionConflict(logger, title, null);

        public static void LogCreatedCollection(this ILogger logger, string collectionId) =>
            _logCreatedCollection(logger, collectionId, null);

        public static void LogSuccessfullyCreatedCollection(this ILogger logger, string collectionTitle) =>
            _logSuccessfullyCreatedCollection(logger, collectionTitle, null);

        public static void LogUnableToRetrieveCollection(this ILogger logger, string collectionTitle) =>
            _logUnableToRetrieveCollection(logger, collectionTitle, null);

        public static void LogErrorCreatingCollection(this ILogger logger, string collectionTitle, Exception ex) =>
            _logErrorCreatingCollection(logger, collectionTitle, ex);

        public static void LogUpdateCollection(this ILogger logger, string action, string collectionTitle) =>
            _logUpdateCollection(logger, string.Format("{0} {1}", action, collectionTitle), null);

        public static void LogSuccessfullyUpdatedCollection(this ILogger logger, string collectionTitle) =>
            _logSuccessfullyUpdatedCollection(logger, collectionTitle, null);

        public static void LogErrorUpdatingCollection(this ILogger logger, string collectionTitle, Exception ex) =>
            _logErrorUpdatingCollection(logger, collectionTitle, ex);

        public static void LogErrorSyncingCollections(this ILogger logger, Exception ex) =>
            _logErrorSyncingCollections(logger, ex);

        public static void LogDownloadingArt(this ILogger logger, string artUrl) =>
            _logDownloadingArt(logger, artUrl, null);

        public static void LogMimeTypeFromResponse(this ILogger logger, string mimeType) =>
            _logMimeTypeFromResponse(logger, mimeType, null);

        public static void LogMimeTypeFromUrl(this ILogger logger, string mimeType) =>
            _logMimeTypeFromUrl(logger, mimeType, null);

        public static void LogIoErrorSavingImage(this ILogger logger, string itemType, Exception ex) =>
            _logIoErrorSavingImage(logger, itemType, ex);

        public static void LogIoErrorSavingBackdrop(this ILogger logger, string itemType, Exception ex) =>
            _logIoErrorSavingBackdrop(logger, itemType, ex);

        public static void LogSavedArtwork(this ILogger logger, string itemType) =>
            _logSavedArtwork(logger, itemType, null);

        public static void LogCannotSaveBackdrop(this ILogger logger, string itemType) =>
            _logCannotSaveBackdrop(logger, itemType, null);

        public static void LogUpdatedRepoWithImages(this ILogger logger, string itemType) =>
            _logUpdatedRepoWithImages(logger, itemType, null);

        public static void LogDownloadingItemArt(this ILogger logger, string itemType, string artUrl) =>
            _logDownloadingItemArt(logger, itemType, artUrl, null);

        public static void LogErrorDeterminingMimeType(this ILogger logger, string url, Exception ex) =>
            _logErrorDeterminingMimeType(logger, url, ex);

        public static void LogErrorGettingLibraryName(this ILogger logger, string itemName, Exception ex) =>
            _logErrorGettingLibraryName(logger, itemName, ex);

        public static void LogNoLibrariesSelected(this ILogger logger) =>
            _logNoLibrariesSelected(logger, null);

        public static void LogProcessingLibraryArtwork(this ILogger logger, string libraryId) =>
            _logProcessingLibraryArtwork(logger, libraryId, null);

        public static void LogFoundPlexItems(this ILogger logger, int count, string libraryId) =>
            _logFoundPlexItems(logger, count, libraryId, null);

        public static void LogProcessingItemArtwork(this ILogger logger, string title) =>
            _logProcessingItemArtwork(logger, title, null);

        public static void LogLibraryArtworkComplete(this ILogger logger, string libraryId, int updatedCount) =>
            _logLibraryArtworkComplete(logger, libraryId, "items", updatedCount, null);

        public static void LogErrorSyncingItemArtwork(this ILogger logger, Exception ex) =>
            _logErrorSyncingItemArtwork(logger, ex);

        public static void LogEstimatingArtworkUpdates(this ILogger logger, string libraryId) =>
            _logEstimatingArtworkUpdates(logger, libraryId, null);

        public static void LogEstimatedArtworkUpdates(this ILogger logger, string libraryId, int estimatedMatches, int sampleSize, double matchRate) =>
            _logEstimatedArtworkUpdates(logger, libraryId, estimatedMatches, sampleSize, matchRate, null);

        public static void LogErrorEstimatingLibraryArtwork(this ILogger logger, string libraryId, Exception ex) =>
            _logErrorEstimatingLibraryArtwork(logger, libraryId, ex);

        public static void LogErrorEstimatingItemArtwork(this ILogger logger, Exception ex) =>
            _logErrorEstimatingItemArtwork(logger, ex);

        public static void LogStartingBackgroundSync(this ILogger logger, string syncId) =>
            _logStartingBackgroundSync(logger, syncId, null);

        public static void LogErrorBackgroundSync(this ILogger logger, Exception ex) =>
            _logErrorBackgroundSync(logger, ex);

        public static void LogErrorStartingSync(this ILogger logger, Exception ex) =>
            _logErrorStartingSync(logger, ex);

        public static void LogErrorGettingSyncStatus(this ILogger logger, Exception ex) =>
            _logErrorGettingSyncStatus(logger, ex);

        public static void LogErrorTestingPlexConnection(this ILogger logger, Exception ex) =>
            _logErrorTestingPlexConnection(logger, ex);

        public static void LogStartingBackgroundDryRun(this ILogger logger, string syncId) =>
            _logStartingBackgroundDryRun(logger, syncId, null);

        public static void LogDryRunCompleted(this ILogger logger, int totalChanges, int addCount, int updateCount) =>
            _logDryRunCompleted(logger, totalChanges, addCount, updateCount, null);

        public static void LogErrorBackgroundDryRun(this ILogger logger, Exception ex) =>
            _logErrorBackgroundDryRun(logger, ex);

        public static void LogErrorStartingDryRun(this ILogger logger, Exception ex) =>
            _logErrorStartingDryRun(logger, ex);

        public static void LogReceivedLibraryIDs(this ILogger logger, string libraryIds) =>
            _logReceivedLibraryIDs(logger, libraryIds, null);

        public static void LogErrorUpdatingLibraries(this ILogger logger, Exception ex) =>
            _logErrorUpdatingLibraries(logger, ex);
            
        public static void LogErrorSettingVisibility(this ILogger logger, Guid id, Exception ex) =>
            _logErrorSettingVisibility(logger, id, ex);
    }
}