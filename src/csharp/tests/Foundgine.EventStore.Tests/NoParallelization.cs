using Xunit;

// Each test class here starts its own PostgreSQL Testcontainers container (one per test). Running several classes at once
// starts many containers together, and a container that is not yet reachable fails the test with "Connection refused".
// Serial execution trades a little speed for a reliable gate.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
