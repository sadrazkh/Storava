using Xunit;

// These tests are serialized, deliberately.
//
// SqliteConnection.ClearAllPools() is process-wide: it closes pooled connections for every
// connection string in the process, not just the caller's. Three classes here reach it — TestHost
// and ScanResumeTests call it directly to release a file they are about to delete, and
// DatabaseGatewayTests drives the real compaction, which clears the pools before VACUUM. Any of
// those running beside a class that is holding a pooled connection can have it closed underneath,
// which showed up as SettingsServiceTests failing roughly one full-solution run in four while
// passing every time it ran alone.
//
// The alternative is a shared collection listing whichever classes happen to open a connection
// today, which is the same constraint written in a place nobody will update. These are database
// tests; the whole assembly runs in single digit seconds either way.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
