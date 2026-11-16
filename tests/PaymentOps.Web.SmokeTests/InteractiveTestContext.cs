using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// A bUnit context for interactive components. MudBlazor's services dispose only asynchronously, so the context is
/// disposed through xUnit's async lifetime; the renderer reports Interactive Server, as the app runs.
/// </summary>
public abstract class InteractiveTestContext : IAsyncLifetime
{
    protected InteractiveTestContext()
    {
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
        Context.Services.AddMudServices();
    }

    protected BunitContext Context { get; } = new();

    /// <summary>Register test services before the first render: the renderer resolves services when it's configured.</summary>
    protected IRenderedComponent<TComponent> Render<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : IComponent
    {
        Context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Context.Render(parameters);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Context.DisposeAsync().AsTask();
}
