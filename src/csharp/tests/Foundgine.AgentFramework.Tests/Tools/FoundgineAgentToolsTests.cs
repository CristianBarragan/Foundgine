using Foundgine.Providers.AgentFramework.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using static Foundgine.AgentFramework.Tests.Support;

namespace Foundgine.AgentFramework.Tests.Tools;

public sealed class FoundgineAgentToolsTests
{
    private static string[] Names(IEnumerable<AITool> tools) => tools.Select(t => t.Name).OrderBy(n => n).ToArray();

    [Fact]
    public void DefaultSurfaceIsReadOnly()
    {
        var names = Names(CreateTools(Context(), new RecordingMutations()).CreateTools());
        Assert.Equal(new[] { "foundgine_capabilities", "foundgine_query_semantic" }, names);
    }

    [Fact]
    public void OpenIntentToolOnlyWhenGroundingIsConfigured()
    {
        Assert.DoesNotContain("foundgine_query", Names(CreateTools(Context(), null, openIntent: false).CreateTools()));
        Assert.Contains("foundgine_query", Names(CreateTools(Context(), null, openIntent: true).CreateTools()));
    }

    [Fact]
    public void MutationSurfaceHasNoModelCallableApproveAndExecuteRequiresApproval()
    {
        var tools = CreateTools(Context(), new RecordingMutations()).CreateTools(includeMutations: true);

        Assert.DoesNotContain(tools, t => t.Name.Contains("approve", StringComparison.OrdinalIgnoreCase));
        Assert.IsType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == "foundgine_mutation_execute"));
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == "foundgine_mutation_dry_run"));
    }

    [Fact]
    public void MutationsRequireAConfiguredEngine() =>
        Assert.Throws<InvalidOperationException>(() => CreateTools(Context(), null).CreateTools(includeMutations: true));

    [Fact]
    public async Task ExecuteRejectsPlanFingerprintThatDoesNotMatchAndRunsNothing()
    {
        var mutations = new RecordingMutations("fp-1");
        var tools = CreateTools(Context(), mutations);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.ExecuteAsync(MutationJson, "forged-fp"));

        Assert.Equal(0, mutations.Approvals);
        Assert.Equal(0, mutations.Executions);
    }

    [Fact]
    public async Task ExecuteRecordsHostSubjectAsApproverAndRunsExactlyOnce()
    {
        var mutations = new RecordingMutations("fp-1");
        var security = Context();
        var tools = CreateTools(security, mutations);

        var json = await tools.ExecuteAsync(MutationJson, "fp-1");

        Assert.Equal(Subject, mutations.ApprovedBy);
        Assert.Equal(1, mutations.Approvals);
        Assert.Equal(1, mutations.Executions);
        Assert.Same(security, mutations.SecurityOnExecute);
        Assert.Contains("fp-1", json);
    }

    [Fact]
    public async Task MissingSecurityContextFailsClosedForExecuteAndDryRun()
    {
        var mutations = new RecordingMutations();
        var tools = CreateTools(null, mutations);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ExecuteAsync(MutationJson, "fp-1"));
        Assert.Throws<UnauthorizedAccessException>(() => tools.DryRun(MutationJson));

        Assert.Equal(0, mutations.DryRuns);
        Assert.Equal(0, mutations.Approvals);
        Assert.Equal(0, mutations.Executions);
    }

    [Fact]
    public void DryRunReturnsFingerprintAndEffects()
    {
        var json = CreateTools(Context(), new RecordingMutations("fp-9")).DryRun(MutationJson);
        Assert.Contains("\"planFingerprint\":\"fp-9\"", json);
        Assert.Contains("create Customer", json);
    }

    [Fact]
    public async Task ExecuteRequiresAPlanFingerprint() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateTools(Context(), new RecordingMutations()).ExecuteAsync(MutationJson, " "));
}
