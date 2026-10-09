namespace PaymentOps.Domain.Security;

/// <summary>
/// The signed-in user, as established by the server from their token. Authorization decisions in
/// tools and retrieval use this, never anything the model says.
/// </summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>Stable Entra object ID; empty when not signed in.</summary>
    string UserId { get; }

    string DisplayName { get; }

    /// <summary>Effective app roles, including those implied by a higher role.</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>Object IDs of the security groups assigned to the application that the user belongs to.</summary>
    IReadOnlySet<string> GroupIds { get; }

    bool IsInRole(string role);
}
