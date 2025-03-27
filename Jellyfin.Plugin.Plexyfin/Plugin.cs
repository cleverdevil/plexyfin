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
    public class PlexifinScheduledTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<PlexifinScheduledTask> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexifinScheduledTask"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/>.</param>
        /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/>.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/>.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/>.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{T}"/>.</param>
        public PlexifinScheduledTask(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IHttpClientFactory httpClientFactory,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILogger<PlexifinScheduledTask> logger)
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
        public string Key => "PlexifinSync";

        /// <inheritdoc />
        public string Description => "Syncs collections and playlists from Plex to Jellyfin.";

        /// <inheritdoc />
        public string Category => "Plexyfin";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Check if sync is enabled in config
            var config = Plugin.Instance.Configuration;
            if (!config.SyncCollections && !config.SyncPlaylists)
            {
                _logger.LogInformation("Scheduled sync skipped because neither collections nor playlists are enabled for sync");
                progress.Report(100);
                return;
            }

            // Validate Plex configuration
            if (string.IsNullOrEmpty(config.PlexServerUrl) || string.IsNullOrEmpty(config.PlexApiToken))
            {
                _logger.LogWarning("Scheduled sync skipped because Plex server URL or API token are not configured");
                progress.Report(100);
                return;
            }

            try
            {
                _logger.LogInformation("Starting scheduled sync from Plex");
                progress.Report(10);

                // For now, we'll just log a warning and pass null for the services we can't get
                _logger.LogWarning("IProviderManager and IFileSystem not available to scheduled task, using controller with null dependencies");
                
                // Create a controller instance to reuse the sync logic
                var controller = new PlexifinController(
                    _libraryManager,
                    _collectionManager,
                    _httpClientFactory,
                    null, // providerManager
                    null, // fileSystem
                    (ILogger<PlexifinController>)_logger);

                // Call the sync method
                await controller.SyncFromPlexAsync().ConfigureAwait(false);
                
                _logger.LogInformation("Scheduled sync completed successfully");
                progress.Report(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled sync from Plex");
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
    /// Since we're now directly uploading images to Jellyfin, this class is kept only for backward compatibility.
    /// In this simplified version, we remove the remote image provider functionality since we're using direct API upload.
    /// </summary>
    public class PlexImageProvider : IHasOrder
    {
        private readonly ILogger<PlexImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexImageProvider"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{T}"/>.</param>
        public PlexImageProvider(ILogger<PlexImageProvider> logger)
        {
            _logger = logger;
            _logger.LogInformation("PlexImageProvider initialized - direct API uploads are now used for images");
        }

        /// <inheritdoc />
        public string Name => "Plexyfin";

        /// <inheritdoc />
        public int Order => 1;
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
            _logger.LogInformation("Plexyfin plugin initializing");
            Instance = this;
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; } = null!; // Will be initialized in constructor
        
        // We're intentionally hiding the base class property, because we want to make it accessible to other parts of the plugin
        // This is fine since we're assigning the same value as the base class property

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
            _logger.LogInformation("Registering Plexyfin components with Jellyfin DI system");
            
            try
            {
                // Create and register the image provider (simplified version)
                var imageProvider = new PlexImageProvider(
                    serviceCollection.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger<PlexImageProvider>());
                
                // Register the provider with the IHasOrder interface
                serviceCollection.AddSingleton<IHasOrder>(imageProvider);
                
                // Register the scheduled task
                serviceCollection.AddSingleton<IScheduledTask, PlexifinScheduledTask>();
                
                // IFileSystem should already be registered by Jellyfin's core services
                
                _logger.LogInformation("Successfully registered Plexyfin components");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering Plexyfin components");
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