# ReCoLogger

A drop-in `Microsoft.Extensions.Logging` provider for .NET apps (console, Avalonia, MAUI, WPF, Android, servers) that gives you:

- **Output to the console, Android logcat and the IDE output window**
- **Per-session log files** named by date and time, keeping only the last N sessions (3 by default)
- **An observable in-memory list** (`ObservableCollection`) for realtime, in-app log screens
- **Class, method and line** in every record, also for `ILogger<T>` calls
- **Dynamic filters applied when you read logs**: level, time range, text, class and ILogger category
  (so you can hide framework spam *without* losing it)
- Scopes (`using (Log.BeginScope(...))`), timing (`using (Log.Measure(...))`), unhandled-exception capture,
  zip export and custom sinks

Targets **.NET 8+** (works on .NET 8, 9, 10 and newer, including `net8.0-android` / `net10.0-android` apps).

## Install

```
dotnet add package ReCoLogger
```

## Quick start (DI)

```csharp
services.AddLogging(builder => builder
    .SetMinimumLevel(LogLevel.Trace)      // see note below
    .AddReCoLogger(o =>
    {
        o.AppName = "MyApp";
        o.UiDispatch = a => Dispatcher.UIThread.Post(a); // Avalonia; MAUI: MainThread.BeginInvokeOnMainThread
    }));
```

Then use `ILogger<T>` exactly like with any other provider:

```csharp
public class OrderService(ILogger<OrderService> logger)
{
    public void Create(int id, string user)
    {
        logger.LogInformation("Order {OrderId} created for {User}", id, user);
        // 2026-11-16 22:20:11.402 [INF] [T05] OrderService.Create:7 @MyApp.OrderService | Order 5 created for Ann
    }
}
```

> **About `SetMinimumLevel`:** the default minimum level of `ILogger` is `Information`, so `Debug`/`Trace` records
> never reach any provider. Set it to `Trace` to capture everything and filter later.

`ILogger<T>` in short: you declare `ILogger<OrderService>` in a constructor and DI injects it. `T` only decides the
*category* (shown as the class name in records). All the `LogInformation`, `LogError`, ... methods are ready-made
extension methods. Use message templates (`"Order {OrderId}"`) instead of `$"..."` interpolation.

> **Call `AddReCoLogger` before anything logs.** `Log.Init` applies options only on the first call; a log call made
> earlier initializes the logger with default options.

## Without DI

```csharp
Log.Init(new LogOptions { AppName = "MyApp" });   // optional: the first log call initializes with defaults

Log.Information("Started");
Log.Warning("Disk is almost full");
Log.Error("Request failed", ex);

using (Log.BeginScope("Order 42"))      // adds {Order 42} to every record inside, also across await
using (Log.Measure("LoadData"))         // logs "LoadData took 123 ms" at the end of the block
{
    ...
}

private static readonly ClassLogger _log = Log.For<MyClass>();   // exact class name instead of the file name
```

Everything written through the static `Log` and through `ILogger` ends up in the same sinks.

## Filtering at query time

Records from `ILogger` carry their full category, so you can decide **per query** what to see:

```csharp
// last 20 records without Microsoft.* / System.* noise (your own ILogger<T> logs stay)
var f = new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories };
IReadOnlyList<string> last20 = LogFiles.ReadLast(20, f);

// only direct Log.* records, nothing from ILogger at all
var onlyDirect = new LogFilter { IncludeILogger = false };

// errors from the last hour that mention "timeout"
var recent = new LogFilter { MinLevel = LogLevel.Error, From = DateTime.Now.AddHours(-1), Text = "timeout" };
```

`LogFiles.ReadLast(n, filter)` applies the filter first, so you always get *n matching* records.
The same `LogFilter` drives the live list. See [docs/FILTERING.md](docs/FILTERING.md).

## Realtime in-app logs

```csharp
public sealed class LogsViewModel(LiveLogView logs)       // LiveLogView is registered in DI
{
    public LiveLogView Logs { get; } = logs;
}
```

```xml
<ListBox ItemsSource="{Binding Logs.Items}">
  <ListBox.ItemTemplate>
    <DataTemplate>
      <TextBlock Text="{Binding Message}" />
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

Change `Logs.Filter.MinLevel`, `Logs.Filter.Text`, `Logs.Filter.ExcludedCategories`, ... from the UI thread and the
list updates itself. The list is fed from `Log.Store`, a ring buffer of the latest records
(`LogOptions.StoreCapacity`, 5000 by default). Set `LogOptions.UiDispatch` so that updates are marshalled to the
UI thread.

## Log files

- Folder: `LocalApplicationData/<AppName>/logs` (override with `LogOptions.Directory`)
- One file per session: `log_2026-11-16_21-53-01.txt`
- At startup only the newest `KeepSessions - 1` old files are kept, plus the new one
- Writing happens on a background thread, so logging never blocks your code

```
LogFiles.GetSessionFiles()                 // all session files, oldest first
LogFiles.ReadLast(100, filter)             // last 100 records (multi-line records count as one)
LogFiles.ReadAll(filter)                   // everything
LogFiles.ExportZip(path)                   // pack all logs into a zip
```

Record format: [docs/FILE_FORMAT.md](docs/FILE_FORMAT.md).

## Options (`LogOptions`)

| Option | Default | Meaning |
|---|---|---|
| `AppName` | `"App"` | Used for the default folder and the logcat tag |
| `Directory` | `LocalApplicationData/AppName/logs` | Log folder |
| `KeepSessions` | `3` | Session files to keep, the current one included |
| `MinLevel` | `Trace` | Records below are dropped by the logger |
| `ToConsole` / `ToDebugOutput` / `ToLogcat` / `ToFile` | `true` | Enable individual outputs |
| `LogcatTag` | `AppName` | Android logcat tag (max 23 chars) |
| `StoreCapacity` | `5000` | Records kept for the live list |
| `UiDispatch` | `null` | Marshals live-list updates to the UI thread |
| `CaptureUnhandledExceptions` | `true` | Log unhandled exceptions and flush before the process dies |
| `CaptureCallerInfo` | `true` | Find method and line of `ILogger` calls via the stack (see below) |

## Platform notes

- **Android logcat.** The package targets plain `net8.0`, so `Android.Util.Log` is bound by reflection at runtime
  and only on Android. Long records are split into 3500-character chunks (logcat truncates at ~4000). `Critical` is
  written as `Error` (`Log.Wtf` can terminate the process on some devices).
- **Caller info for `ILogger`.** `ILogger` does not pass the caller, so ReCoLogger walks the stack to the first frame
  outside the logging plumbing. This costs a few microseconds per record; disable it with
  `CaptureCallerInfo = false` if you log very heavily. Line numbers need PDB files; without them you get
  `Class.Method`. If the JIT inlines your calling method into its caller, the outer method is reported.
  Direct `Log.*` calls use compile-time caller attributes and are always exact.
- **Storing everything.** If you log framework categories at `Debug`/`Trace` (EF Core, HttpClient, ASP.NET), files
  grow quickly. You can still add a hard cut-off with `builder.AddFilter("Microsoft", LogLevel.Warning)`; query-time
  filters then work on top of it.

## Building and testing

```
dotnet build
dotnet test                              # runs on net10.0
dotnet test -p:TestAllTfms=true          # net8.0, net9.0 and net10.0 (needs those runtimes)
dotnet run --project samples/ReCoLogger.Sample
```

Publishing to NuGet: [docs/PUBLISHING.md](docs/PUBLISHING.md).

## License

MIT, see [LICENSE](LICENSE).
