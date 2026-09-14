namespace Foundgine.Core.Semantic.IR;

public static class SemanticReadNodeExtensions
{
    public static IEnumerable<SemanticReadNode> TraverseDepthFirst(this SemanticReadNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Traverse(root);

        static IEnumerable<SemanticReadNode> Traverse(SemanticReadNode node)
        {
            yield return node;
            foreach (var child in node.Children)
            foreach (var descendant in Traverse(child))
                yield return descendant;
        }
    }
}
