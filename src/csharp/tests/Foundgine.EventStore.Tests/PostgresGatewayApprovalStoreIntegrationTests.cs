using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.EventStore.Schema;
using Foundgine.Providers.Gateway.Approvals;
using Foundgine.Providers.Outbox;
using Foundgine.Runtime;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Foundgine.EventStore.Tests;

/// <summary>
/// Real-PostgreSQL tests for <see cref="PostgresFoundgineGatewayApprovalStore"/> (Testcontainers, like the other
/// event-store suites). Scenario names mirror the Java suite PostgresFoundgineGatewayApprovalStoreIntegrationTest;
/// a conformance test enforces that every Java scenario has a .NET counterpart.
/// </summary>
public sealed class PostgresGatewayApprovalStoreIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("foundgine").WithUsername("foundgine").WithPassword("foundgine")
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private TestCodec _codec = null!;
    private PostgresFoundgineGatewayApprovalStore _store = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await EventStoreMigrator.ApplyAsync(_dataSource);
        _codec = new TestCodec();
        _store = new PostgresFoundgineGatewayApprovalStore(_dataSource, _codec, enforceDatabaseClock: false);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ------------------------------------------------------------ atomic commit / rollback

    [Fact]
    public async Task StoreWithEvidence_commitsApprovalAndOutboxTogether()
    {
        var digest = Digest("commit");
        var evidence = Evidence("alice", "tenant-a", Now, Now.AddMinutes(10));
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), evidence);

        Assert.Equal(1L, await Count("foundgine_gateway_approvals", $"handle_digest = '{digest}'"));
        Assert.Equal(1L, await Count("foundgine_outbox", "tenant_id = 'tenant-a' AND topic = 'ApprovalRequested'"));
        var key = (string?)await Scalar("SELECT message_key FROM foundgine_outbox");
        var payload = (string?)await Scalar("SELECT payload::text FROM foundgine_outbox");
        Assert.Equal(Guid.ParseExact(evidence.EventId, "N").ToString(), key);
        Assert.NotEqual(digest, key);
        Assert.DoesNotContain(digest, payload);
    }

    [Fact]
    public async Task OutboxFailure_rollsBackApproval()
    {
        var evidence = Evidence("alice", "tenant-a", Now, Now.AddMinutes(10));
        await Exec($"INSERT INTO foundgine_outbox(tenant_id,id,topic,payload,created_at,available_at) VALUES ('tenant-a','{Guid.ParseExact(evidence.EventId, "N")}','x','{{}}'::jsonb,now(),now())");
        var digest = Digest("outbox-fail");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), evidence));

        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.Equal(1L, await Count("foundgine_outbox", "TRUE"));
        Assert.Null(await _store.TakeAsync(digest, "alice", "tenant-a", Now));
    }

    [Fact]
    public async Task ApprovalFailure_leavesNoOutboxRowAndKeepsOriginalApproval()
    {
        var digest = Digest("dup");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), Evidence("alice", "tenant-a", Now, Now.AddMinutes(10)));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(digest,
            Approval("mallory", "tenant-a"), Evidence("mallory", "tenant-a", Now, Now.AddMinutes(10))));

        Assert.Equal(1L, await Count("foundgine_outbox", "TRUE"));
        Assert.Null(await _store.TakeAsync(digest, "mallory", "tenant-a", Now));
        Assert.NotNull(await _store.TakeAsync(digest, "alice", "tenant-a", Now));
    }

    [Fact]
    public async Task CodecEncodeFailure_persistsNothing()
    {
        _codec.FailEncode = true;
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("enc"),
            Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(1))));
        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.Equal(0L, await Count("foundgine_outbox", "TRUE"));
    }

    [Fact]
    public async Task InvalidEvidenceAndInputs_areRejectedBeforeAnyWrite()
    {
        var ok = Evidence("alice", "tenant-a", Now, Now.AddMinutes(1));
        var approval = Approval("alice", "tenant-a");
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("a"), approval, Evidence("bob", "tenant-a", Now, Now.AddMinutes(1))));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("b"), approval, Evidence("alice", "tenant-b", Now, Now.AddMinutes(1))));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("c"), approval, Evidence("alice", "tenant-a", Now, Now)));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("d"), approval, ok with { EventType = "Other" }));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("e"), approval, ok with { PlanFingerprint = "other-fp" }));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync("not-a-digest", approval, ok));
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("f"), approval, ok with { EventId = "not-a-uuid" }));
        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.Equal(0L, await Count("foundgine_outbox", "TRUE"));
    }

    [Fact]
    public async Task StandaloneStoreIsProhibited() =>
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _store.StoreAsync(Digest("x"), Approval("alice", "t"), "alice", "t", Now.AddMinutes(1)));

    [Fact]
    public async Task NonUtcOffsets_areNormalizedInsteadOfRejectedByNpgsql()
    {
        var offset = TimeSpan.FromHours(13);
        var occurred = Now.ToOffset(offset);
        var expires = Now.AddMinutes(10).ToOffset(offset);
        var digest = Digest("offset");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", occurred, expires));
        Assert.Null(await _store.TakeAsync(digest, "alice", "t", Now.AddMinutes(10).ToOffset(offset)));
        Assert.NotNull(await _store.TakeAsync(digest, "alice", "t", Now.AddMinutes(9).ToOffset(offset)));
    }

    // ------------------------------------------------------------ global tenant partition

    [Fact]
    public async Task GlobalApproval_usesReservedOutboxPartitionButKeepsNullTenantInPayload()
    {
        var digest = Digest("global");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", null), Evidence("alice", null, Now, Now.AddMinutes(10)));

        Assert.Equal(1L, await Count("foundgine_outbox", $"tenant_id = '{PostgresFoundgineGatewayApprovalStore.GlobalOutboxPartition}'"));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "tenant_id IS NULL"));
        Assert.Equal(true, await Scalar("SELECT payload->>'Tenant' IS NULL FROM foundgine_outbox"));
        Assert.Null(await _store.TakeAsync(digest, "alice", "", Now));
        Assert.NotNull(await _store.TakeAsync(digest, "alice", null, Now));
    }

    [Fact]
    public async Task ReservedPartitionIdCannotBeUsedAsARealTenant()
    {
        var reserved = PostgresFoundgineGatewayApprovalStore.GlobalOutboxPartition;
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.StoreWithEvidenceAsync(Digest("res"),
            Approval("alice", reserved), Evidence("alice", reserved, Now, Now.AddMinutes(1))));
        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.Equal(0L, await Count("foundgine_outbox", "TRUE"));
    }

    [Fact]
    public async Task CommittedEvidenceIsLeasableByTheRealOutboxDispatcherPath()
    {
        var evidence = Evidence("alice", "tenant-a", Now, Now.AddMinutes(10));
        await _store.StoreWithEvidenceAsync(Digest("lease"), Approval("alice", "tenant-a"), evidence);
        await _store.StoreWithEvidenceAsync(Digest("lease-g"), Approval("alice", null), Evidence("alice", null, Now, Now.AddMinutes(10)));

        var leases = await new PostgresOutbox(_dataSource).LeaseAsync(10, TimeSpan.FromSeconds(30), Now.AddSeconds(1));
        Assert.Equal(2, leases.Count);
        Assert.All(leases, l => Assert.Equal("ApprovalRequested", l.Message.Topic));
        Assert.Equal(Guid.ParseExact(evidence.EventId, "N"), leases.Single(l => l.Message.TenantId == "tenant-a").Message.Id);
    }

    // ------------------------------------------------------------ one-time consumption

    [Fact]
    public async Task ConcurrentTake_allowsExactlyOneConsumer()
    {
        var digest = Digest("race");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), Evidence("alice", "tenant-a", Now, Now.AddMinutes(10)));
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(async () => await _store.TakeAsync(digest, "alice", "tenant-a", Now))));
        Assert.Single(results, r => r is not null);
        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
    }

    [Fact]
    public async Task TakeAfterRestart_usesANewlyConstructedStore()
    {
        var digest = Digest("restart");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), Evidence("alice", "tenant-a", Now, Now.AddMinutes(10)));

        var restarted = new PostgresFoundgineGatewayApprovalStore(_dataSource, new TestCodec(), enforceDatabaseClock: false);
        var taken = await restarted.TakeAsync(digest, "alice", "tenant-a", Now);
        Assert.NotNull(taken);
        Assert.Equal("approval-alice", taken!.ApprovalId);
        Assert.Equal("alice", taken.Request.Security!.Subject);
        Assert.Equal("tenant-a", taken.Request.Security.Tenant);
        Assert.Null(await restarted.TakeAsync(digest, "alice", "tenant-a", Now));
        Assert.Null(await _store.TakeAsync(digest, "alice", "tenant-a", Now));
    }

    [Fact]
    public async Task ConcurrentTakeAcrossIndependentStoreInstances_allowsExactlyOneConsumer()
    {
        var digest = Digest("race-multi");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", null), Evidence("alice", null, Now, Now.AddMinutes(10)));
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
            await new PostgresFoundgineGatewayApprovalStore(_dataSource, new TestCodec(), enforceDatabaseClock: false).TakeAsync(digest, "alice", null, Now))));
        Assert.Single(results, r => r is not null);
    }

    // ------------------------------------------------------------ isolation

    [Fact]
    public async Task SubjectAndTenantIsolation_wrongCallersNeitherSeeNorConsume()
    {
        var digest = Digest("iso");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "tenant-a"), Evidence("alice", "tenant-a", Now, Now.AddMinutes(10)));

        Assert.Null(await _store.TakeAsync(digest, "bob", "tenant-a", Now));
        Assert.Null(await _store.TakeAsync(digest, "alice", "tenant-b", Now));
        Assert.Null(await _store.TakeAsync(digest, "alice", null, Now));
        Assert.Null(await _store.TakeAsync(digest, "ALICE", "tenant-a", Now));
        Assert.False(await _store.RevokeAsync(digest, "bob", "tenant-a"));
        Assert.False(await _store.RevokeAsync(digest, "alice", "tenant-b"));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.NotNull(await _store.TakeAsync(digest, "alice", "tenant-a", Now));
    }

    [Fact]
    public async Task SameSubjectInDifferentTenants_doesNotCrossConsume()
    {
        var a = Digest("tenant-a");
        var b = Digest("tenant-b");
        await _store.StoreWithEvidenceAsync(a, Approval("alice", "tenant-a"), Evidence("alice", "tenant-a", Now, Now.AddMinutes(1)));
        await _store.StoreWithEvidenceAsync(b, Approval("alice", "tenant-b"), Evidence("alice", "tenant-b", Now, Now.AddMinutes(1)));
        Assert.Null(await _store.TakeAsync(a, "alice", "tenant-b", Now));
        Assert.Null(await _store.TakeAsync(b, "alice", "tenant-a", Now));
        Assert.Equal("tenant-a", (await _store.TakeAsync(a, "alice", "tenant-a", Now))!.Request.Security!.Tenant);
        Assert.Equal("tenant-b", (await _store.TakeAsync(b, "alice", "tenant-b", Now))!.Request.Security!.Tenant);
    }

    [Fact]
    public async Task MalformedLookupInputs_neverReachOrConsumeRows()
    {
        var digest = Digest("malformed");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(1)));
        Assert.Null(await _store.TakeAsync(null!, "alice", "t", Now));
        Assert.Null(await _store.TakeAsync("' OR '1'='1", "alice", "t", Now));
        Assert.Null(await _store.TakeAsync(digest, null!, "t", Now));
        Assert.False(await _store.RevokeAsync("' OR '1'='1", "alice", "t"));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "TRUE"));
    }

    // ------------------------------------------------------------ expiry / revocation / codec recovery

    [Fact]
    public async Task Expiry_isExclusiveAtTheBoundaryAndExpiredRowsAreNeverConsumed()
    {
        var expires = Now.AddMinutes(10);
        var digest = Digest("expiry");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, expires));

        Assert.Null(await _store.TakeAsync(digest, "alice", "t", expires));
        Assert.Null(await _store.TakeAsync(digest, "alice", "t", expires.AddSeconds(1)));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.NotNull(await _store.TakeAsync(digest, "alice", "t", expires.AddMilliseconds(-1)));
    }

    [Fact]
    public async Task PurgeExpired_removesOnlyExpiredRowsInBatches()
    {
        for (var i = 0; i < 5; i++)
            await _store.StoreWithEvidenceAsync(Digest("old" + i), Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(1 + i)));
        var live = Digest("live");
        await _store.StoreWithEvidenceAsync(live, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddHours(1)));

        var cutoff = Now.AddMinutes(10);
        Assert.Equal(2, await _store.PurgeExpiredAsync(cutoff, 2));
        Assert.Equal(3, await _store.PurgeExpiredAsync(cutoff, 100));
        Assert.Equal(0, await _store.PurgeExpiredAsync(cutoff, 100));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "TRUE"));
        Assert.NotNull(await _store.TakeAsync(live, "alice", "t", cutoff));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await _store.PurgeExpiredAsync(cutoff, 0));
    }

    [Fact]
    public async Task Revocation_isOwnerBoundOneTimeAndPreventsConsumption()
    {
        var digest = Digest("revoke");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(1)));
        Assert.False(await _store.RevokeAsync(digest, "bob", "t"));
        Assert.True(await _store.RevokeAsync(digest, "alice", "t"));
        Assert.False(await _store.RevokeAsync(digest, "alice", "t"));
        Assert.Null(await _store.TakeAsync(digest, "alice", "t", Now));
        Assert.Equal(1L, await Count("foundgine_outbox", "TRUE"));
    }

    [Fact]
    public async Task CodecDecodeFailure_rollsBackConsumptionAndAllowsRetry()
    {
        var digest = Digest("decode");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(10)));

        _codec.FailDecode = true;
        await Assert.ThrowsAnyAsync<Exception>(async () => await _store.TakeAsync(digest, "alice", "t", Now));
        Assert.Equal(1L, await Count("foundgine_gateway_approvals", "TRUE"));

        _codec.FailDecode = false;
        Assert.NotNull(await _store.TakeAsync(digest, "alice", "t", Now));
        Assert.Null(await _store.TakeAsync(digest, "alice", "t", Now));
    }

    [Fact]
    public async Task DecodeFailureDoesNotLetAConcurrentConsumerDoubleSpend()
    {
        var digest = Digest("decode-race");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(10)));
        _codec.FailFirstDecodes = 3;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try { return await _store.TakeAsync(digest, "alice", "t", Now); }
            catch (InvalidOperationException) { return null; }
        })));
        Assert.True(results.Count(r => r is not null) <= 1);
        Assert.Equal(0L, await Count("foundgine_gateway_approvals", "TRUE"));
    }

    // ------------------------------------------------------------ helpers

    [Fact]
    public async Task SlowCodecDecode_doesNotHoldARowLockOrTransaction()
    {
        var digest = Digest("slow-decode");
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"), Evidence("alice", "t", Now, Now.AddMinutes(10)));
        _codec.DecodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _codec.DecodeDelayMillis = 1500;
        var taker = Task.Run(async () => await _store.TakeAsync(digest, "alice", "t", Now));
        await _codec.DecodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var c = await _dataSource.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync())
        await using (var cmd = new NpgsqlCommand("SELECT handle_digest FROM foundgine_gateway_approvals WHERE handle_digest=@digest FOR UPDATE NOWAIT", c, tx))
        {
            cmd.Parameters.AddWithValue("digest", digest);
            Assert.NotNull(await cmd.ExecuteScalarAsync());
            await tx.CommitAsync();
        }
        Assert.NotNull(await taker.WaitAsync(TimeSpan.FromSeconds(10)));
        _codec.DecodeStarted = null; _codec.DecodeDelayMillis = 0;
    }

    [Fact]
    public async Task encryptedPayloadSupportsKeyRotationAndFailsClosedOnTamperOrRetiredKey()
    {
        var inner = new TestCodec();
        var key1 = RandomNumberGenerator.GetBytes(32); var key2 = RandomNumberGenerator.GetBytes(32);
        var provider = new TestKeyProvider(key1, key2);
        var encrypted = new EncryptingFoundgineGatewayApprovalPayloadCodec(inner, provider);
        var first = encrypted.EncodeApproval(Approval("alice", "t"));
        Assert.Contains("key-1", first);
        Assert.Equal("alice", encrypted.DecodeApproval(first).Request.Security!.Subject);
        provider.Current = "key-2";
        var second = encrypted.EncodeApproval(Approval("alice", "t"));
        Assert.Contains("key-2", second);
        Assert.Equal("alice", encrypted.DecodeApproval(first).Request.Security!.Subject);
        Assert.Throws<InvalidOperationException>(() => encrypted.DecodeApproval(first.Replace("key-1", "key-retired")));
        Assert.Throws<InvalidOperationException>(() => encrypted.DecodeApproval(second.Replace("key-2", "key-1")));
        await Task.CompletedTask;
    }

    private sealed class TestKeyProvider(byte[] key1, byte[] key2) : IFoundgineGatewayApprovalKeyProvider
    {
        public string Current = "key-1";
        public string CurrentKeyId => Current;
        public byte[] GetKey(string keyId) => keyId switch
        { "key-1" => key1.ToArray(), "key-2" => key2.ToArray(), _ => throw new CryptographicException("retired key") };
    }

    [Fact]
    public async Task DatabaseClockEnforcement_rejectsApprovalExpiredOnDatabaseClock()
    {
        var digest = Digest("database-clock");
        var databaseNow = DateTimeOffset.UtcNow;
        await _store.StoreWithEvidenceAsync(digest, Approval("alice", "t"),
            Evidence("alice", "t", databaseNow.AddMinutes(-10), databaseNow.AddMinutes(-1)));
        var conservative = new PostgresFoundgineGatewayApprovalStore(_dataSource, _codec);
        Assert.Null(await conservative.TakeAsync(digest, "alice", "t", databaseNow.AddMinutes(-5)));
    }

    private static string Digest(string seed) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static FoundgineGatewayApprovalEvidence Evidence(string subject, string? tenant, DateTimeOffset at, DateTimeOffset expires) =>
        new(Guid.NewGuid().ToString("N"), "ApprovalRequested", subject, tenant, "fp", at, expires);

    private static SecurityExecutionContext Ctx(string subject, string? tenant)
    {
        var warrant = new SecurityWarrant("w", "iss", subject, "aud", [], SecurityWarrantConstraints.Unrestricted,
            DateTimeOffset.UnixEpoch, DateTimeOffset.MaxValue, "n", "k", null, []);
        return new SecurityExecutionContext(warrant, subject, "aud", tenant);
    }

    private static MutationPlanApproval Approval(string subject, string? tenant) =>
        new(new SemanticMutationRequest(new SemanticMutationOperationGraph([]), Ctx(subject, tenant)),
            "approval-" + subject, "fp", subject, Now);

    private sealed class TestCodec : IFoundgineGatewayApprovalPayloadCodec
    {
        public volatile bool FailEncode;
        public volatile bool FailDecode;
        public volatile TaskCompletionSource? DecodeStarted;
        public volatile int DecodeDelayMillis;
        private int _failFirstDecodes;
        public int FailFirstDecodes { set => Volatile.Write(ref _failFirstDecodes, value); }

        public string EncodeApproval(MutationPlanApproval a)
        {
            if (FailEncode) throw new InvalidOperationException("injected encode failure");
            var s = a.Request.Security!;
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["approvalId"] = a.ApprovalId, ["fingerprint"] = a.PlanFingerprint, ["approvedBy"] = a.ApprovedBy,
                ["subject"] = s.Subject, ["tenant"] = s.Tenant ?? "", ["hasTenant"] = s.Tenant is not null,
            });
        }

        public MutationPlanApproval DecodeApproval(string payload)
        {
            DecodeStarted?.TrySetResult();
            if (DecodeDelayMillis > 0) Thread.Sleep(DecodeDelayMillis);
            if (FailDecode) throw new InvalidOperationException("injected decode failure");
            while (true)
            {
                var n = Volatile.Read(ref _failFirstDecodes);
                if (n <= 0) break;
                if (Interlocked.CompareExchange(ref _failFirstDecodes, n - 1, n) == n)
                    throw new InvalidOperationException("injected decode failure");
            }
            using var doc = JsonDocument.Parse(payload);
            var r = doc.RootElement;
            var tenant = r.GetProperty("hasTenant").GetBoolean() ? r.GetProperty("tenant").GetString() : null;
            return new MutationPlanApproval(
                new SemanticMutationRequest(new SemanticMutationOperationGraph([]), Ctx(r.GetProperty("subject").GetString()!, tenant)),
                r.GetProperty("approvalId").GetString()!, r.GetProperty("fingerprint").GetString()!,
                r.GetProperty("approvedBy").GetString()!, Now);
        }

        public string EncodeEvidence(FoundgineGatewayApprovalEvidence e) => JsonSerializer.Serialize(e);
    }

    private async Task<long> Count(string table, string where) =>
        (long)(await Scalar($"SELECT count(*) FROM {table} WHERE {where}"))!;

    private async Task<object?> Scalar(string sql)
    {
        await using var c = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task Exec(string sql)
    {
        await using var c = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}
