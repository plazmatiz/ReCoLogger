using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ReCoLogger.Tests;

/// <summary>Type used as the category in ILogger&lt;DiSample&gt;.</summary>
internal sealed class DiSample { }

public class DependencyInjectionTests
{
    /// <summary>Builds a DI container the way an app would: AddLogging(...AddReCoLogger(...)).</summary>
    sealed class DiFixture : IDisposable
    {
        readonly TempDir _dir = new();

        public string Dir => _dir.Path;
        public ServiceProvider Services { get; }

        public DiFixture()
        {
            Log.Shutdown();

            var services = new ServiceCollection();
            services.AddLogging(b => b
                .SetMinimumLevel(LogLevel.Trace)
                .AddReCoLogger(o =>
                {
                    o.AppName = "Tests";
                    o.Directory = Dir;
                    o.ToConsole = false;
                    o.ToDebugOutput = false;
                    o.ToLogcat = false;
                    o.CaptureUnhandledExceptions = false;
                }));
            Services = services.BuildServiceProvider();
            Log.Store.Clear(); // drop the "Session started" record
        }

        public ILogger<DiSample> Logger => Services.GetRequiredService<ILogger<DiSample>>();

        public void Dispose()
        {
            Services.Dispose();
            Log.Shutdown();
            _dir.Dispose();
        }
    }

    [Fact]
    public void Message_templates_are_resolved_and_the_category_is_kept()
    {
        using var fx = new DiFixture();

        fx.Logger.LogInformation("User {Id} logged in from {City}", 5, "Kyiv");

        var e = Assert.Single(Log.Store.Entries);
        Assert.Equal("User 5 logged in from Kyiv", e.Message);
        Assert.Equal(LogLevel.Information, e.Level);
        Assert.Equal(typeof(DiSample).FullName, e.Category);
        Assert.Equal("DiSample", e.ClassName);
    }

    [Fact]
    public void Exception_and_event_id_are_forwarded()
    {
        using var fx = new DiFixture();
        var ex = new InvalidOperationException("boom");

        fx.Logger.LogError(new EventId(7, "Failure"), ex, "Failed to process {Id}", 3);

        var e = Assert.Single(Log.Store.Entries);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Same(ex, e.Exception);
        Assert.Equal(7, e.EventId.Id);
        Assert.Equal("Failed to process 3", e.Message);
    }

    [Fact]
    public void Caller_method_and_line_are_captured_from_the_stack()
    {
        using var fx = new DiFixture();

        fx.Logger.LogWarning("where am I?");

        var e = Assert.Single(Log.Store.Entries);
        Assert.Equal(nameof(Caller_method_and_line_are_captured_from_the_stack), e.MethodName);
        Assert.True(e.Line > 0);
        Assert.EndsWith("DependencyInjectionTests.cs", e.FilePath);
    }

    [Fact]
    public async Task Async_method_names_are_cleaned_up()
    {
        using var fx = new DiFixture();

        await LogAfterAwait(fx.Logger);

        Assert.Equal(nameof(LogAfterAwait), Assert.Single(Log.Store.Entries).MethodName);
    }

    static async Task LogAfterAwait(ILogger logger)
    {
        await Task.Yield();
        logger.LogInformation("after await");
    }

    [Fact]
    public void ILogger_scopes_show_up_on_records()
    {
        using var fx = new DiFixture();

        using (fx.Logger.BeginScope("Order {Id}", 42))
            fx.Logger.LogInformation("inside");
        fx.Logger.LogInformation("outside");

        Assert.Equal(new string?[] { "Order 42", null }, Log.Store.Entries.Select(e => e.Scope));
    }

    [Fact]
    public void LogStore_and_LiveLogView_can_be_injected()
    {
        using var fx = new DiFixture();

        Assert.Same(Log.Store, fx.Services.GetRequiredService<LogStore>());
        Assert.NotNull(fx.Services.GetRequiredService<LiveLogView>());
    }

    [Fact]
    public void Framework_spam_can_be_hidden_dynamically_in_the_live_list()
    {
        using var fx = new DiFixture();
        var factory = fx.Services.GetRequiredService<ILoggerFactory>();
        factory.CreateLogger("Microsoft.Hosting.Lifetime").LogInformation("framework noise");
        fx.Logger.LogInformation("mine");
        var view = fx.Services.GetRequiredService<LiveLogView>();
        Assert.Equal(2, view.Items.Count);

        view.Filter.ExcludedCategories = LogFilter.FrameworkCategories;
        Assert.Equal("mine", Assert.Single(view.Items).Message);

        view.Filter.ExcludedCategories = [];
        Assert.Equal(2, view.Items.Count);

        view.Filter.IncludeILogger = false;
        Assert.Empty(view.Items);
    }

    [Fact]
    public void Framework_spam_can_be_hidden_dynamically_when_reading_files()
    {
        using var fx = new DiFixture();
        var factory = fx.Services.GetRequiredService<ILoggerFactory>();
        factory.CreateLogger("Microsoft.Hosting.Lifetime").LogInformation("framework noise");
        fx.Logger.LogInformation("mine");
        Log.Flush();

        var filter = new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories };
        var filtered = LogFiles.ReadLast(10, filter, fx.Dir);
        var unfiltered = LogFiles.ReadLast(10, null, fx.Dir);

        Assert.Contains(filtered, r => r.Contains("mine"));
        Assert.DoesNotContain(filtered, r => r.Contains("framework noise"));
        Assert.Contains(unfiltered, r => r.Contains("framework noise"));
    }

    [Fact]
    public void Direct_Log_calls_and_ILogger_land_in_the_same_store()
    {
        using var fx = new DiFixture();

        Log.Information("direct");
        fx.Logger.LogInformation("via ILogger");

        var entries = Log.Store.Entries.ToList();
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].Category);
        Assert.NotNull(entries[1].Category);
    }
}
