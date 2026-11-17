using Xunit;

// Every PostgreSQL test in this assembly starts with PrepareAsync, which TRUNCATEs banking.bank_account,
// banking.transfer_idempotency and banking.transfer_audit on one shared database, and several tests rely on
// deterministic row-lock ordering. Classes running in parallel wipe each other's seeded rows, so the assembly
// runs serially regardless of runner flags (dotnet test, IDE, CI).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
