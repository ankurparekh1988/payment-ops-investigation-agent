namespace PaymentOps.Domain.Security;

/// <summary>App roles, each including everything the roles below it can do.</summary>
public static class OpsRoles
{
    public const string Reader = "Ops.Reader";
    public const string Engineer = "Ops.Engineer";
    public const string Admin = "Ops.Admin";

    private static readonly string[] Hierarchy = [Reader, Engineer, Admin];

    /// <summary>Expands assigned roles to effective roles: an Admin is also an Engineer and a Reader.</summary>
    public static IReadOnlySet<string> Effective(IEnumerable<string> assignedRoles)
    {
        ArgumentNullException.ThrowIfNull(assignedRoles);

        var highest = assignedRoles.Select(role => Array.IndexOf(Hierarchy, role)).DefaultIfEmpty(-1).Max();
        return Hierarchy.Take(highest + 1).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The given role and every role above it, for checks against assigned roles.</summary>
    public static string[] AtLeast(string role)
    {
        var index = Array.IndexOf(Hierarchy, role);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Not an app role.");
        }

        return Hierarchy[index..];
    }
}
