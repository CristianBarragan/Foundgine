namespace Foundgine.Testing;

public static class ExecutionIRTestFactory
{
    public static Foundgine.Core.Execution.ExecutionIR Create(
        Foundgine.Core.Execution.ExecutionIRNode root,
        IReadOnlyList<string> requiredSecurityInvariants)
    {
        return new Foundgine.Core.Execution.ExecutionIR(
            root,
            requiredSecurityInvariants,
            new Foundgine.Core.Semantic.Planning.SemanticPlanAuthorizationBinding(
                "test-contract",
                "test-authorization"));
    }
}