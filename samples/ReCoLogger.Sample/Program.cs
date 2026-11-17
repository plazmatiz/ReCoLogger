using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReCoLogger;

// 1. Register ReCoLogger like any other ILogger provider.
var services = new ServiceCollection();
services.AddLogging(builder => builder
    .SetMinimumLevel(LogLevel.Trace) // log everything; hide the noise later, when you read the logs
    .AddReCoLogger(o =>
    {
        o.AppName = "ReCoLogger.Sample";
        o.KeepSessions = 3;
        // In a UI app: o.UiDispatch = a => Dispatcher.UIThread.Post(a);
    }));
services.AddTransient<OrderService>();

using var provider = services.BuildServiceProvider();

// 2. Use ILogger<T> as usual.
var orders = provider.GetRequiredService<OrderService>();
orders.Create(42, "Ann");
orders.Fail(43);

// 3. Pretend some framework code is chatty too.
var framework = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Hosting.Lifetime");
framework.LogInformation("Application started. Press Ctrl+C to shut down.");

// 4. The static facade works without DI and writes to the same place.
Log.Warning("Disk space is low");
using (Log.Measure("Slow operation")) Thread.Sleep(50);

// 5. Read logs back with dynamic filters.
Console.WriteLine();
Console.WriteLine("--- last 5 records, framework spam hidden ---");
var noFramework = new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories };
foreach (var line in LogFiles.ReadLast(5, noFramework))
    Console.WriteLine(line);

Console.WriteLine();
Console.WriteLine("--- warnings and errors only ---");
foreach (var line in LogFiles.ReadAll(new LogFilter { MinLevel = LogLevel.Warning }))
    Console.WriteLine(line);

// 6. A live, filterable list for a UI (bind LiveLogView.Items to a ListBox).
var live = provider.GetRequiredService<LiveLogView>();
live.Filter.MinLevel = LogLevel.Error;
Console.WriteLine();
Console.WriteLine($"Live list currently shows {live.Items.Count} error(s) out of {Log.Store.Entries.Count} records.");
Console.WriteLine($"Log files are in: {Log.LogDirectory}");

Log.Shutdown();

internal sealed class OrderService(ILogger<OrderService> logger)
{
    public void Create(int id, string user)
    {
        logger.LogInformation("Order {OrderId} created for {User}", id, user);
    }

    public void Fail(int id)
    {
        try
        {
            throw new InvalidOperationException("Payment gateway timeout");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process order {OrderId}", id);
        }
    }
}
