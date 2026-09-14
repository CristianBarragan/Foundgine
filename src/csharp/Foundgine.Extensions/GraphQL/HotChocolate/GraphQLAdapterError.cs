namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Structured GraphQL-facing adapter error. The core Foundgine pipeline remains
/// exception/transport agnostic; hosts can map this error to GraphQL errors.
/// </summary>
public sealed record GraphQLAdapterError(
    string Code,
    string Message,
    IReadOnlyList<object?>? Path = null,
    string Category = "BAD_REQUEST");

public sealed record GraphQLAdapterResult<T>(
    T? Data,
    IReadOnlyList<GraphQLAdapterError> Errors)
{
    public bool Succeeded => Errors.Count == 0;
    public bool IsSuccess => Succeeded;
    public GraphQLAdapterError? Error => Errors.FirstOrDefault();

    public static GraphQLAdapterResult<T> Success(T data) =>
        new(data, Array.Empty<GraphQLAdapterError>());

    public static GraphQLAdapterResult<T> Failure(GraphQLAdapterError error) =>
        new(default, [error]);
}

public static class GraphQLAdapterErrorCode
{
    public const string BadUserInput = "BAD_USER_INPUT";
    public const string GraphQLValidationFailed = "GRAPHQL_VALIDATION_FAILED";
    public const string AdapterError = "GRAPHQL_ADAPTER_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
}

/// <summary>
/// Converts adapter failures into stable GraphQL-facing error categories.
/// </summary>
public static class GraphQLAdapterErrors
{
    public static GraphQLAdapterError FromException(Exception exception)
    {
        // A missing/invalid security context is a distinct failure class from a
        // malformed GraphQL request: it is never the caller's request that is
        // wrong, so it must not be reported as BAD_USER_INPUT or a validation
        // failure, both of which default to Category "BAD_REQUEST".
        if (exception is UnauthorizedAccessException)
            return new GraphQLAdapterError(
                GraphQLAdapterErrorCode.Unauthenticated,
                exception.Message,
                Category: GraphQLAdapterErrorCode.Unauthenticated);

        var message = exception.Message;
        var code = message.Contains("variable", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("expects", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("cannot be null", StringComparison.OrdinalIgnoreCase)
            ? "BAD_USER_INPUT"
            : message.Contains("field", StringComparison.OrdinalIgnoreCase) ||
              message.Contains("argument", StringComparison.OrdinalIgnoreCase) ||
              message.Contains("fragment", StringComparison.OrdinalIgnoreCase) ||
              message.Contains("operation", StringComparison.OrdinalIgnoreCase)
                ? "GRAPHQL_VALIDATION_FAILED"
                : "GRAPHQL_ADAPTER_ERROR";

        return new GraphQLAdapterError(code, message);
    }
}
