using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Persistence.Tenancy;

// The ordinary lease keeps search_path=pg_catalog. The native public model is fixed by context type.
internal sealed class StockMovementWriteContextV1 : CP6Context
{
    internal StockMovementWriteContextV1(DbContextOptions<CP6Context> options, Guid tenant)
        : base(options, new TenantContext { CurrentTenantId = tenant })
    {
        // A failed movement rolls back the entire Owner transaction. The new movement row uses its top-level xid.
        Database.AutoSavepointsEnabled = false;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
    }
}
