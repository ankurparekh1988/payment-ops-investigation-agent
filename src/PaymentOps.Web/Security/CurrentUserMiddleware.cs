namespace PaymentOps.Web.Security;

/// <summary>Makes the authenticated user available to scoped services during an HTTP request.</summary>
internal sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, CurrentUserAccessor accessor)
    {
        accessor.User = context.User;
        return next(context);
    }
}
