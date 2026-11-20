using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_import_and_batch_cannot_write_unproven_legacy_qualification()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        var legacy = new BusinessPartner
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, BpCd = "OLD-" + h.Tenant.ToString("N")[..8],
            BpName = "模拟旧伙伴", BaseCd = "BPA", Status = 1, CustomerFlg = true
        };
        h.Db.BusinessPartners.Add(legacy);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await EnablePartnerUse(h.Db, h.Tenant);

        var direct = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."T_WebBusinessPartner" SET "IsFrozen"=true
            WHERE "TenantId"={h.Tenant} AND "Id"={legacy.Id}
            """));
        Assert.Equal("LOCK_PLAN_UNPROVEN", direct.MessageText);

        await h.Db.Database.OpenConnectionAsync();
        try
        {
            var connection = (NpgsqlConnection)h.Db.Database.GetDbConnection();
            var imported = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var importer = await connection.BeginTextImportAsync("""
                    COPY public."T_WebBusinessPartner" ("Id","TenantId","BpCd","BpName","BaseCd","Status","CustomerFlg")
                    FROM STDIN
                    """);
                await importer.WriteLineAsync($"{Guid.NewGuid():D}\t{h.Tenant:D}\tIMPORTED\t模拟批量导入\tBPA\t1\tt");
            });
            Assert.Equal("LOCK_PLAN_UNPROVEN", imported.MessageText);
        }
        finally { await h.Db.Database.CloseConnectionAsync(); }

        Assert.False(await h.Db.BusinessPartners.AsNoTracking().Where(row => row.Id == legacy.Id)
            .Select(row => row.IsFrozen).SingleAsync());
    }

    private static async Task EnablePartnerUse(CP6Context db, Guid tenant, string legal = UseLegal)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT main_bp.lock_use_scope({tenant},{legal},true);
            INSERT INTO main_bp.use_writer_scopes(tenant_id,legal_entity_key,primary_writer)
            VALUES ({tenant},{legal},'WORKSPACE');
            """);
        await tx.CommitAsync();
    }
}
