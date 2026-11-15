using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public interface ISpaceDesignWriteAuthority
{
    Task AuthorizeModelEditAsync(SpaceContext context, CancellationToken cancellationToken, string? additionalPermission = null);
    Task ConfirmSessionAsync(SpaceContext context, CancellationToken cancellationToken);
}

internal static class SpaceDesignWriteTransaction
{
    internal static async Task<IDbContextTransaction> BeginAsync(SpaceContext context, ISpaceDesignWriteAuthority? authority,
        CancellationToken cancellationToken, string? additionalPermission = null)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (authority is not null && context.Database.IsNpgsql())
                await authority.AuthorizeModelEditAsync(context, cancellationToken, additionalPermission);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
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
