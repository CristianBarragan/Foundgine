namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Physical execution hints for aggregate predicates. These hints do not
/// change semantic meaning; providers may use them to short-circuit work.
/// </summary>
public enum AggregateExecutionStrategy : byte
{
    Default = 0,
    CountExistsShortCircuit = 1,
    CountEmptyShortCircuit = 2
}
