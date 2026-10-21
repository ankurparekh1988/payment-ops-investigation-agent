using PaymentOps.Domain.Security;

namespace PaymentOps.Tools;

/// <summary>A read-only tool as the agent sees it: a name, a description, a request type and one way to run it.</summary>
public interface IInvestigationTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>The request record the model's arguments are read into, with snake_case names.</summary>
    Type RequestType { get; }

    /// <summary>Names the data a timeout concerns, for the gap the turn records.</summary>
    string Source { get; }

    Task<ToolResult> RunAsync(object request, IUserContext user, CancellationToken cancellationToken);
}

/// <summary>
/// Checks the caller's role before anything else, then turns expected failures into results. Works on its
/// own, without the agent: the agent only adds limits and the turn's evidence.
/// </summary>
public abstract class InvestigationTool<TRequest> : IInvestigationTool
    where TRequest : class
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string Source { get; }

    public Type RequestType => typeof(TRequest);

    public Task<ToolResult> RunAsync(object request, IUserContext user, CancellationToken cancellationToken) =>
        RunAsync((TRequest)request, user, cancellationToken);

    public async Task<ToolResult> RunAsync(TRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        if (!ToolAuthorization.Allows(user, Name))
        {
            return new ToolResult.Failed(ToolErrorCode.NotAuthorized, "This tool isn't available to the current user.");
        }

        try
        {
            return await RunCoreAsync(request, user, cancellationToken);
        }
        catch (ToolFailedException ex)
        {
            return ex.Result;
        }
        catch (ArgumentException)
        {
            // Our validation runs first, so this is a port refusing something we let through.
            return new ToolResult.Failed(ToolErrorCode.InvalidArgument, "The data source refused these arguments.");
        }
    }

    protected abstract Task<ToolResult> RunCoreAsync(TRequest request, IUserContext user, CancellationToken cancellationToken);

    protected static ToolResult.Failed Invalid(string message, ToolErrorCode code = ToolErrorCode.InvalidArgument) => new(code, message);
}

/// <summary>Ends a tool run early with a result, from inside a helper.</summary>
internal sealed class ToolFailedException(ToolResult.Failed result) : Exception(result.Message)
{
    public ToolResult.Failed Result { get; } = result;
}
