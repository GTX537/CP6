using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Crm;

public class CrmTenantIsolationTests
{
    private static CP6Context DbFor(string name, Guid tenant) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options,
        new TenantContext { CurrentTenantId = tenant });

    private static CrmLead NewLead(string no) => new()
    {
        LeadNo = no,
        Subject = "Website inquiry",
        CompanyName = "Example",
        NormalizedCompanyName = "EXAMPLE",
        ContactName = "Contact",
        SourceChannel = CrmSourceChannel.Website,
        SlaDueAt = DateTime.UtcNow.AddHours(4),
    };

    [Fact]
    public async Task Lead_IsStampedAndHiddenFromOtherTenant()
    {
        var database = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var db = DbFor(database, tenantA))
        {
            db.CrmLeads.Add(NewLead("LEAD-A"));
            await db.SaveChangesAsync();
        }

        using (var db = DbFor(database, tenantA))
        {
            var lead = Assert.Single(await db.CrmLeads.ToListAsync());
            Assert.Equal(tenantA, lead.TenantId);
        }
        using (var db = DbFor(database, tenantB))
            Assert.Empty(await db.CrmLeads.ToListAsync());
    }

    [Fact]
    public async Task PublicRouteRegistry_IsSharedButContainsNoBusinessPayload()
    {
        var database = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var target = Guid.NewGuid();

        using (var db = DbFor(database, tenantA))
        {
            db.CrmPublicRoutes.Add(new CrmPublicRoute
            {
                TenantId = tenantA,
                RouteType = "site",
                PublicKey = "example-site",
                TargetId = target,
            });
            await db.SaveChangesAsync();
        }

        using var otherTenantView = DbFor(database, tenantB);
        var route = Assert.Single(await otherTenantView.CrmPublicRoutes.ToListAsync());
        Assert.Equal(tenantA, route.TenantId);
        Assert.Equal(target, route.TargetId);
    }
}
