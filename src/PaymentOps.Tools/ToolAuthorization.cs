using System.Collections.Frozen;

using PaymentOps.Domain.Security;

namespace PaymentOps.Tools;

/// <summary>
/// Which role each tool needs. Checked when a turn's tool list is built and again when a tool runs, so a
/// tool the user can't use is neither offered nor executable.
/// </summary>
public static class ToolAuthorization
{
    private static readonly FrozenDictionary<string, string> MinimumRole =
        ToolNames.ReadTools.ToFrozenDictionary(tool => tool, _ => OpsRoles.Reader, StringComparer.Ordinal);

    public static bool Allows(IUserContext user, string tool)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.IsAuthenticated && MinimumRole.TryGetValue(tool, out var role) && user.IsInRole(role);
    }
}
