using Microsoft.AspNetCore.Authorization;

namespace BackupGateway.Web.Services.Auth;

internal sealed class TargetGrantAuthorizationHandler(ITargetAuthorizationService targetAuthorizationService)
    : AuthorizationHandler<TargetGrantRequirement>
{
    protected async override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TargetGrantRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is not HttpContext httpContext
            || !ClientIdentity.TryGetId(context.User, out Guid clientId)
            || httpContext.Request.RouteValues["targetId"] is not string targetId
            || string.IsNullOrWhiteSpace(targetId)
            || targetId.Length > 128)
        {
            return;
        }

        if (await targetAuthorizationService.IsGrantedAsync(clientId, targetId, httpContext.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
