using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Security;

/// <summary>
/// Approving creates a ticket, so it needs a recent sign-in. Checked here, around whichever approval service is
/// registered, so no caller can skip it; ownership, expiry and idempotency stay with the service inside.
/// </summary>
internal sealed class FreshApprovalService(IApprovalService inner, IRecentAuthentication recent) : IApprovalService
{
    internal IApprovalService Inner => inner;

    public Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken) =>
        recent.IsFresh ? inner.ApproveAsync(proposalId, cancellationToken) : Task.FromResult<ApprovalResult>(new ApprovalResult.NotAuthorized());

    // Rejecting creates nothing, so an older sign-in is enough.
    public Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken) =>
        inner.RejectAsync(proposalId, reason, note, cancellationToken);
}

internal static class FreshApprovalServiceCollectionExtensions
{
    /// <summary>Wraps the one registered <see cref="IApprovalService"/>, keeping its lifetime.</summary>
    public static IServiceCollection RequireRecentAuthenticationForApproval(this IServiceCollection services)
    {
        var registered = services.Where(d => d.ServiceType == typeof(IApprovalService) && !d.IsKeyedService).ToList();
        if (registered.Count != 1)
        {
            throw new InvalidOperationException("Expected exactly one IApprovalService registration.");
        }

        var descriptor = registered[0];
        services[services.IndexOf(descriptor)] = ServiceDescriptor.Describe(
            typeof(IApprovalService),
            sp => new FreshApprovalService(Inner(sp, descriptor), sp.GetRequiredService<IRecentAuthentication>()),
            descriptor.Lifetime);
        return services;
    }

    private static IApprovalService Inner(IServiceProvider services, ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { } type
            ? (IApprovalService)ActivatorUtilities.CreateInstance(services, type)
            : descriptor.ImplementationFactory is { } factory
                ? (IApprovalService)factory(services)
                : (IApprovalService)descriptor.ImplementationInstance!;
}
