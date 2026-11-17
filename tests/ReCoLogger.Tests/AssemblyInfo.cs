// ReCoLogger has process-wide static state (Log.Init / Log.Store), so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
