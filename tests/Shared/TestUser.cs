using PaymentOps.Domain.Security;

namespace PaymentOps.Tests.Shared;

/// <summary>A signed-in (or anonymous) user with General or Restricted knowledge access.</summary>
internal sealed record TestUser(bool IsAuthenticated, bool CanAccessRestrictedKnowledge) : IUserContext
{
    public static TestUser General { get; } = new(true, false);

    public static TestUser Restricted { get; } = new(true, true);

    public static TestUser Anonymous { get; } = new(false, false);

    /// <summary>A signed-in Ops Reader with General knowledge access.</summary>
    public static TestUser Reader { get; } = General with { Roles = OpsRoles.Effective([OpsRoles.Reader]) };

    /// <summary>An Ops Reader who may also read Restricted knowledge.</summary>
    public static TestUser RestrictedReader { get; } = Restricted with { Roles = OpsRoles.Effective([OpsRoles.Reader]) };

    /// <summary>An Operations Engineer, who may approve tickets from their own investigations.</summary>
    public static TestUser Engineer { get; } = General with { Roles = OpsRoles.Effective([OpsRoles.Engineer]), UserId = "00000000-0000-0000-0000-000000000002" };

    /// <summary>A second Engineer, who owns none of the first one's proposals.</summary>
    public static TestUser OtherEngineer { get; } = Engineer with { UserId = "00000000-0000-0000-0000-000000000003" };

    private readonly string? _userId;

    public string UserId
    {
        get => _userId ?? (IsAuthenticated ? "00000000-0000-0000-0000-000000000001" : string.Empty);
        init => _userId = value;
    }

    public string DisplayName => "Test";

    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

    public bool IsInRole(string role) => Roles.Contains(role);
}
