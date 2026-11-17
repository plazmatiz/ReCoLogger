# Log file format

Plain UTF-8 text, one **record** per entry. A record starts with a header line; extra lines of the same record
(multi-line messages, exceptions) start with four spaces.

```
2026-11-16 22:20:11.402 [ERR] [T05] OrderService.Fail:24 #7 @MyApp.OrderService {Order 43} | Failed to process order 43
    System.InvalidOperationException: Payment gateway timeout
       at MyApp.OrderService.Fail(Int32 id) in ...
2026-11-16 22:20:11.455 [INF] [T01] Program.<Main>$:30 | Disk space is low
```

## Header

```
yyyy-MM-dd HH:mm:ss.fff [LVL] [Tnn] Class.Method:line [#eventId] [@category] [{scope}] | message
```

| Part | Notes |
|---|---|
| `yyyy-MM-dd HH:mm:ss.fff` | Local time, invariant culture |
| `[LVL]` | `TRC` `DBG` `INF` `WRN` `ERR` `CRT` |
| `[Tnn]` | Managed thread id, at least two digits |
| `Class.Method:line` | `:line` is omitted when unknown. For `ILogger<T>` records `Class` is the short category |
| `#eventId` | Only when the ILogger `EventId.Id` is not 0 |
| `@category` | Only for `ILogger` records: full category, spaces replaced with `_` |
| `{scope}` | Only inside a scope; nested scopes are joined with ` > `; ` | ` inside a scope is written as ` / ` |
| ` | ` | Separates the header from the message |

The header tokens (up to the first ` | `) are separated by single spaces and contain no spaces themselves except inside
`{scope}`, which is why a reader can parse `[#eventId]` and `[@category]` positionally.

## Files

- Name: `log_yyyy-MM-dd_HH-mm-ss.txt`; if two sessions start in the same second the second gets `_1`, `_2`, ...
- Lexicographic order of names is chronological order.
- Only the newest `KeepSessions` files are kept (older ones are deleted when a new session starts).
- Files are opened with `FileShare.ReadWrite | FileShare.Delete`, so you can read the current session's file while
  the app is running.
