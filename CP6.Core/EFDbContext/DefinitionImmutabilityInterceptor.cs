using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Core.EFDbContext;

/// <summary>Fail-closed guard for immutable published definition rows, including direct EF writes.</summary>
public sealed class DefinitionImmutabilityInterceptor : SaveChangesInterceptor
{
    internal static void Guard(DbContext context)
    {
        GuardEntries(context.ChangeTracker.Entries<Wf_FlowDefVersion>());
        GuardEntries(context.ChangeTracker.Entries<Wf_FormDefVersion>());
    }

    private static void GuardEntries<TEntity>(IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>> entries)
        where TEntity : class
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var status = entry.Property(nameof(Wf_FlowDefVersion.Status));
            var wasPublished = Convert.ToInt32(status.OriginalValue) == WfDefinitionVersionStatus.Published;
            if (wasPublished)
                throw new InvalidOperationException("E-WF-037");
        }
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context != null) Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null) Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }
}
