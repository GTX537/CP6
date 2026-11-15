using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Services;

internal sealed class SpaceDesignWriteAuthority(
    IHttpContextAccessor http,
    IServiceScopeFactory scopeFactory,
    ISpaceExecutionContext execution) : ISpaceDesignWriteAuthority
{
    public Task AuthorizeAsync(SpaceContext context, string requiredPermission, CancellationToken ct, string? additionalPermission = null)
        => CheckAsync(context, requiredPermission, additionalPermission, ct);

    public Task ConfirmSessionAsync(SpaceContext context, CancellationToken ct)
        => CheckAsync(context, requiredPermission: null, additionalPermission: null, ct);

    private async Task CheckAsync(SpaceContext space, string? requiredPermission, string? additionalPermission, CancellationToken ct)
    {
        // Internal Space work has its own execution context. This boundary consumes the HTTP session.
        if (http.HttpContext is not { } request)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = space.CurrentTenantId;
        var core = services.GetRequiredService<CP6Context>();
        try
        {
            core.Database.SetDbConnection(space.Database.GetDbConnection(), contextOwnsConnection: false);
            await core.Database.UseTransactionAsync(space.Database.CurrentTransaction!.GetDbTransaction(), ct);
            if (requiredPermission is not null)
                await AuthenticationCompletionGateV1.AcquireAsync(core, ct);

            var actor = await IamAdministrationHttpV1.CurrentActor(request, services, ct, allowImpersonation: true);
            if (actor != execution.ActorId)
                throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
            if (requiredPermission is not null)
            {
                var current = await services.GetRequiredService<ICurrentPermissionContext>().GetAsync();
                if (!current.ActionKeys.Contains(requiredPermission) ||
                    (additionalPermission is not null && !current.ActionKeys.Contains(additionalPermission)))
                    throw new SpaceProblemException(SpaceErrorCodes.PermissionDenied, 403,
                        "The Space request was denied.", recoveryAction: "request-access");
            }
        }
        catch (IamAdministrationExceptionV1 error) when (error.Status == 401)
        {
            throw new SpaceProblemException(SpaceErrorCodes.AuthenticationRequired, 401,
                "Authentication is required.", recoveryAction: "authenticate");
        }
        catch (Exception error) when ((error is DbException or CurrentPermissionUnavailableException or TimeoutException)
            && !DatabaseFailureClassifier.Classify(error).CanRetryTransaction)
        {
            throw new SpaceProblemException("SPACE_AUTHORITY_UNAVAILABLE", 503,
                "The Space write authority is unavailable.", retryable: true);
        }
    }
}
