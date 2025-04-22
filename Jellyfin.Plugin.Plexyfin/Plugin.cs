using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Plexyfin.Api;
using Jellyfin.Plugin.Plexyfin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using System.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.IO;
using System.Linq;

namespace Jellyfin.Plugin.Plexyfin
{
    /// <summary>
    /// Task that triggers automatic sync from Plex to Jellyfin.
    /// </summary>
    public class PlexyfinScheduledTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<PlexyfinScheduledTask> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexyfinScheduledTask"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/>.</param>
        /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/>.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{T}"/>.</param>
        public PlexyfinScheduledTask(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IHttpClientFactory httpClientFactory,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILogger<PlexyfinScheduledTask> logger)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _httpClientFactory = httpClientFactory;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Sync from Plex";

        /// <inheritdoc />
        public string Key => "PlexyfinSync";

        /// <inheritdoc />
        public string Description => "Syncs collections from Plex to Jellyfin.";

        /// <inheritdoc />
        public string Category => "Plexyfin";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Validate progress parameter
            if (progress == null)
            {
                throw new ArgumentNullException(nameof(progress));
            }
            
            // Check if sync is enabled in config
            var config = Plugin.Instance.Configuration;
            if (!config.SyncCollections)
            {
                _logger.LogSkippedNotEnabled();
                progress.Report(100);
                return;
            }

            // Validate Plex configuration
            if (config.PlexServerUrl == null || string.IsNullOrEmpty(config.PlexApiToken))
            {
                _logger.LogMissingPlexConfig();
                progress.Report(100);
                return;
            }

            try
            {
                _logger.LogStartingScheduledSync();
                progress.Report(10);

                // We have these services directly in the scheduled task, let's use them
                _logger.LogCreatingController();
                
                // Create a controller instance to reuse the sync logic
                var controllerLogger = new PlexyfinControllerLogger(_logger);
                var controller = new PlexyfinController(
                    _libraryManager,
                    _collectionManager,
                    _httpClientFactory,
                    _providerManager,
                    _fileSystem,
                    controllerLogger);

                // Call the sync method
                await controller.SyncFromPlexAsync().ConfigureAwait(false);
                
                _logger.LogSyncCompleted();
                progress.Report(100);
            }
            catch (Exception ex)
            {
                _logger.LogSyncError(ex);
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Check if scheduled sync is enabled
            if (Plugin.Instance.Configuration.EnableScheduledSync)
            {
                var interval = Plugin.Instance.Configuration.SyncIntervalHours;
                
                // Make sure interval is reasonable
                if (interval < 1)
                {
                    interval = 24; // Default to daily
                }
                
                return new[] 
                {
                    new TaskTriggerInfo
                    {
                        Type = TaskTriggerInfo.TriggerInterval,
                        IntervalTicks = TimeSpan.FromHours(interval).Ticks
                    }
                };
            }
            
            // Return an empty list if scheduled sync is disabled
            return Array.Empty<TaskTriggerInfo>();
        }
    }

    /// <summary>
    /// The main plugin class for Plexyfin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILogger<Plugin> _logger;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
        public Plugin(
            IApplicationPaths applicationPaths, 
            IXmlSerializer xmlSerializer,
            ILoggerFactory loggerFactory)
            : base(applicationPaths, xmlSerializer)
        {
            _logger = loggerFactory.CreateLogger<Plugin>();
            _logger.LogPluginInitializing("Plexyfin");
            Instance = this;
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; } = null!; // Will be initialized in constructor
        
        /// <summary>
        /// Gets or sets the last sync result.
        /// Used for sharing data between API endpoints.
        /// </summary>
        public SyncResult SyncResult { get; set; } = new SyncResult();
        
        // We don't need to override the ApplicationPaths property anymore since we're not using it directly

        /// <inheritdoc />
        public override string Name => "Plexyfin";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("b9f0c474-e9a8-4292-ae41-eb3c1542f4cd");
        
        /// <summary>
        /// Applies configuration to the service collection.
        /// This method is automatically called by the DI system.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        public void ApplyToContainer(IServiceCollection serviceCollection)
        {
            _logger.LogRegisteringComponents("Plexyfin");
            
            try
            {
                // We no longer need the image provider as we're using direct file system access
                
                // Register the scheduled task
                serviceCollection.AddSingleton<IScheduledTask, PlexyfinScheduledTask>();
                
                // IFileSystem should already be registered by Jellyfin's core services
                
                _logger.LogSuccessfullyRegistered("Plexyfin");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogErrorRegistering(ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogErrorRegistering(ex);
            }
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.Configuration.configPage.html",
                        GetType().Namespace)
                }
            };
        }
    }
}