using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_forward_migration_preserves_existing_command_and_installs_no_mapping_authority()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var owner = server.Context(tenant);
        var name = "wp0403_forward_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync("CREATE DATABASE " + name);
        await using var legacy = server.Context(tenant);
        legacy.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(legacy.Database.GetConnectionString()) { Database = name }.ConnectionString);
        const string previous = "20261120052000_BusinessPartnerUseV1";
        const string mapping = "20261120114000_BusinessPartnerMappingV1";
        var migrator = legacy.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        legacy.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = name[..20], TenantName = "模拟既有资料迁移" });
        var existing = new BusinessPartner { BpCd = "0000001", BpName = "模拟既有原档", BaseCd = "BP0", CurrencyCd = "JPY" };
        legacy.BusinessPartners.Add(existing);
        await legacy.SaveChangesAsync();
        var command = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var priorReceipt = new
        {
            commandId = command, operation = "CREATE", outcome = "APPLIED",
            bp = new { bpId = existing.Id, bpCd = "0000001", legalEntityKey = MappingLegal, revision = "1", contentHash = (string?)null },
            mapping = (object?)null, reconcile = (object?)null, errorCode = (string?)null, completedAt = MappingInstant()
        };
        var oldReceipt = JsonSerializer.Serialize(new
        {
            priorReceipt.commandId, priorReceipt.operation, priorReceipt.outcome, priorReceipt.bp, priorReceipt.mapping,
            priorReceipt.reconcile, priorReceipt.errorCode, priorReceipt.completedAt, receiptHash = MappingHash(priorReceipt)
        }, MappingJson);
        var fingerprint = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldReceipt));
        var priorProfile = BusinessPartnerWorkspaceV1.Canonical(new { bpId = existing.Id, bpCd = existing.BpCd, revision = "1",
            state = new { registration = "PRE_REGISTERED", lifecycle = "ACTIVE", hold = "CLEAR" } });
        var encrypted = new EphemeralDataProtectionProvider().CreateProtector(BusinessPartnerWorkspaceV1.ProfileOwner,
            tenant.CurrentTenantId.ToString("D"), "revision", existing.Id.ToString("D")).Protect(priorProfile);
        await using (var seed = await legacy.Database.BeginTransactionAsync())
        {
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.scopes VALUES({tenant.CurrentTenantId},{MappingLegal},'模拟既有法人',true,2,'BP-NORM-U15.0-1',true);
                INSERT INTO main_bp.heads(tenant_id,bp_id,legal_entity_key,registration,revision)
                    VALUES({tenant.CurrentTenantId},{existing.Id},{MappingLegal},'PRE_REGISTERED',1);
                INSERT INTO main_bp.profile_revisions VALUES({tenant.CurrentTenantId},{MappingLegal},{existing.Id},1,
                    {existing.RowVersion!},{MappingSha(priorProfile)},{encrypted},{DateTimeOffset.UtcNow});
                INSERT INTO main_bp.commands(tenant_id,command_id,actor_id,legal_entity_key,bp_id,operation,fingerprint,receipt)
                    VALUES({tenant.CurrentTenantId},{command},{actor},{MappingLegal},{existing.Id},'CREATE',{fingerprint},{oldReceipt});
                """);
            await seed.CommitAsync();
        }
        Assert.Contains(mapping, await legacy.Database.GetPendingMigrationsAsync());
        await migrator.MigrateAsync(mapping);
        Assert.Contains(mapping, await legacy.Database.GetAppliedMigrationsAsync());
        var raw = await legacy.Database.SqlQuery<string>($"""
            SELECT receipt AS "Value" FROM main_bp.commands
             WHERE tenant_id={tenant.CurrentTenantId} AND command_id={command} AND bp_id={existing.Id} AND account_id IS NULL
               AND fingerprint={fingerprint}
            """).SingleAsync();
        Assert.Equal(oldReceipt, raw);
        Assert.Equal(new[] { 0, 0, 0 }, await legacy.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM main_bp.mapping_authorities
            UNION ALL SELECT count(*)::integer FROM main_bp.mapping_slots
            UNION ALL SELECT count(*)::integer FROM main_bp.mapping_use_guards
            """).ToArrayAsync());
        await DeviceEvidence("wp04-03-forward-migration.json", new { previous, mapping, existing.Id, command,
            exactExistingReceiptSurvives = true, defaultsGrantNoExternalAuthorities = true });
    }
}
