# Filtering

ReCoLogger separates **capturing** from **showing**. Capture everything, decide what to show when you look.
One `LogFilter` class is used for both the live list and for logs read from files.

## `LogFilter` properties

| Property | Type | Meaning |
|---|---|---|
| `MinLevel` / `MaxLevel` | `LogLevel` | Inclusive level range (default: Trace..Critical) |
| `From` / `To` | `DateTime?` | Inclusive time range (local time) |
| `Text` | `string?` | Case-insensitive substring of the message or exception text |
| `ClassName` | `string?` | Exact class name (case-insensitive) |
| `ExcludedCategories` | `string[]` | ILogger category **prefixes** to hide, case-insensitive |
| `IncludeILogger` | `bool` | `false` hides every record that came through `ILogger` |

`LogFilter.FrameworkCategories` is a ready-made `["Microsoft", "System"]`.

Every property raises `LogFilter.Changed` when the value changes; `LiveLogView` listens to it and rebuilds itself.

## Categories

For `ILogger<OrderService>` the category is the full type name (`MyApp.Services.OrderService`); for
`factory.CreateLogger("Microsoft.Hosting.Lifetime")` it is that string. Records written through the static `Log`
have **no** category, so `ExcludedCategories` and `IncludeILogger` never hide them.

`ExcludedCategories` is a prefix match: `"Microsoft"` hides `Microsoft.Hosting.Lifetime`,
`Microsoft.EntityFrameworkCore.Database.Command`, ... ; `"System.Net"` hides only the networking ones.

## Recipes

```csharp
// 1. Last 20 records, framework spam hidden (your own ILogger<T> logs stay)
LogFiles.ReadLast(20, new LogFilter { ExcludedCategories = LogFilter.FrameworkCategories });

// 2. Last 20 records from your own Log.* calls only
LogFiles.ReadLast(20, new LogFilter { IncludeILogger = false });

// 3. Everything, including framework logs
LogFiles.ReadLast(20);

// 4. Errors since the app started, mentioning "payment"
LogFiles.ReadAll(new LogFilter { MinLevel = LogLevel.Error, Text = "payment", From = appStartTime });

// 5. Live list: change on the fly
view.Filter.ExcludedCategories = ["Microsoft.EntityFrameworkCore", "System.Net.Http"];
view.Filter.MinLevel = LogLevel.Warning;
```

## Notes

- `LogFiles.ReadLast(n, filter)` applies the filter first, so you get `n` records that pass it.
- For file records, `Text` searches the message and exception part only, not the header (class, category, ...).
- `ClassName` on file records matches the class shown in the record (`Class.Method:line`).
- Time filters compare with millisecond precision (that is what is stored in files).
- A static `AddFilter("Microsoft", LogLevel.Warning)` on the `ILoggingBuilder` is still available when you want a
  *hard* cut-off at capture time (for example to keep files small). It is independent from `LogFilter`.
