using System.Collections.Concurrent;
using System.Security.Cryptography;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpConcurrencyTests
{
    private const int Threads = 16;

    /// <summary>Runs <paramref name="task"/> on <see cref="Threads"/> threads released together; rethrows the first failure.</summary>
    private static void Race(Action<int> task)
    {
        using var go = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, Threads).Select(i => Task.Factory.StartNew(() => { go.Wait(); task(i); },
            TaskCreationOptions.LongRunning)).ToArray();
        go.Set();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(60)), "threads did not finish");
    }

    [Fact]
    public void SessionCapIsExactUnderContentionAndIdsStayUnique()
    {
        const int cap = 500;
        var m = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), cap), new InMemoryFoundgineMcpSessionStore());
        var ids = new ConcurrentDictionary<string, byte>();
        var refused = 0;
        Race(_ =>
        {
            for (var k = 0; k < 100; k++)
            {
                try { Assert.True(ids.TryAdd(m.Create().SessionId, 0), "duplicate session id"); }
                catch (InvalidOperationException) { Interlocked.Increment(ref refused); }
            }
        });
        Assert.Equal(cap, ids.Count);
        Assert.Equal(cap, m.ActiveSessionCount);
        Assert.Equal(Threads * 100 - cap, refused);
    }

    [Fact]
    public void ExactlyOneConcurrentTerminateWinsAndValidationNeverRevivesIt()
    {
        var m = new FoundgineMcpSessionManager();
        for (var round = 0; round < 50; round++)
        {
            var id = m.Create("alice").SessionId;
            var wins = 0;
            Race(i =>
            {
                if (i % 2 == 0) { if (m.Terminate(id, "alice")) Interlocked.Increment(ref wins); }
                else m.Validate(id, "alice");
            });
            Assert.Equal(1, wins);
            Assert.False(m.Validate(id, "alice").IsValid);
        }
        Assert.Equal(0, m.ActiveSessionCount);
    }

    [Fact]
    public void ExpiryAndPurgeRaceSafely()
    {
        long now = 1_000;
        var m = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), 100_000),
            new InMemoryFoundgineMcpSessionStore(), () => Interlocked.Read(ref now));
        var removed = 0; var created = 0;
        Race(i =>
        {
            for (var k = 0; k < 200; k++)
            {
                if (i % 4 == 0) Interlocked.Add(ref now, 7);
                else if (i % 4 == 1) Interlocked.Add(ref removed, m.PurgeExpired());
                else { var s = m.Create(); Interlocked.Increment(ref created); m.Validate(s.SessionId); }
            }
        });
        Interlocked.Add(ref now, 10_000);
        Interlocked.Add(ref removed, m.PurgeExpired());
        Assert.Equal(0, m.ActiveSessionCount);
        Assert.True(removed <= created);
    }

    [Fact]
    public void RotationRevocationAndVerificationNeverObserveATornKeySet()
    {
        var keys = Enumerable.Range(0, 6).Select(_ => RSA.Create(2048)).ToList();
        try
        {
            var ring = new FoundgineMcpApprovalKeyRing(TimeSpan.FromSeconds(1));
            ring.ActivateSigningKey("k0", keys[0]);
            using var stop = new CancellationTokenSource();
            var failure = new ConcurrentQueue<Exception>();
            var verified = 0;
            var payload = "payload"u8.ToArray();

            var readers = Enumerable.Range(0, Threads - 1).Select(_ => Task.Factory.StartNew(() =>
            {
                while (!stop.IsCancellationRequested && failure.IsEmpty)
                {
                    try
                    {
                        var signer = ring.Snapshot();                       // id and key fixed together
                        var sig = signer.Sign(payload);
                        var pub = ring.Resolve(signer.KeyId)                // retained for an hour: must still resolve
                            ?? throw new InvalidOperationException("a retained key stopped resolving: " + signer.KeyId);
                        using var verifier = RSA.Create();
                        verifier.ImportParameters(pub.ExportParameters(false));
                        if (!verifier.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                            throw new InvalidOperationException("signature does not verify under its own key id");
                        Interlocked.Increment(ref verified);
                    }
                    catch (Exception e) { failure.Enqueue(e); }
                }
            }, TaskCreationOptions.LongRunning)).ToArray();

            for (var i = 1; i < keys.Count; i++)
            {
                ring.ActivateSigningKey("k" + i, keys[i], TimeSpan.FromHours(1));
                Thread.Sleep(20);
            }
            stop.Cancel();
#pragma warning disable xUnit1031
            Assert.True(Task.WaitAll(readers, TimeSpan.FromSeconds(60)));
#pragma warning restore xUnit1031
            if (failure.TryPeek(out var first)) throw new Xunit.Sdk.XunitException(first.ToString());
            Assert.True(verified > 0);
            Assert.Equal("k5", ring.KeyId);
            Assert.Equal(6, ring.Keys().Count);
            Assert.True(ring.Revoke("k5"));
            Assert.Null(ring.Resolve("k5"));
        }
        finally { foreach (var k in keys) k.Dispose(); }
    }
}
