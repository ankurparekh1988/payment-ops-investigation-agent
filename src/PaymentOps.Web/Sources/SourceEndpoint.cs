using Microsoft.AspNetCore.Http.HttpResults;

using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Pages;

namespace PaymentOps.Web.Sources;

/// <summary>
/// The section a citation points at, re-authorized on every request. Missing and not permitted are
/// the same 404, so the endpoint never reveals that a Restricted section exists.
/// </summary>
internal static class SourceEndpoint
{
    public static async Task<RazorComponentResult<SourcePage>> HandleAsync(string sectionKey, IInvestigationService investigation, CancellationToken cancellationToken)
    {
        var source = await investigation.GetSourceAsync(sectionKey, cancellationToken);
        return new RazorComponentResult<SourcePage>(new Dictionary<string, object?> { [nameof(SourcePage.Source)] = source })
        {
            StatusCode = source switch
            {
                SourceResult.Found => StatusCodes.Status200OK,
                SourceResult.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status404NotFound,
            },
        };
    }
}
