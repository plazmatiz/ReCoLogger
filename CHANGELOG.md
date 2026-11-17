# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0]

### Added
- `ILoggerProvider` (`AddReCoLogger`) so `ILogger<T>` records go to ReCoLogger sinks.
- Static facade `Log` (`Information`, `Warning`, ..., `For<T>()`, `BeginScope`, `Measure`).
- Sinks: console (colored), debug output, Android logcat (reflection-based), per-session file, in-memory store.
- Per-session log files named by date and time, keeping only the last N sessions.
- `LogStore` + `LiveLogView`: observable, filterable list for realtime in-app logs.
- `LogFilter`: level range, time range, text, class, ILogger category prefixes, ILogger on/off.
- `LogFiles`: `ReadLast`, `ReadAll`, `GetSessionFiles`, `ExportZip` with query-time filtering.
- Class, method and line in every record (compile-time for `Log.*`, stack-based for `ILogger`).
- Unhandled and unobserved exception capture.
