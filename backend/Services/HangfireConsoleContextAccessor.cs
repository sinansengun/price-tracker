using System.Threading;
using Hangfire.Server;

namespace PriceTracker.Services;

public static class HangfireConsoleContextAccessor
{
    private static readonly AsyncLocal<PerformContext?> CurrentContext = new();

    public static PerformContext? Current => CurrentContext.Value;

    public static IDisposable Push(PerformContext? context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(PerformContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
