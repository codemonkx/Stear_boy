using Xunit;

// The network tests bind the fixed Gyropad ports and the suite mutates shared
// statics (Games.Current, GYROPAD_CONFIG_DIR), so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
