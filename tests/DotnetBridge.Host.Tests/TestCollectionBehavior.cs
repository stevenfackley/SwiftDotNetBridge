using Xunit;

// Some hardening tests mutate process-wide statics (BridgeLimits, BridgeDiagnostics).
// Serialize the suite so those mutations can't bleed across concurrently-running tests.
// The suite is tiny and fast, so this costs nothing and removes a whole class of flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
