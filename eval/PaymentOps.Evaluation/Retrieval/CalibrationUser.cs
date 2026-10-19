using PaymentOps.Domain.Security;

namespace PaymentOps.Evaluation.Retrieval;

/// <summary>
/// A synthetic signed-in user for one access class, so calibration goes through the same
/// identity-to-filter step as the app instead of building a filter itself.
/// </summary>
internal sealed record CalibrationUser(bool CanAccessRestrictedKnowledge) : IUserContext
{
    public bool IsAuthenticated => true;

    public string UserId => "calibration";

    public string DisplayName => "Retrieval calibration";

    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();

    public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

    public static CalibrationUser For(string access) => new(access == "restricted");

    public bool IsInRole(string role) => false;
}
