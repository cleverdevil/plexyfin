using System;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Plexyfin.Api;

namespace Jellyfin.Plugin.Plexyfin
{
    /// <summary>
    /// Logger adapter that converts ILogger&lt;PlexyfinScheduledTask&gt; to ILogger&lt;PlexyfinController&gt;
    /// </summary>
    public class PlexyfinControllerLogger : ILogger<PlexyfinController>
    {
        private readonly ILogger _innerLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexyfinControllerLogger"/> class.
        /// </summary>
        /// <param name="logger">The source logger to adapt.</param>
        public PlexyfinControllerLogger(ILogger logger)
        {
            _innerLogger = logger;
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            return _innerLogger.BeginScope(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return _innerLogger.IsEnabled(logLevel);
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _innerLogger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
