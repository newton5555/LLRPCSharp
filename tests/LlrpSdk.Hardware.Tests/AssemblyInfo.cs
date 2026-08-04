using Xunit;

// Real-device tests must run sequentially: tests share one physical reader and
// each deployment deletes/replaces all ROSpecs, so parallel execution would
// corrupt device state between tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
