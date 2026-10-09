using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PaymentOps.Web.Security;

/// <summary>
/// Stands in for Entra ID sign-in until the environment has an app registration: nobody is signed
/// in, and anything that needs a user answers 503 instead of failing.
/// </summary>
internal sealed class SignInNotConfiguredHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SignInNotConfigured";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Response.WriteAsync("Sign-in isn't configured for this environment yet.");
    }
}
