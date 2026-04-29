using Hangfire.Console;
using Microsoft.Extensions.Logging;

namespace PriceTracker.Services;

public sealed class HangfireConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new HangfireConsoleLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class HangfireConsoleLogger(string categoryName) : ILogger
    {
        private const string ScraperCategoryPrefix = "PriceTracker.Services.Scrapers.";

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (!categoryName.StartsWith(ScraperCategoryPrefix, StringComparison.Ordinal)) return;

            var context = HangfireConsoleContextAccessor.Current;
            if (context == null) return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception == null) return;

            if (exception != null)
            {
                var exMessage = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
                message = string.IsNullOrWhiteSpace(message) ? exMessage : $"{message} | {exMessage}";
            }

            context.WriteLine($"[{logLevel}] {message}");
        }
    }
}
