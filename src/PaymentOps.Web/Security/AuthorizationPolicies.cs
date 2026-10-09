using Microsoft.AspNetCore.Authorization;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

/// <summary>Named authorization policies. Higher roles satisfy lower ones.</summary>
internal static class AuthorizationPolicies
{
    public const string ApproveActions = nameof(ApproveActions);
    public const string Administer = nameof(Administer);

    public static void Configure(AuthorizationOptions options)
    {
        // Every page and endpoint requires an app role unless it explicitly allows anonymous access.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(OpsRoles.AtLeast(OpsRoles.Reader))
            .Build();

        options.AddPolicy(ApproveActions, policy => policy.RequireRole(OpsRoles.AtLeast(OpsRoles.Engineer)));
        options.AddPolicy(Administer, policy => policy.RequireRole(OpsRoles.AtLeast(OpsRoles.Admin)));
    }
}
