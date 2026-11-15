using System.Data;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public interface ISpaceDesignWriteAuthority
{
    Task AuthorizeAsync(SpaceContext context, string requiredPermission, CancellationToken cancellationToken, string? additionalPermission = null);
    Task ConfirmSessionAsync(SpaceContext context, CancellationToken cancellationToken);
}

internal static class SpaceDesignWriteTransaction
{
    internal static async Task<IDbContextTransaction> BeginAsync(SpaceContext context, ISpaceDesignWriteAuthority? authority,
        CancellationToken cancellationToken, string? additionalPermission = null, string requiredPermission = "space:model:edit")
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (authority is not null && context.Database.IsNpgsql())
                await authority.AuthorizeAsync(context, requiredPermission, cancellationToken, additionalPermission);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    internal static async Task<IDbContextTransaction> BeginForPermissionAsync(SpaceContext context, ISpaceDesignWriteAuthority? authority,
        string requiredPermission, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await BeginAsync(context, authority, cancellationToken, requiredPermission: requiredPermission);
            }
            catch (Exception error) when (authority is not null && context.Database.IsNpgsql() &&
                                          DatabaseFailureClassifier.Classify(error).CanRetryTransaction)
            {
                if (attempt == 3)
                    throw new SpaceProblemException(SpaceErrorCodes.ConcurrencyConflict, 409,
                        "The Space write authority changed concurrently.", retryable: true);
            }
        }
    }

    internal static async Task CommitAsync(SpaceContext context, ISpaceDesignWriteAuthority? authority,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        if (authority is not null && context.Database.IsNpgsql())
            await authority.ConfirmSessionAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
