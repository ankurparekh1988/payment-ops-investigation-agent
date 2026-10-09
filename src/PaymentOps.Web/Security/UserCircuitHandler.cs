using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PaymentOps.Web.Security;

/// <summary>
/// Keeps <see cref="CurrentUserAccessor"/> current for the lifetime of a Blazor circuit, following
/// the documented pattern for using the authentication state in circuit-scoped services.
/// </summary>
internal sealed class UserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CurrentUserAccessor accessor)
    : CircuitHandler, IDisposable
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        accessor.User = state.User;
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;

    private void OnAuthenticationChanged(Task<AuthenticationState> task) =>
        _ = UpdateUser(task);

    private async Task UpdateUser(Task<AuthenticationState> task)
    {
        var state = await task;
        accessor.User = state.User;
    }
}
