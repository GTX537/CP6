using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public sealed class FieldPermissionSourceBoundaryTests
{
    [Fact]
    public async Task Actual_context_entry_does_not_fall_back_to_non_postgresql_or_legacy_aggregate()
    {
        await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = await CurrentPermissionContextTests.Seed(db);
        var current = CurrentPermissionContextTests.Context(db, CurrentPermissionContextTests.Principal(user, db.CurrentTenantId));
        Assert.Contains("order:read", (await current.GetAsync()).ActionKeys);
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => current.GetFieldSourceAsync("order"));
    }
}
